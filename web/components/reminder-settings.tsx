"use client"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  BellRing,
  Save,
  MessageSquare,
  CheckCircle2,
  Link2,
  Unlink,
  Loader2,
  AlertCircle,
  AlertTriangle,
  Clock,
  XCircle,
  RefreshCw,
} from "lucide-react"
import { useSession } from "@/lib/auth/session"
import { formatDateTime } from "@/lib/format"
import {
  reminderSettingsApi,
  type ReminderSettingsDto,
  type UpdateReminderSettingsRequest,
  type WhatsAppConnectionStatus,
  type ReminderEffectiveStatus,
  type ReminderStatusDto,
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

  // Per-clinic overrides of previously per-install-only values + resolved effective status (AC-1/AC-2).
  const [smsApiUrl, setSmsApiUrl] = useState("")
  const [whatsAppApiUrl, setWhatsAppApiUrl] = useState("")
  const [leadTimeHours, setLeadTimeHours] = useState("")
  const [messageTemplateBody, setMessageTemplateBody] = useState("")
  const [smsEffectiveStatus, setSmsEffectiveStatus] = useState<ReminderEffectiveStatus>("not_configured")
  const [whatsAppEffectiveStatus, setWhatsAppEffectiveStatus] = useState<ReminderEffectiveStatus>("not_configured")

  // Delivery-status surface (AC-3): recent reminder outbox rows + their state.
  const [deliveryRows, setDeliveryRows] = useState<ReminderStatusDto[]>([])
  const [deliveryLoading, setDeliveryLoading] = useState(true)
  const [deliveryError, setDeliveryError] = useState<string | null>(null)

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
    setSmsApiUrl(settings.smsApiUrl ?? "")
    setWhatsAppApiUrl(settings.whatsAppApiUrl ?? "")
    setLeadTimeHours(settings.leadTimeHours?.join(", ") ?? "")
    setMessageTemplateBody(settings.messageTemplateBody ?? "")
    setSmsEffectiveStatus(settings.smsEffectiveStatus)
    setWhatsAppEffectiveStatus(settings.whatsAppEffectiveStatus)
    setWaStatus(settings.whatsAppConnectionStatus)
    setWaConnectedNumber(settings.whatsAppPhoneNumberId)
    setWaLastError(settings.whatsAppLastError)
  }

  // Parses the comma/space-separated lead-time field into positive hour tiers; null when empty (= inherit).
  const parseLeadTimes = (raw: string): number[] | null => {
    const values = raw
      .split(/[,\s]+/)
      .map((s) => Number.parseInt(s, 10))
      .filter((n) => Number.isFinite(n) && n > 0)
    return values.length > 0 ? values : null
  }

  const loadDelivery = async () => {
    setDeliveryLoading(true)
    setDeliveryError(null)
    try {
      const rows = await reminderSettingsApi.status()
      if (mounted.current) setDeliveryRows(rows)
    } catch (err) {
      if (mounted.current) {
        setDeliveryError(err instanceof Error ? err.message : "Échec du chargement du statut des rappels.")
      }
    } finally {
      if (mounted.current) setDeliveryLoading(false)
    }
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
    void loadDelivery()
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
        smsApiUrl: smsApiUrl.trim() || null,
        whatsAppApiUrl: whatsAppApiUrl.trim() || null,
        leadTimeHours: parseLeadTimes(leadTimeHours),
        messageTemplateBody: messageTemplateBody.trim() || null,
      }
      // Secrets are write-only: only send them when the admin typed a new value (blank ⇒ unchanged).
      if (smsApiKey.trim()) payload.smsApiKey = smsApiKey.trim()
      if (whatsAppAccessToken.trim()) payload.whatsAppAccessToken = whatsAppAccessToken.trim()

      const updated = await reminderSettingsApi.update(payload)
      if (mounted.current) {
        // Re-hydrate from the canonicalized response (effective status, deduped lead times, …) then clear
        // the write-only secret inputs.
        hydrate(updated)
        setSmsApiKey("")
        setWhatsAppAccessToken("")
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
      <Badge variant="secondary" className="text-2xs gap-1">
        <CheckCircle2 className="w-3 h-3 text-green-600" /> Configuré
      </Badge>
    ) : (
      <Badge variant="outline" className="text-2xs">
        Non configuré
      </Badge>
    )

  // Channel readiness (AC-2): only surfaced when the admin explicitly turned the channel on. Green = the
  // resolved settings + credentials make it sendable; amber = enabled but a URL/secret/template is missing.
  const readinessBadge = (toggle: Toggle, status: ReminderEffectiveStatus) => {
    if (toggle !== "on") return null
    return status === "configured" ? (
      <Badge variant="secondary" className="text-2xs gap-1">
        <CheckCircle2 className="w-3 h-3 text-green-600" /> Prêt à envoyer
      </Badge>
    ) : (
      <Badge variant="outline" className="text-2xs gap-1 text-amber-600 border-amber-400">
        <AlertTriangle className="w-3 h-3" /> Configuration incomplète
      </Badge>
    )
  }

  const deliveryStatusBadge = (status: ReminderStatusDto["status"]) => {
    if (status === "sent") {
      return (
        <Badge variant="secondary" className="text-2xs gap-1">
          <CheckCircle2 className="w-3 h-3 text-green-600" /> Envoyé
        </Badge>
      )
    }
    if (status === "failed") {
      return (
        <Badge variant="outline" className="text-2xs gap-1 text-red-600 border-red-300">
          <XCircle className="w-3 h-3" /> Échec
        </Badge>
      )
    }
    return (
      <Badge variant="outline" className="text-2xs gap-1 text-amber-600 border-amber-400">
        <Clock className="w-3 h-3" /> En attente
      </Badge>
    )
  }

  return (
    <Card className="border border-gray-200 dark:border-slate-800">
      <CardHeader className="pb-3">
        <div className="flex items-center gap-2">
          <div className="w-1 h-6 bg-primary rounded-full" />
          <CardTitle className="text-base flex items-center gap-2">
            <BellRing className="w-4 h-4 text-primary" />
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
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <MessageSquare className="w-4 h-4 text-primary" />
                  SMS
                </div>
                {readinessBadge(smsEnabled, smsEffectiveStatus)}
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
                <Label htmlFor="sms-api-url" className="text-xs font-medium">
                  URL de la passerelle SMS
                </Label>
                <Input
                  id="sms-api-url"
                  placeholder="Ex. https://api.sms-gateway.tn/send"
                  value={smsApiUrl}
                  onChange={(e) => setSmsApiUrl(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
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
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <MessageSquare className="w-4 h-4 text-green-600" />
                  WhatsApp
                </div>
                {readinessBadge(whatsAppEnabled, whatsAppEffectiveStatus)}
              </div>

              {isCloud && (
                <div className="space-y-2 rounded-lg border border-green-100 dark:border-green-900/40 bg-green-50/40 dark:bg-green-950/10 p-3">
                  <div className="flex items-center justify-between">
                    <span className="text-xs font-medium">Connexion (Embedded Signup)</span>
                    {waStatus === "Connected" && whatsAppEffectiveStatus === "configured" ? (
                      <Badge variant="secondary" className="text-2xs gap-1">
                        <CheckCircle2 className="w-3 h-3 text-green-600" /> Connecté
                      </Badge>
                    ) : waStatus === "Connected" ? (
                      // AC-2: OAuth is done but the resolved settings still can't send — warn instead of green.
                      <Badge variant="outline" className="text-2xs gap-1 text-amber-600 border-amber-400">
                        <AlertTriangle className="w-3 h-3" /> Connexion incomplète
                      </Badge>
                    ) : waStatus === "Error" ? (
                      <Badge variant="outline" className="text-2xs gap-1 text-red-600 border-red-300">
                        <AlertCircle className="w-3 h-3" /> Erreur
                      </Badge>
                    ) : (
                      <Badge variant="outline" className="text-2xs">
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
                  <p className="text-2xs text-muted-foreground">
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

              <div className="space-y-1">
                <Label htmlFor="wa-api-url" className="text-xs font-medium">
                  URL de base (Graph API)
                </Label>
                <Input
                  id="wa-api-url"
                  placeholder="Ex. https://graph.facebook.com/v21.0"
                  value={whatsAppApiUrl}
                  onChange={(e) => setWhatsAppApiUrl(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                <div className="space-y-1">
                  {/* AC-P3.52 — decided explicitly, not overlooked: « Phone Number ID » is the verbatim name
                      of a field in Meta's WhatsApp Business dashboard, so the operator has to match it
                      character-for-character when copying the value across. Translating it would make that
                      copy harder, not easier. The French gloss below carries the meaning. */}
                  <Label htmlFor="wa-phone-id" className="text-xs font-medium">
                    Phone Number ID
                    <span className="ml-1 font-normal text-muted-foreground">
                      (identifiant du numéro, tel qu&apos;affiché par Meta)
                    </span>
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

            {/* Programmation & message (partagé entre les canaux) */}
            <div className="space-y-3 rounded-lg border border-gray-100 dark:border-slate-800 p-3">
              <div className="flex items-center gap-2 text-sm font-medium">
                <Clock className="w-4 h-4 text-primary" />
                Programmation &amp; message
              </div>

              <div className="space-y-1">
                <Label htmlFor="lead-time-hours" className="text-xs font-medium">
                  Heures de rappel (avant le rendez-vous)
                </Label>
                <Input
                  id="lead-time-hours"
                  placeholder="Ex. 24, 6"
                  value={leadTimeHours}
                  onChange={(e) => setLeadTimeHours(e.target.value)}
                  disabled={saving}
                  className="h-8 text-sm"
                />
                <p className="text-2xs text-muted-foreground">
                  Séparez les paliers (heures) par des virgules. Vide = valeurs par défaut de l&apos;installation.
                </p>
              </div>

              <div className="space-y-1">
                <Label htmlFor="message-body" className="text-xs font-medium">
                  Message du rappel
                </Label>
                <Textarea
                  id="message-body"
                  placeholder="Ex. Rappel : {patient}, rendez-vous le {date} chez {clinic}."
                  value={messageTemplateBody}
                  onChange={(e) => setMessageTemplateBody(e.target.value)}
                  disabled={saving}
                  rows={3}
                  className="text-sm"
                />
                <p className="text-2xs text-muted-foreground">
                  Variables : {"{patient}"}, {"{date}"}, {"{clinic}"}. Vide = message par défaut.
                </p>
              </div>
            </div>

            <div className="flex justify-end">
              <Button
                onClick={handleSave}
                size="sm"
                className="h-8 text-xs bg-primary hover:bg-primary/90"
                disabled={saving}
              >
                <Save className="w-3.5 h-3.5 mr-1" />
                {saving ? "Enregistrement…" : "Enregistrer"}
              </Button>
            </div>

            {/*
              The delivery-status list that used to sit here has MOVED to the « Rappels » page (/rappels), which
              is built around it: filters by statut / canal / date, server-paged, and readable by all staff rather
              than admins only. It is deliberately NOT duplicated here — two renderings of the same outbox rows
              would drift, and the one buried at the bottom of a settings card was the weaker of the two.
            */}
          </>
        )}
      </CardContent>
    </Card>
  )
}
