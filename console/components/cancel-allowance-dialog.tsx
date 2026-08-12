"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import {
  MESSAGING_ALLOWANCE_ALREADY_CANCELLED_CODE,
  type PlatformMessagingCancellationPreview,
  type PlatformMessagingCancelled,
  type PlatformMessagingEntry,
} from "@/lib/api/platform";
import { formatDate, formatMoney } from "@/lib/format";

/**
 * « Annuler cette allocation » (`vendor-whatsapp-messaging-quota` US-7).
 *
 * ⚠️ **The consequence is stated before the vendor commits, and it is not computed here** (AC-7.3). Every figure in the
 * sentence comes from `entry.ifCancelled`, which the server produced by re-folding the cabinet's real ledger with this one
 * allocation marked cancelled. The tempting client-side version — « le forfait actuel moins ce nombre » — is wrong for a
 * **forfait mensuel**, which replaces rather than adds: cancelling one hands the month back to whatever earlier monthly
 * figure was in force, which may be higher, lower, or absent entirely.
 *
 * ⚠️ **This reaches the CURRENT month, unlike a lowering** (AC-7.4/7.4a), so the practice's reminders can start being
 * held the moment the vendor clicks. That is the sentence this dialog exists to put in front of them — and it is why the
 * « épuisé » case gets its own wording rather than a date.
 *
 * ⚠️ **Consumption is untouched. Nothing is unsent and nothing is clawed back**, which the confirmation says out loud:
 * a vendor who reads « le forfait passe de 500 à 200 » beside « 260 déjà envoyés » can see for themselves that the month
 * ends up over, and that is a more trustworthy statement than a verdict on its own.
 *
 * ⚠️ **The motif is mandatory** (AC-7.1) and the server refuses a blank one in French. `required` here too, so the
 * ordinary case never costs a round trip — but the client check is a courtesy and the server is the guard.
 *
 * ⚠️ **Nothing is deleted.** The allocation stays on the fiche, struck through and marked « Annulée » in words, with this
 * motif on it (AC-7.2) — which is why this dialog says « annuler », never « supprimer ».
 */
