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
  Mail,
} from "lucide-react"
import {
  useWhatsAppEmbeddedSignup,
  type EmbeddedSignupOutcome,
} from "@/lib/hooks/use-whatsapp-embedded-signup"
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

  // Outbound email (SMTP) — the channel that delivers generated documents by email.
  const [smtpHost, setSmtpHost] = useState("")
  const [smtpPort, setSmtpPort] = useState("")
  const [smtpUseTls, setSmtpUseTls] = useState<Toggle>("inherit")
  const [smtpUsername, setSmtpUsername] = useState("")
  const [smtpPassword, setSmtpPassword] = useState("")
  const [smtpPasswordConfigured, setSmtpPasswordConfigured] = useState(false)
  const [smtpFromAddress, setSmtpFromAddress] = useState("")
  const [smtpFromName, setSmtpFromName] = useState("")
  const [emailEffectiveStatus, setEmailEffectiveStatus] = useState<ReminderEffectiveStatus>("not_configured")

  // Delivery-status surface (AC-3): recent reminder outbox rows + their state.
  const [deliveryRows, setDeliveryRows] = useState<ReminderStatusDto[]>([])
  const [deliveryLoading, setDeliveryLoading] = useState(true)
  const [deliveryError, setDeliveryError] = useState<string | null>(null)

  // WhatsApp Embedded-Signup connection — vendor-provisioned only; see the note in the JSX below.
  const [waStatus, setWaStatus] = useState<WhatsAppConnectionStatus>("NotConnected")
  const [waConnectedNumber, setWaConnectedNumber] = useState<string | null>(null)
  const [waLastError, setWaLastError] = useState<string | null>(null)
  const [waBusy, setWaBusy] = useState(false)

  /**
   * AC-1.7 — where the vendor provisions WhatsApp, the three manual credential fields are NOT offered, and the
   * connection is owned by `whatsapp-connect-card` on « Rappels » rather than by this card. Served on the settings
   * DTO itself, so the form that hides a field and the handler that refuses it read one answer.
   */
  const [vendorManagedWhatsApp, setVendorManagedWhatsApp] = useState(false)

  const mounted = useRef(true)
  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // Populates every field from a settings DTO (used on load and after connect/disconnect).
  /**
   * The concurrency token, kept from whichever read produced the form's current values — and replaced from every
   * successful save's own response, so a second save in the same session does not 409 on the admin's own change.
   */
  const [version, setVersion] = useState<number | undefined>(undefined)

  const hydrate = (settings: ReminderSettingsDto) => {
    setVersion(settings.version)
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
    setSmtpHost(settings.smtpHost ?? "")
    setSmtpPort(settings.smtpPort != null ? String(settings.smtpPort) : "")
    setSmtpUseTls(toToggle(settings.smtpUseTls))
    setSmtpUsername(settings.smtpUsername ?? "")
    setSmtpPasswordConfigured(settings.smtpPasswordConfigured)
    setSmtpFromAddress(settings.smtpFromAddress ?? "")
    setSmtpFromName(settings.smtpFromName ?? "")
    setEmailEffectiveStatus(settings.emailEffectiveStatus)
    setWaStatus(settings.whatsAppConnectionStatus)
    setWaConnectedNumber(settings.whatsAppPhoneNumberId)
    setWaLastError(settings.whatsAppLastError)
    setVendorManagedWhatsApp(settings.whatsAppVendorManaged)
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

  /*
   * The Embedded-Signup flow lives in `useWhatsAppEmbeddedSignup` (§ 31/§ 38) — extracted, not copied, because the
   * vendor-managed connect card on « Rappels » runs the same five-outcome protocol. v3 → v4 and the four finish
   * types this used to drop are that hook's business now.
   */
  const finishConnect = async (outcome: EmbeddedSignupOutcome) => {
    try {
      if (outcome.kind === "no-phone-number") {
        toast.error("Aucun numéro ajouté", {
          description:
            "Le compte WhatsApp Business a été créé sans numéro. Reprenez la connexion et ajoutez le numéro du cabinet.",
        })
        return
      }

      if (outcome.kind !== "connected") {
        toast.info(outcome.kind === "failed" ? "Meta a signalé une erreur." : "Connexion annulée.")
        return
      }

      const updated = await reminderSettingsApi.connectWhatsApp({
        code: outcome.code,
        wabaId: outcome.wabaId,
        phoneNumberId: outcome.phoneNumberId,
      })
      if (mounted.current) hydrate(updated)
      toast.success("WhatsApp connecté")
    } catch (err) {
      toast.error("Échec de la connexion WhatsApp", {
        description: err instanceof Error ? err.message : "Veuillez réessayer.",
      })
    } finally {
      if (mounted.current) setWaBusy(false)
    }
  }

  const embeddedSignup = useWhatsAppEmbeddedSignup({
    enabled: vendorManagedWhatsApp,
    onOutcome: (outcome) => void finishConnect(outcome),
  })

  const handleConnect = () => {
    if (!embeddedSignup.sdkReady) {
      toast.error("Le SDK Meta n'est pas encore chargé, réessayez.")
      return
    }
    if (!embeddedSignup.configured) {
      toast.error("Configuration Meta manquante (config_id).")
      return
    }
    setWaBusy(true)
    embeddedSignup.start()
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
        version,
        smsEnabled: fromToggle(smsEnabled),
        whatsAppEnabled: fromToggle(whatsAppEnabled),
        smsSenderId: smsSenderId.trim() || null,
        /*
          AC-1.7 — where the vendor provisions WhatsApp, its four identity fields are sent as `null` and NOT from
          state. They are still hydrated (the server returns them) and the fields are simply not rendered, so
          without this an ordinary SMS save would post the stored Phone Number ID back and the handler would refuse
          the whole request under `messaging_whatsapp_is_vendor_managed`. The server keeps what it has stored.
        */
        whatsAppPhoneNumberId: vendorManagedWhatsApp ? null : whatsAppPhoneNumberId.trim() || null,
        whatsAppTemplateName: vendorManagedWhatsApp ? null : whatsAppTemplateName.trim() || null,
        whatsAppTemplateLanguage: vendorManagedWhatsApp ? null : whatsAppTemplateLanguage.trim() || null,
        smsApiUrl: smsApiUrl.trim() || null,
        whatsAppApiUrl: vendorManagedWhatsApp ? null : whatsAppApiUrl.trim() || null,
        leadTimeHours: parseLeadTimes(leadTimeHours),
        messageTemplateBody: messageTemplateBody.trim() || null,
        smtpHost: smtpHost.trim() || null,
        // A blank or unparseable port means "inherit", never 0 — the server treats a non-positive port as unset.
        smtpPort: Number.parseInt(smtpPort, 10) > 0 ? Number.parseInt(smtpPort, 10) : null,
        smtpUseTls: fromToggle(smtpUseTls),
        smtpUsername: smtpUsername.trim() || null,
        smtpFromAddress: smtpFromAddress.trim() || null,
        smtpFromName: smtpFromName.trim() || null,
      }
      // Secrets are write-only: only send them when the admin typed a new value (blank ⇒ unchanged).
      if (smsApiKey.trim()) payload.smsApiKey = smsApiKey.trim()
      if (whatsAppAccessToken.trim()) payload.whatsAppAccessToken = whatsAppAccessToken.trim()
      if (smtpPassword.trim()) payload.smtpPassword = smtpPassword.trim()

      const updated = await reminderSettingsApi.update(payload)
      if (mounted.current) {
        // Re-hydrate from the canonicalized response (effective status, deduped lead times, …) then clear
        // the write-only secret inputs.
        hydrate(updated)
        setSmsApiKey("")
        setWhatsAppAccessToken("")
        setSmtpPassword("")
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
        <CheckCircle2 className="w-3 h-3 text-success" /> Configuré
      </Badge>
    ) : (
      <Badge variant="outline" className="text-2xs">
        Non configuré
      </Badge>
    )

  /*
   * Channel readiness (AC-2): only surfaced when the admin explicitly turned the channel on. Green = the
   * resolved settings + credentials make it sendable; amber = enabled but a URL/secret/template is missing.
   *
   * ⚠️ The amber badges are `text-warning-ink`, not `text-warning`: `--warning` sits at L 0.62, which lands
   * near 3.5:1 against a light surface — under the floor for 11px badge text. `--warning-ink` is the
   * darkened step that exists for exactly this (see `ui/status-tone.ts`). The badges stay `variant="outline"`
   * rather than routing through `statusToneClass`, because these three sit *inline beside a field label*
   * and a filled pill there reads as a second control; the tone map is for status columns.
   */
  const readinessBadge = (toggle: Toggle, status: ReminderEffectiveStatus) => {
    if (toggle !== "on") return null
    return status === "configured" ? (
      <Badge variant="secondary" className="text-2xs gap-1">
        <CheckCircle2 className="w-3 h-3 text-success" /> Prêt à envoyer
      </Badge>
    ) : (
      <Badge variant="outline" className="text-2xs gap-1 text-warning-ink border-warning/40">
        <AlertTriangle className="w-3 h-3" /> Configuration incomplète
      </Badge>
    )
  }

  const deliveryStatusBadge = (status: ReminderStatusDto["status"]) => {
    if (status === "sent") {
      return (
        <Badge variant="secondary" className="text-2xs gap-1">
          <CheckCircle2 className="w-3 h-3 text-success" /> Envoyé
        </Badge>
      )
    }
    if (status === "failed") {
      return (
        <Badge variant="outline" className="text-2xs gap-1 text-destructive border-destructive/40">
          <XCircle className="w-3 h-3" /> Échec
        </Badge>
      )
    }
    return (
      <Badge variant="outline" className="text-2xs gap-1 text-warning-ink border-warning/40">
        <Clock className="w-3 h-3" /> En attente
      </Badge>
    )
  }

  return (
    // No border override: `Card` already renders `border`, painted `--border` by the base layer.
    <Card>
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
          Configurez les canaux de rappel et l&apos;identité d&apos;expéditeur propres à ce cabinet. Les
          champs laissés sur « Par défaut » ou vides utilisent la configuration de l&apos;installation. Les clés
          secrètes ne sont jamais réaffichées ; laissez-les vides pour conserver la valeur enregistrée.
        </p>

        {/* ⚠️ Every `<Input>`/`<Textarea>` below says `md:text-sm`, never a bare `text-sm`: `ui/input.tsx`
            ships `text-base md:text-sm` as the iOS focus-zoom guard (Safari zooms into any field under 16px
            and never zooms back), and tailwind-merge treats an unprefixed size at the call site as a
            REPLACEMENT for `text-base` — so the class written to make a field compact disarms the guard. */}
        {loading ? (
          <p className="text-xs text-muted-foreground">Chargement…</p>
        ) : error ? (
          <p className="text-xs text-destructive">{error}</p>
        ) : (
          <>
            {/* SMS */}
            <div className="space-y-3 rounded-lg border p-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <MessageSquare className="w-4 h-4 text-primary" />
                  SMS
                </div>
                {readinessBadge(smsEnabled, smsEffectiveStatus)}
              </div>

              <div className="space-y-1">
                {/* ⚠️ `htmlFor` + `id`, and the accessible name says WHICH channel. Two labels reading « Canal »
                    with no association left both comboboxes anonymous — a screen reader announced « Par défaut,
                    liste » twice with nothing to tell them apart, on the control that turns a channel off. Every
                    text field beside them was already labelled properly. */}
                <Label htmlFor="sms-channel-toggle" className="text-xs font-medium">
                  Canal
                </Label>
                <Select value={smsEnabled} onValueChange={(v) => setSmsEnabled(v as Toggle)} disabled={saving}>
                  <SelectTrigger id="sms-channel-toggle" aria-label="Canal SMS" className="h-8 text-sm">
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
                  className="h-8 md:text-sm"
                />
              </div>

              <div className="space-y-1">
                <Label htmlFor="sms-sender-id" className="text-xs font-medium">
                  Identifiant d&apos;expéditeur
                </Label>
                <Input
                  id="sms-sender-id"
                  placeholder="Ex. MonCabinet"
                  value={smsSenderId}
                  onChange={(e) => setSmsSenderId(e.target.value)}
                  disabled={saving}
                  className="h-8 md:text-sm"
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
                  className="h-8 md:text-sm"
                />
              </div>
            </div>

            {/* WhatsApp */}
            <div className="space-y-3 rounded-lg border p-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <MessageSquare className="w-4 h-4 text-success" />
                  WhatsApp
                </div>
                {readinessBadge(whatsAppEnabled, whatsAppEffectiveStatus)}
              </div>

              {/* ⚠️ The manual « Connexion (Embedded Signup) » panel that stood here is gone with the Auth0
                  deployment kind. It was gated on `mode === "cloud"`, which had ALREADY been false on every
                  shipped deployment — both compose files set AUTH_MODE=local — so it was dead UI before this
                  change rather than because of it. Neither remaining profile wants it: SelfHostedLan answers
                  `ExposesMetaOnboarding` false and 404s the two endpoints, and on HostedMultiTenant the vendor
                  owns the connection through `whatsapp-connect-card` on « Rappels ». The manual credential
                  fields below remain the advanced path. */}

              <div className="space-y-1">
                {/* Same fix as the SMS channel above. */}
                <Label htmlFor="whatsapp-channel-toggle" className="text-xs font-medium">
                  Canal
                </Label>
                <Select
                  value={whatsAppEnabled}
                  onValueChange={(v) => setWhatsAppEnabled(v as Toggle)}
                  disabled={saving}
                >
                  <SelectTrigger id="whatsapp-channel-toggle" aria-label="Canal WhatsApp" className="h-8 text-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="inherit">Par défaut</SelectItem>
                    <SelectItem value="on">Activé</SelectItem>
                    <SelectItem value="off">Désactivé</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              {/*
                AC-1.7 — the three fields that ARE the cabinet's WhatsApp credentials (endpoint, Phone Number ID,
                access token) are absent where the vendor provisions them, and `UpdateClinicReminderSettingsCommand`
                refuses them server-side. Absent rather than disabled: a greyed field still says « this is yours to
                fill in one day », which is the opposite of true here.
              */}
              {vendorManagedWhatsApp ? (
                <p className="rounded-lg border border-primary/25 bg-primary/5 p-3 text-xs text-muted-foreground">
                  Les identifiants WhatsApp de ce cabinet sont fournis et gérés par nous. Utilisez
                  « Connecter WhatsApp » sur la page « Rappels » — votre numéro et votre modèle de message sont
                  configurés pour vous.
                </p>
              ) : (
                <>
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
                  className="h-8 md:text-sm"
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
                    className="h-8 md:text-sm"
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
                    className="h-8 md:text-sm"
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
                  className="h-8 md:text-sm"
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
                  className="h-8 md:text-sm"
                />
              </div>
                </>
              )}
            </div>

            {/* Programmation & message (partagé entre les canaux) */}
            <div className="space-y-3 rounded-lg border p-3">
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
                  className="h-8 md:text-sm"
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
                  className="md:text-sm"
                />
                <p className="text-2xs text-muted-foreground">
                  Variables : {"{patient}"}, {"{date}"}, {"{clinic}"}. Vide = message par défaut.
                </p>
              </div>
            </div>

            {/*
              Outbound email (SMTP) — the channel that delivers generated documents (ordonnances, lettres de
              liaison, factures, devis, reçus). It lives in this card because it is the cabinet's third outbound
              channel, configured the same way as the two above; there is deliberately no on/off toggle, because
              a host and a from-address ARE the enable — a channel switched "on" with no server would be a
              promise the dispatcher cannot keep.
            */}
            <div className="space-y-3 rounded-lg border p-3">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-sm font-medium">
                  <Mail className="w-4 h-4 text-primary" />
                  Email (envoi de documents)
                </div>
                {emailEffectiveStatus === "configured" ? (
                  <Badge variant="secondary" className="text-2xs gap-1">
                    <CheckCircle2 className="w-3 h-3 text-success" /> Prêt à envoyer
                  </Badge>
                ) : (
                  <Badge variant="outline" className="text-2xs gap-1 text-warning-ink border-warning/40">
                    <AlertTriangle className="w-3 h-3" /> Configuration incomplète
                  </Badge>
                )}
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor="smtp-host" className="text-xs font-medium">
                    Serveur SMTP
                  </Label>
                  <Input
                    id="smtp-host"
                    placeholder="Ex. smtp.gmail.com"
                    value={smtpHost}
                    onChange={(e) => setSmtpHost(e.target.value)}
                    disabled={saving}
                    className="h-8 md:text-sm"
                  />
                </div>

                <div className="space-y-1">
                  <Label htmlFor="smtp-port" className="text-xs font-medium">
                    Port
                  </Label>
                  <Input
                    id="smtp-port"
                    type="number"
                    inputMode="numeric"
                    placeholder="587"
                    value={smtpPort}
                    onChange={(e) => setSmtpPort(e.target.value)}
                    disabled={saving}
                    className="h-8 md:text-sm"
                  />
                </div>
              </div>

              <div className="space-y-1">
                {/* Associated, like the two channel toggles above — same unnamed-combobox defect. */}
                <Label htmlFor="smtp-tls-toggle" className="text-xs font-medium">
                  Chiffrement TLS
                </Label>
                <Select value={smtpUseTls} onValueChange={(v) => setSmtpUseTls(v as Toggle)} disabled={saving}>
                  <SelectTrigger id="smtp-tls-toggle" aria-label="Chiffrement TLS" className="h-8 text-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="inherit">Par défaut</SelectItem>
                    <SelectItem value="on">Activé</SelectItem>
                    <SelectItem value="off">Désactivé</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                <div className="space-y-1">
                  <Label htmlFor="smtp-from-address" className="text-xs font-medium">
                    Adresse d&apos;expédition
                  </Label>
                  <Input
                    id="smtp-from-address"
                    type="email"
                    placeholder="Ex. contact@moncabinet.tn"
                    value={smtpFromAddress}
                    onChange={(e) => setSmtpFromAddress(e.target.value)}
                    disabled={saving}
                    className="h-8 md:text-sm"
                  />
                </div>

                <div className="space-y-1">
                  <Label htmlFor="smtp-from-name" className="text-xs font-medium">
                    Nom d&apos;expédition
                  </Label>
                  <Input
                    id="smtp-from-name"
                    placeholder="Ex. Cabinet Dr Ben Salah"
                    value={smtpFromName}
                    onChange={(e) => setSmtpFromName(e.target.value)}
                    disabled={saving}
                    className="h-8 md:text-sm"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label htmlFor="smtp-username" className="text-xs font-medium">
                  Nom d&apos;utilisateur
                </Label>
                <Input
                  id="smtp-username"
                  placeholder="Souvent identique à l'adresse d'expédition"
                  value={smtpUsername}
                  onChange={(e) => setSmtpUsername(e.target.value)}
                  disabled={saving}
                  className="h-8 md:text-sm"
                />
                <p className="text-2xs text-muted-foreground">
                  Laissez vide pour un relais SMTP local sans authentification.
                </p>
              </div>

              <div className="space-y-1">
                <div className="flex items-center justify-between">
                  <Label htmlFor="smtp-password" className="text-xs font-medium">
                    Mot de passe
                  </Label>
                  {secretBadge(smtpPasswordConfigured)}
                </div>
                <Input
                  id="smtp-password"
                  type="password"
                  placeholder={smtpPasswordConfigured ? "•••••••• (inchangé)" : "Mot de passe SMTP"}
                  value={smtpPassword}
                  onChange={(e) => setSmtpPassword(e.target.value)}
                  disabled={saving}
                  className="h-8 md:text-sm"
                />
                <p className="text-2xs text-muted-foreground">
                  Stocké chiffré. Laissez vide pour conserver le mot de passe déjà enregistré.
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
