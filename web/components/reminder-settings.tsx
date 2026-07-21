"use client"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { BellRing, Save, MessageSquare, CheckCircle2, Link2, Unlink, Loader2, AlertCircle } from "lucide-react"
import { useSession } from "@/lib/auth/session"
import {
  reminderSettingsApi,
  type ReminderSettingsDto,
  type UpdateReminderSettingsRequest,
  type WhatsAppConnectionStatus,
} from "@/lib/api/reminder-settings"

// Tri-state channel toggle: "inherit" (null = per-install default), "on" (true), "off" (false).
type Toggle = "inherit" | "on" | "off"

const toToggle = (value: boolean | null): Toggle => (value === null ? "inherit" : value ? "on" : "off")
const fromToggle = (value: Toggle): boolean | null => (value === "inherit" ? null : value === "on")

// Meta Embedded Signup config (public, non-secret). Empty in Local/unconfigured installs (the button no-ops).
const META_APP_ID = process.env.NEXT_PUBLIC_META_APP_ID ?? ""
const META_CONFIG_ID = process.env.NEXT_PUBLIC_META_CONFIG_ID ?? ""
const META_GRAPH_VERSION = "v21.0"
const FACEBOOK_ORIGINS = ["https://www.facebook.com", "https://web.facebook.com"]

// Data the Embedded-Signup popup posts back via the window "message" event.
type EmbeddedSignupData = { waba_id?: string; phone_number_id?: string }

interface FbLoginResponse {
  authResponse?: { code?: string } | null
  status?: string
}

interface FbSdk {
  init(params: { appId: string; autoLogAppEvents?: boolean; xfbml?: boolean; version: string }): void
  login(callback: (response: FbLoginResponse) => void, options?: Record<string, unknown>): void
}

declare global {
  interface Window {
    FB?: FbSdk
    fbAsyncInit?: () => void
  }
}

// Masks a WhatsApp phone-number id, showing only the last 4 digits.
const maskId = (id: string | null): string => (!id ? "" : id.length <= 4 ? id : `••••${id.slice(-4)}`)

/**
 * Admin-only "Rappels (SMS / WhatsApp)" card. Lets a clinic configure its own reminder channels, sender
 * identity and credentials, overriding the per-install defaults. Secrets are write-only: the fields show a
 * "configuré / non configuré" badge and stay blank on load — leaving them blank keeps the stored secret.
 * Mounted by ClinicSettings for admins (both Cloud and Local).
 */
