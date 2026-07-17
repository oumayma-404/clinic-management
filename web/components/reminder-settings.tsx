"use client"

import { useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Badge } from "@/components/ui/badge"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { BellRing, Save, MessageSquare, CheckCircle2 } from "lucide-react"
import { reminderSettingsApi, type UpdateReminderSettingsRequest } from "@/lib/api/reminder-settings"

// Tri-state channel toggle: "inherit" (null = per-install default), "on" (true), "off" (false).
type Toggle = "inherit" | "on" | "off"

const toToggle = (value: boolean | null): Toggle => (value === null ? "inherit" : value ? "on" : "off")
const fromToggle = (value: Toggle): boolean | null => (value === "inherit" ? null : value === "on")

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

  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    const load = async () => {
      try {
        const settings = await reminderSettingsApi.get()
        if (!mounted.current) return
        setSmsEnabled(toToggle(settings.smsEnabled))
        setWhatsAppEnabled(toToggle(settings.whatsAppEnabled))
        setSmsSenderId(settings.smsSenderId ?? "")
        setWhatsAppPhoneNumberId(settings.whatsAppPhoneNumberId ?? "")
        setWhatsAppTemplateName(settings.whatsAppTemplateName ?? "")
        setWhatsAppTemplateLanguage(settings.whatsAppTemplateLanguage ?? "")
        setSmsApiKeyConfigured(settings.smsApiKeyConfigured)
        setWhatsAppAccessTokenConfigured(settings.whatsAppAccessTokenConfigured)
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