export function CancelAllowanceDialog({
  clinicId,
  clinicName,
  entry,
}: {
  clinicId: string;
  clinicName: string;
  entry: PlatformMessagingEntry;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cancelled, setCancelled] = useState<PlatformMessagingCancelled | null>(null);
  const [reason, setReason] = useState("");

  const amount = entry.amountDt === null ? "forfait offert" : formatMoney(entry.amountDt);

  function openChanged(next: boolean) {
    if (next) {
      setError(null);
      setCancelled(null);
      setReason("");
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape, the close control and the overlay tap all arrive here. It only asks when a motif has actually been typed —
    // a prompt on an untouched form is the one people learn to dismiss without reading.
    if (
      reason.trim() !== ""
      && cancelled === null
      && !window.confirm("Abandonner cette annulation ? Le motif saisi sera perdu.")
    ) {
      return;
    }

    setOpen(false);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const response = await fetch("/bff/forfaits/annulations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, entryId: entry.entryId, reason: reason.trim() }),
      });

      // ⚠️ Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is the
      // only fact left when a body is unreadable.
      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformMessagingCancelled> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'annulation (${response.status}).`);
        // An allocation somebody else has already struck through is a fact about the ledger, not a failed request: the
        // fiche is re-read so its motif and its author appear, while the refusal stays on screen to explain why nothing
        // happened here.
        if (body.code === MESSAGING_ALLOWANCE_ALREADY_CANCELLED_CODE) {
          router.refresh();
        }
        return;
      }

      setCancelled(body as PlatformMessagingCancelled);
      setReason("");
      // The fiche re-reads: the month's figures and the allocation's own « Annulée » line both move with this write.
      router.refresh();
    } catch {
      setError("Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Sheet open={open} onOpenChange={openChanged}>
      <SheetTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="sm"
          aria-label={`Annuler l'allocation de ${entry.messages} rappels du ${formatDate(entry.recordedOn)} (${amount})`}
        >
          Annuler cette allocation
        </Button>
      </SheetTrigger>

      {/*
        `dvh`, never `vh`: a `vh`-sized panel does not shrink when the keyboard opens and the pinned footer goes off
        screen. The width override is `lg:`-prefixed because that is this application's own presentation boundary.
      */}
      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Annuler cette allocation</SheetTitle>
            {/* The cabinet, the kind and the amount, so two open tabs cannot cancel the wrong one. */}
            <SheetDescription>
              {clinicName} · {entry.kindLabel} · {entry.messages} rappels · {amount} · enregistrée le{" "}
              {formatDate(entry.recordedOn)}
            </SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {cancelled ? <Outcome cancelled={cancelled} /> : <Consequence preview={entry.ifCancelled} />}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            {cancelled ? null : (
              <div className="space-y-1.5">
                <Label htmlFor={`${fieldId}-reason`}>Motif de l&apos;annulation</Label>
                {/* A native textarea with the Input's own classes: a motif is a sentence, and `text-base` keeps it at
                    16 px so a phone does not zoom on focus. */}
                <textarea
                  id={`${fieldId}-reason`}
                  required
                  rows={3}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  disabled={submitting}
                  placeholder="Ex. : complément enregistré sur le mauvais cabinet"
                  className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                />
                <p className="text-xs text-muted-foreground">
                  Obligatoire. Il reste inscrit sur l&apos;allocation et explique, plus tard, pourquoi le forfait de ce
                  cabinet a diminué — y compris pour le mois en cours.
                </p>
              </div>
            )}
          </div>

          {/* Pinned: a `shrink-0` sibling of the scrolling body, so it stays on screen with the keyboard open. */}
          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {cancelled ? null : (
              <Button type="submit" variant="destructive" disabled={submitting}>
                {submitting ? "Annulation…" : "Annuler l'allocation"}
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {cancelled ? "Fermer" : "Revenir"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * AC-7.3/7.4 — what will happen, said before the vendor commits.
 *
 * ⚠️ **Every value is the server's fold.** The three cases are genuinely different statements rather than one sentence
 * with a number in it: a cabinet that keeps sending, one whose reminders start being held immediately, and one whose
 * forfait record disappears entirely — which is our own bookkeeping fault and reads differently from a limit reached.
 */
function Consequence({ preview }: { preview: PlatformMessagingCancellationPreview | null }) {
  if (!preview) {
    return (
      <p className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="note">
        Cette allocation est déjà annulée : son motif et son auteur figurent sur la fiche.
      </p>
    );
  }

  return (
    <div
      className={
        preview.exhausted
          ? "rounded-md border border-destructive/40 p-3 text-sm"
          : "rounded-md border border-border bg-muted/40 p-3 text-sm"
      }
      role="note"
    >
      <p className="font-medium">
        {preview.allowance === null
          ? "Après l'annulation, ce cabinet n'aura plus aucun forfait de rappels enregistré."
          : `Après l'annulation, son forfait de ce mois passera à ${preview.allowance} rappels.`}
      </p>

      {/* Consumption beside the new figure, so « épuisé » is something the vendor can check rather than take on trust. */}
      {preview.consumed === null ? (
        <p className="mt-1 text-muted-foreground">
          Ce mois-ci n&apos;est pas mesuré : nous ne savons pas combien de rappels ont déjà été envoyés.
        </p>
      ) : (
        <p className="mt-1 text-muted-foreground">
          {preview.consumed} rappels ont déjà été envoyés ce mois-ci — ce chiffre ne change pas : rien n&apos;est
          désenvoyé et rien n&apos;est récupéré.
        </p>
      )}

      <p className="mt-1 text-muted-foreground">
        {preview.allowance === null
          ? "Ses rappels WhatsApp seront mis en attente, et son écran « Rappels » indiquera de nous contacter."
          : preview.exhausted
            ? "Son forfait sera épuisé : ses rappels WhatsApp seront mis en attente dès maintenant, sans rien consommer. Son agenda, ses dossiers et ses rappels SMS continuent normalement."
            : `Il lui restera ${preview.remaining} rappels : ses envois continuent normalement.`}
      </p>
    </div>
  );
}

/**
 * What happened, read back from the server rather than from the preview the vendor confirmed — the ledger may have moved
 * between the page render and the click, and this is the answer that is true now.
 */
function Outcome({ cancelled }: { cancelled: PlatformMessagingCancelled }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">Allocation annulée.</p>

      <p className="mt-1 text-muted-foreground">
        Forfait de ce mois : {cancelled.allowanceThisMonth === null ? "aucun" : cancelled.allowanceThisMonth} rappels
        {cancelled.previousAllowanceThisMonth === null
          ? ""
          : ` (auparavant ${cancelled.previousAllowanceThisMonth})`}
        {cancelled.consumedThisMonth === null
          ? ""
          : ` · ${cancelled.consumedThisMonth} déjà envoyés, inchangé`}
      </p>

      {cancelled.exhaustedThisMonth ? (
        <p className="mt-1 text-muted-foreground">
          Le forfait de ce cabinet est désormais épuisé pour le mois en cours : ses rappels WhatsApp sont en attente.
          Son agenda, ses dossiers et ses rappels SMS continuent normalement.
        </p>
      ) : null}

      <p className="mt-1 text-muted-foreground">
        L&apos;allocation reste dans le journal, barrée, avec son motif — rien n&apos;a été supprimé.
      </p>
    </div>
  );
}