export function ReminderSettings() {
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [smsEnabled, setSmsEnabled] = useState<Toggle>("inherit")
  const [whatsAppEnabled, setWhatsAppEnabled] = useState<Toggle>("inherit")
  const [smsSenderId, setSmsSenderId] = useState("")
  const [whatsAppPhoneNumberId, setWhatsAppPhoneNumberId] = useState("")
  const [whatsAppTemplateName, setWhatsAppTemplateName] = useState("")
  const [whatsAppTemplateLanguage, setWhatsAppTemplateLanguage] = useState("")
  const [smsApiKey, setSmsApiKey] = useState("")
  const [whatsAppAccessToken, setWhatsAppAccessToken] = useState("")
  const [smsApiKeyConfigured, setSmsApiKeyConfigured] = useState(false)
  const [whatsAppAccessTokenConfigured, setWhatsAppAccessTokenConfigured] = useState(false)

  // WhatsApp Embedded-Signup connection (Cloud only).
  const { mode } = useSession()
  const isCloud = mode === "cloud"
  const [waStatus, setWaStatus] = useState<WhatsAppConnectionStatus>("NotConnected")
  const [waConnectedNumber, setWaConnectedNumber] = useState<string | null>(null)
  const [waLastError, setWaLastError] = useState<string | null>(null)
  const [waBusy, setWaBusy] = useState(false)
  const [sdkReady, setSdkReady] = useState(false)
  const signupDataRef = useRef<EmbeddedSignupData | null>(null)

  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // Populates every field from a settings DTO (used on load and after connect/disconnect).
  const hydrate = (settings: ReminderSettingsDto) => {
    setSmsEnabled(toToggle(settings.smsEnabled))
    setWhatsAppEnabled(toToggle(settings.whatsAppEnabled))
    setSmsSenderId(settings.smsSenderId ?? "")
    setWhatsAppPhoneNumberId(settings.whatsAppPhoneNumberId ?? "")
    setWhatsAppTemplateName(settings.whatsAppTemplateName ?? "")
    setWhatsAppTemplateLanguage(settings.whatsAppTemplateLanguage ?? "")
    setSmsApiKeyConfigured(settings.smsApiKeyConfigured)
    setWhatsAppAccessTokenConfigured(settings.whatsAppAccessTokenConfigured)
    setWaStatus(settings.whatsAppConnectionStatus)
    setWaConnectedNumber(settings.whatsAppPhoneNumberId)
    setWaLastError(settings.whatsAppLastError)
  }

  useEffect(() => {
    const load = async () => {
      try {
        const settings = await reminderSettingsApi.get()
        if (!mounted.current) return
        hydrate(settings)
      } catch (err) {
        if (mounted.current) {
          setError(err instanceof Error ? err.message : "Échec du chargement des paramètres de rappel.")
        }
      } finally {
        if (mounted.current) setLoading(false)
      }
    }
    void load()
  }, [])

  // Load the Meta JS SDK once (Cloud + app id configured); captures the Embedded-Signup result via "message".
  useEffect(() => {
    if (!isCloud || !META_APP_ID) return

    const handleMessage = (event: MessageEvent) => {
      if (!FACEBOOK_ORIGINS.includes(event.origin)) return
      try {
        const parsed = JSON.parse(event.data as string)
        if (parsed?.type === "WA_EMBEDDED_SIGNUP" && parsed?.event === "FINISH") {
          signupDataRef.current = {
            waba_id: parsed?.data?.waba_id,
            phone_number_id: parsed?.data?.phone_number_id,
          }
        }
      } catch {
        // Non-JSON messages from other sources are ignored.
      }
    }
    window.addEventListener("message", handleMessage)

    if (window.FB) {
      setSdkReady(true)
    } else if (!document.getElementById("facebook-jssdk")) {
      window.fbAsyncInit = () => {
        window.FB?.init({ appId: META_APP_ID, autoLogAppEvents: true, xfbml: false, version: META_GRAPH_VERSION })
        if (mounted.current) setSdkReady(true)
      }
      const script = document.createElement("script")
      script.id = "facebook-jssdk"
      script.src = "https://connect.facebook.net/en_US/sdk.js"
      script.async = true
      script.defer = true
      script.crossOrigin = "anonymous"
      document.body.appendChild(script)
    }

    return () => window.removeEventListener("message", handleMessage)
  }, [isCloud])

  const finishConnect = async (response: FbLoginResponse) => {
    try {
      const code = response.authResponse?.code
      const data = signupDataRef.current
      if (!code || !data?.waba_id || !data?.phone_number_id) {
        // Popup closed / abandoned or an incomplete run — no-op.
        toast.info("Connexion annulée.")
        return
      }
      const updated = await reminderSettingsApi.connectWhatsApp({
        code,
        wabaId: data.waba_id,
        phoneNumberId: data.phone_number_id,
      })
      if (mounted.current) hydrate(updated)
      toast.success("WhatsApp connecté")
    } catch (err) {
      toast.error("Échec de la connexion WhatsApp", {
        description: err instanceof Error ? err.message : "Veuillez réessayer.",
      })
    } finally {
      if (mounted.current) setWaBusy(false)
      signupDataRef.current = null
    }
  }

  const handleConnect = () => {
    if (!window.FB || !sdkReady) {
      toast.error("Le SDK Meta n'est pas encore chargé, réessayez.")
      return
    }
    if (!META_CONFIG_ID) {
      toast.error("Configuration Meta manquante (config_id).")
      return
    }
    signupDataRef.current = null
    setWaBusy(true)
    window.FB.login((response) => void finishConnect(response), {
      config_id: META_CONFIG_ID,
      response_type: "code",
      override_default_response_type: true,
      extras: { setup: {}, sessionInfoVersion: "3" },
    })
  }

  const handleDisconnect = async () => {
    setWaBusy(true)
    try {
      const updated = await reminderSettingsApi.disconnectWhatsApp()
      if (mounted.current) hydrate(updated)
      toast.success("WhatsApp déconnecté")
    } catch (err) {
      toast.error("Échec de la déconnexion", {
        description: err instanceof Error ? err.message : "Veuillez réessayer.",
      })
    } finally {
      if (mounted.current) setWaBusy(false)
    }
  }

  const handleSave = async () => {
    setSaving(true)
    try {
      const payload: UpdateReminderSettingsRequest = {
        smsEnabled: fromToggle(smsEnabled),
        whatsAppEnabled: fromToggle(whatsAppEnabled),
        smsSenderId: smsSenderId.trim() || null,
        whatsAppPhoneNumberId: whatsAppPhoneNumberId.trim() || null,
        whatsAppTemplateName: whatsAppTemplateName.trim() || null,
        whatsAppTemplateLanguage: whatsAppTemplateLanguage.trim() || null,
      }
      // Secrets are write-only: only send them when the admin typed a new value (blank ⇒ unchanged).
      if (smsApiKey.trim()) payload.smsApiKey = smsApiKey.trim()
      if (whatsAppAccessToken.trim()) payload.whatsAppAccessToken = whatsAppAccessToken.trim()

      const updated = await reminderSettingsApi.update(payload)
      if (mounted.current) {
        setSmsApiKey("")
        setWhatsAppAccessToken("")
        setSmsApiKeyConfigured(updated.smsApiKeyConfigured)
        setWhatsAppAccessTokenConfigured(updated.whatsAppAccessTokenConfigured)
      }
      toast.success("Paramètres de rappel enregistrés")
    } catch (err) {
      const message = err instanceof Error ? err.message : "L'enregistrement a échoué."
      toast.error("Échec de l'enregistrement", { description: message })
    } finally {
      if (mounted.current) setSaving(false)
    }
  }

  const secretBadge = (configured: boolean) =>
    configured ? (
      <Badge variant="secondary" className="text-[10px] gap-1">
        <CheckCircle2 className="w-3 h-3 text-green-600" /> Configuré
      </Badge>
    ) : (
      <Badge variant="outline" className="text-[10px]">
        Non configuré
      </Badge>
    )

  return (
    <Card className="border border-gray-200 dark:border-slate-800">
      <CardHeader className="pb-3">
        <div className="flex items-center gap-2">
          <div className="w-1 h-6 bg-blue-600 rounded-full" />
          <CardTitle className="text-base flex items-center gap-2">
            <BellRing className="w-4 h-4 text-blue-600" />
            Rappels (SMS / WhatsApp)
          </CardTitle>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-xs text-muted-foreground">
          Configurez les canaux de rappel et l&apos;identité d&apos;expéditeur propres à cette clinique. Les
          champs laissés sur « Par défaut » ou vides utilisent la configuration de l&apos;installation. Les clés
          secrètes ne sont jamais réaffichées ; laissez-les vides pour conserver la valeur enregistrée.
        </p>

        {loading ? (
          <p className="text-xs text-muted-foreground">Chargement…</p>
        ) : error ? (
          <p className="text-xs text-red-600 dark:text-red-400">{error}</p>
        ) : (
          <>
            {/* SMS */}
            <div className="space-y-3 rounded-lg border border-gray-100 dark:border-slate-800 p-3">
              <div className="flex items-center gap-2 text-sm font-medium">
                <MessageSquare className="w-4 h-4 text-blue-600" />
                SMS
              </div>

              <div className="space-y-1">
                <Label className="text-xs font-medium">Canal</Label>
                <Select value={smsEnabled} onValueChange={(v) => setSmsEnabled(v as Toggle)} disabled={saving}>
                  <SelectTrigger className="h-8 text-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="inherit">Par défaut</SelectItem>
                    <SelectItem value="on">Activé</SelectItem>
                    <SelectItem value="off">Désactivé</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-1">
                <Label htmlFor="sms-sender-id" className="text-xs font-medium">
                  Identifiant d&apos;expéditeur
                </Label>
                <Input
                  id="sms-sender-id"
                  placeholder="Ex. MaClinique"
                  value={smsSenderId}
                  onChange={(e) => setSmsSenderId(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
              </div>

              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <Label htmlFor="sms-api-key" className="text-xs font-medium">
                    Clé API
                  </Label>
                  {secretBadge(smsApiKeyConfigured)}
                </div>
                <Input
                  id="sms-api-key"
                  type="password"
                  autoComplete="new-password"
                  placeholder={smsApiKeyConfigured ? "•••••••• (inchangée)" : "Saisir la clé API"}
                  value={smsApiKey}
                  onChange={(e) => setSmsApiKey(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
              </div>
            </div>

            {/* WhatsApp */}
            <div className="space-y-3 rounded-lg border border-gray-100 dark:border-slate-800 p-3">
              <div className="flex items-center gap-2 text-sm font-medium">
                <MessageSquare className="w-4 h-4 text-green-600" />
                WhatsApp
              </div>

              {isCloud && (
                <div className="space-y-2 rounded-lg border border-green-100 dark:border-green-900/40 bg-green-50/40 dark:bg-green-950/10 p-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-medium">Connexion (Embedded Signup)</span>
                    {waStatus === "Connected" ? (
                      <Badge variant="secondary" className="text-[10px] gap-1">
                        <CheckCircle2 className="w-3 h-3 text-green-600" /> Connecté
                      </Badge>
                    ) : waStatus === "Error" ? (
                      <Badge variant="outline" className="text-[10px] gap-1 text-red-600 border-red-300">
                        <AlertCircle className="w-3 h-3" /> Erreur
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-[10px]">
                        Non connecté
                      </Badge>
                    )}
                  </div>

                  {waStatus === "Connected" ? (
                    <>
                      <p className="text-xs text-muted-foreground">
                        Numéro connecté : <span className="font-mono">{maskId(waConnectedNumber)}</span>
                      </p>
                      <Button
                        variant="outline"
                        size="sm"
                        onClick={handleDisconnect}
                        disabled={waBusy}
                        className="h-8 text-xs"
                      >
                        {waBusy ? (
                          <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                        ) : (
                          <Unlink className="w-3.5 h-3.5 mr-1" />
                        )}
                        Déconnecter
                      </Button>
                    </>
                  ) : (
                    <>
                      <p className="text-xs text-muted-foreground">
                        Connectez le compte WhatsApp Business de la clinique en un clic via Meta.
                      </p>
                      {waLastError && <p className="text-xs text-red-600 dark:text-red-400">{waLastError}</p>}
                      <Button
                        size="sm"
                        onClick={handleConnect}
                        disabled={waBusy || !sdkReady}
                        className="h-8 text-xs bg-green-600 hover:bg-green-700"
                      >
                        {waBusy ? (
                          <Loader2 className="w-3.5 h-3.5 mr-1 animate-spin" />
                        ) : (
                          <Link2 className="w-3.5 h-3.5 mr-1" />
                        )}
                        Connecter WhatsApp
                      </Button>
                    </>
                  )}
                  <p className="text-[10px] text-muted-foreground">
                    Les champs ci-dessous restent disponibles comme méthode manuelle avancée.
                  </p>
                </div>
              )}

              <div className="space-y-1">
                <Label className="text-xs font-medium">Canal</Label>
                <Select
                  value={whatsAppEnabled}
                  onValueChange={(v) => setWhatsAppEnabled(v as Toggle)}
                  disabled={saving}
                >
                  <SelectTrigger className="h-8 text-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="inherit">Par défaut</SelectItem>
                    <SelectItem value="on">Activé</SelectItem>
                    <SelectItem value="off">Désactivé</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label htmlFor="wa-phone-id" className="text-xs font-medium">
                    Phone Number ID
                  </Label>
                  <Input
                    id="wa-phone-id"
                    placeholder="Ex. 123456789"
                    value={whatsAppPhoneNumberId}
                    onChange={(e) => setWhatsAppPhoneNumberId(e.target.value)}
                    disabled={saving}
                    className="h-8 text-sm"
                  />
                </div>
                <div className="space-y-1">
                  <Label htmlFor="wa-template-lang" className="text-xs font-medium">
                    Langue du modèle
                  </Label>
                  <Input
                    id="wa-template-lang"
                    placeholder="Ex. fr"
                    value={whatsAppTemplateLanguage}
                    onChange={(e) => setWhatsAppTemplateLanguage(e.target.value)}
                    disabled={saving}
                    className="h-8 text-sm"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label htmlFor="wa-template-name" className="text-xs font-medium">
                  Nom du modèle
                </Label>
                <Input
                  id="wa-template-name"
                  placeholder="Ex. appointment_reminder"
                  value={whatsAppTemplateName}
                  onChange={(e) => setWhatsAppTemplateName(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
              </div>

              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <Label htmlFor="wa-access-token" className="text-xs font-medium">
                    Jeton d&apos;accès
                  </Label>
                  {secretBadge(whatsAppAccessTokenConfigured)}
                </div>
                <Input
                  id="wa-access-token"
                  type="password"
                  autoComplete="new-password"
                  placeholder={whatsAppAccessTokenConfigured ? "•••••••• (inchangé)" : "Saisir le jeton d'accès"}
                  value={whatsAppAccessToken}
                  onChange={(e) => setWhatsAppAccessToken(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
              </div>
            </div>

            <div className="flex justify-end">
              <Button
                onClick={handleSave}
                size="sm"
                className="h-8 text-xs bg-blue-600 hover:bg-blue-700"
                disabled={saving}
              >
                <Save className="w-3.5 h-3.5 mr-1" />
                {saving ? "Enregistrement…" : "Enregistrer"}
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}
