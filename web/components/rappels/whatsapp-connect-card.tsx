"use client"

import { useState } from "react"
import { Link2, Loader2, Smartphone } from "lucide-react"
import { toast } from "sonner"

import { Card, CardContent } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { statusToneClass } from "@/components/ui/status-tone"
import { showErrorToast } from "@/lib/errors"
import {
  useWhatsAppEmbeddedSignup,
  type EmbeddedSignupOutcome,
} from "@/lib/hooks/use-whatsapp-embedded-signup"
import { reminderSettingsApi } from "@/lib/api/reminder-settings"
import type { MessagingSenderState, ReminderAllowanceDto } from "@/lib/api/reminder-allowance"
import { cn } from "@/lib/utils"

/**
 * « Connecter WhatsApp » — US-1's guided connection, on the « Rappels » screen (AC-1.1).
 *
 * <p>⚠️ <b>AC-1.2's sentence comes BEFORE the flow starts, not after.</b> Meta sends the one-time code to the
 * practice's own handset, so an admin who begins this at a desk with the phone in a drawer downstairs has already
 * lost. It is a line of body text above the button, not a toast after it.</p>
 *
 * <p>⚠️ <b>AC-1.4's five states are stated in WORDS, from the server's own label.</b> Never re-derived here:
 * `MessagingSender.From` is the one derivation, and « connecté » must never be presented as « prêt à envoyer ».</p>
 *
 * <p>⚠️ <b>Absent, not disabled, where the flow cannot run</b> (AC-1.6, § 0): the parent renders nothing at all
 * where the deployment does not sell vendor messaging, and this renders no button where `canConnect` is false —
 * a control that cannot work, with no explanation the practice can act on, is worse than no control.</p>
 *
 * <p>⚠️ <b>No template editor, ever</b> (AC-1.3). The wording is submitted on the cabinet's behalf and its review is
 * reported here as a state; recovering a refused one is the vendor's action, which is why the refused state gives a
 * contact route rather than an « éditer le modèle » button.</p>
 */
export function WhatsAppConnectCard({
  data,
  isAdmin,
  onConnected,
}: {
  data: ReminderAllowanceDto | null
  /** AC-1.1 — the offer is an admin's. Presentation only; the endpoint checks it too. */
  isAdmin: boolean
  onConnected: () => void
}) {
  const [busy, setBusy] = useState(false)

  const finish = async (outcome: EmbeddedSignupOutcome) => {
    try {
      if (outcome.kind === "no-phone-number") {
        // FINISH_ONLY_WABA — a real completion with no number, so there is nothing to register. Its own sentence:
        // « annulé » would be false and would send the admin round the same loop expecting a different result.
        toast.error("Aucun numéro n'a été ajouté", {
          description:
            "Votre compte WhatsApp Business a bien été créé, mais sans numéro. Reprenez la connexion et ajoutez le numéro de la clinique.",
        })
        return
      }

      if (outcome.kind === "failed") {
        toast.error("Meta n'a pas pu terminer la connexion.", { description: "Veuillez réessayer." })
        return
      }

      if (outcome.kind === "cancelled") {
        toast.info("Connexion annulée.")
        return
      }

      await reminderSettingsApi.connectWhatsApp({
        code: outcome.code,
        wabaId: outcome.wabaId,
        phoneNumberId: outcome.phoneNumberId,
      })
      toast.success("WhatsApp connecté", {
        description: "Nous soumettons votre modèle de message à Meta — cela peut prendre jusqu'à 24 h.",
      })
      onConnected()
    } catch (error) {
      showErrorToast(error, "La connexion WhatsApp n'a pas pu être terminée.")
    } finally {
      setBusy(false)
    }
  }

  const signup = useWhatsAppEmbeddedSignup({
    enabled: Boolean(data?.canConnect),
    onOutcome: (outcome) => void finish(outcome),
  })

  if (!data) {
    return null
  }

  const state: MessagingSenderState = data.senderState

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-4 sm:p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <h2 className="flex items-center gap-2 text-base font-semibold">
            <Smartphone aria-hidden="true" className="size-4 shrink-0 text-muted-foreground" />
            Connexion WhatsApp
          </h2>
          <span
            className={cn(
              "shrink-0 rounded-full border px-2.5 py-1 text-xs font-medium",
              statusToneClass(state === "Ready" ? "positive" : "active"),
            )}
          >
            {data.senderStateLabel}
          </span>
        </div>

        <StateExplanation state={state} contactEmail={data.contactEmail} contactPhone={data.contactPhone} />

        {/* AC-1.1 — the offer, and only where it can actually run. A non-admin sees the state and no button. */}
        {state === "NotConnected" && isAdmin && data.canConnect && (
          <>
            {/* AC-1.2 — stated BEFORE the flow. See the ⚠️ on the component. */}
            <p className="text-sm text-muted-foreground">
              Meta enverra un code de vérification par SMS ou par appel <strong>sur le téléphone de la clinique</strong>.
              Ayez ce téléphone à portée de main avant de commencer.
            </p>
            <div>
              <Button
                onClick={() => {
                  if (!signup.sdkReady || !signup.configured) {
                    toast.error("La connexion Meta n'est pas encore prête, réessayez dans un instant.")
                    return
                  }
                  setBusy(true)
                  signup.start()
                }}
                disabled={busy}
                className="coarse:h-11"
              >
                {busy ? (
                  <Loader2 aria-hidden="true" className="mr-1.5 size-4 animate-spin" />
                ) : (
                  <Link2 aria-hidden="true" className="mr-1.5 size-4" />
                )}
                Connecter WhatsApp
              </Button>
            </div>
          </>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * AC-1.4/AC-1.5 — what each state means, in words. Five branches and no default: the union is closed server-side, so
 * a sixth state is a `tsc` error here rather than a silently blank card.
 */
function StateExplanation({
  state,
  contactEmail,
  contactPhone,
}: {
  state: MessagingSenderState
  contactEmail: string | null
  contactPhone: string | null
}) {
  switch (state) {
    case "NotConnected":
      return (
        <p className="text-sm text-muted-foreground">
          Connectez le numéro WhatsApp de la clinique pour envoyer les rappels par WhatsApp. Vous n&apos;avez ni compte
          Meta à créer, ni modèle de message à rédiger : nous nous en occupons.
        </p>
      )

    case "PendingReview":
      // AC-1.5 — the duration AND what happens to the reminders booked meanwhile.
      return (
        <p className="text-sm text-muted-foreground">
          Votre numéro est connecté. Meta vérifie maintenant le modèle de message, ce qui peut prendre
          <strong> jusqu&apos;à 24 h</strong>. Les rappels prévus d&apos;ici là sont <strong>mis en attente</strong> —
          rien n&apos;est perdu et rien n&apos;est décompté de votre forfait — et partiront dès la validation.
        </p>
      )

    case "Ready":
      return (
        <p className="text-sm text-muted-foreground">
          Votre numéro est connecté et votre modèle de message est validé : les rappels WhatsApp partent normalement.
        </p>
      )

    // FR-7/EC-10 — recovery is OURS, never the practice's, so this gives a contact route and no « modifier le
    // modèle » control. Paused and Disabled fold in here: from the practice's side they are the same fact.
    case "TemplateRefused":
      return (
        <ContactableExplanation contactEmail={contactEmail} contactPhone={contactPhone}>
          Meta a refusé ou suspendu le modèle de message de votre cabinet. Vos rappels WhatsApp sont en attente — rien
          n&apos;est perdu et rien n&apos;est décompté. C&apos;est à nous de le corriger.
        </ContactableExplanation>
      )

    case "Suspended":
      return (
        <ContactableExplanation contactEmail={contactEmail} contactPhone={contactPhone}>
          Meta a suspendu l&apos;envoi depuis ce numéro. Vos rendez-vous, vos dossiers et vos rappels SMS continuent
          normalement. C&apos;est à nous de le débloquer.
        </ContactableExplanation>
      )
  }
}

/** ⚠️ AC-2.7's rule again: an unpublished contact renders **no route at all**, never an empty `mailto:`. */
function ContactableExplanation({
  children,
  contactEmail,
  contactPhone,
}: {
  children: React.ReactNode
  contactEmail: string | null
  contactPhone: string | null
}) {
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-muted-foreground">{children}</p>
      {(contactEmail || contactPhone) && (
        <p className="text-sm">
          Contactez-nous :{" "}
          {contactEmail && (
            <a className="font-medium underline underline-offset-2" href={`mailto:${contactEmail}`}>
              {contactEmail}
            </a>
          )}
          {contactEmail && contactPhone && " · "}
          {contactPhone && (
            <a className="font-medium underline underline-offset-2" href={`tel:${contactPhone}`}>
              {contactPhone}
            </a>
          )}
        </p>
      )}
    </div>
  )
}
