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
  PERIOD_ALREADY_CANCELLED_CODE,
  type PlatformCancellationPreview,
  type PlatformPeriodCancelled,
  type PlatformSubscriptionEntry,
} from "@/lib/api/platform";
import { formatDate, formatMoney } from "@/lib/format";

/**
 * « Annuler cette période » (`platform-console` US-5) — the console's second write.
 *
 * ⚠️ **The consequence is stated before the vendor commits, and it is not computed here** (AC-5.3, EC-7). Every
 * figure in the sentence comes from `entry.ifCancelled`, which the server produced by re-folding the cabinet's real
 * ledger with this one entry marked cancelled. The tempting client-side version — « la fin moins la durée » — is
 * wrong whenever the entry is not the latest one, which is exactly the case a correction is for.
 *
 * ⚠️ **The confirmation names the cabinet AND the amount** (AC-5.4). With several tabs open the vendor is looking at
 * several practices, and « annuler cette période ? » on its own is a question about no particular one. « Offert »
 * carries no amount rather than 0,000 DT — the two are different statements.
 *
 * ⚠️ **The motif is mandatory** (AC-5.1) and the server refuses a blank one in French. The field is `required` here
 * too, so the ordinary case never costs a round trip — but the client check is a courtesy and the server is the
 * guard, exactly as the upload pre-check is.
 *
 * ⚠️ **Nothing is deleted.** The entry stays on the fiche, struck through and marked « Annulé » in words, with this
 * motif on it (AC-5.2) — which is why this dialog says « annuler », never « supprimer ».
 *
 * ⚠️ **Bottom sheet below `lg:`, centred dialog above**, and the primary action is a `shrink-0` sibling of a
 * scrolling body in both, so it stays on screen with the on-screen keyboard open and at a 380 px landscape height.
 */
export function CancelPeriodDialog({
  clinicId,
  clinicName,
  entry,
}: {
  clinicId: string;
  clinicName: string;
  entry: PlatformSubscriptionEntry;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cancelled, setCancelled] = useState<PlatformPeriodCancelled | null>(null);
  const [reason, setReason] = useState("");

  const amount = entry.amountDt === null ? "période offerte" : formatMoney(entry.amountDt);

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

    // Escape, the close control and the overlay tap all arrive here, so the confirmation covers every way out.
    // It only asks when a motif has actually been typed — a prompt on an untouched form is the one people learn to
    // dismiss without reading.
    if (
      reason.trim() !== "" &&
      cancelled === null &&
      !window.confirm("Abandonner cette annulation ? Le motif saisi sera perdu.")
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
      const response = await fetch("/bff/annulations", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, entryId: entry.entryId, reason: reason.trim() }),
      });

      // ⚠️ Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is
      // the only fact left when a body is unreadable — the rule `failed-read-as-empty` states.
      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformPeriodCancelled> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'annulation (${response.status}).`);
        // An entry somebody else has already struck through is a fact about the ledger, not a failed request: the
        // fiche is re-read so its motif and its author appear, while the refusal stays on screen to explain why
        // nothing happened here.
        if (body.code === PERIOD_ALREADY_CANCELLED_CODE) {
          router.refresh();
        }
        return;
      }

      setCancelled(body as PlatformPeriodCancelled);
      setReason("");
      // The fiche re-reads: the state, the end date and the entry's own « Annulé » line all move with this write.
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
        <Button type="button" variant="outline" size="sm" aria-label={`Annuler la période du ${formatDate(entry.recordedOn)} (${amount})`}>
          Annuler cette période
        </Button>
      </SheetTrigger>

      {/*
        `dvh`, never `vh`: a `vh`-sized panel does not shrink when the keyboard opens and the pinned footer goes off
        screen. The width override is `lg:`-prefixed because that is this application's own presentation boundary —
        the same one the portfolio's table/card split uses.
      */}
      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Annuler cette période</SheetTitle>
            {/* AC-5.4: the cabinet and the amount, so two open tabs cannot cancel the wrong one. */}
            <SheetDescription>
              {clinicName} · {entry.kindLabel} · {amount} · enregistrée le {formatDate(entry.recordedOn)}
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
                {/* A native textarea with the Input's own classes, as the payment sheet uses a native select: a
                    motif is a sentence, and `text-base` keeps it at 16 px so a phone does not zoom on focus. */}
                <textarea
                  id={`${fieldId}-reason`}
                  required
                  rows={3}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  disabled={submitting}
                  placeholder="Ex. : paiement enregistré sur le mauvais cabinet"
                  className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                />
                <p className="text-xs text-muted-foreground">
                  Obligatoire. Il reste inscrit sur la période et explique, plus tard, pourquoi la couverture du
                  cabinet a été raccourcie.
                </p>
              </div>
            )}
          </div>

          {/* Pinned: a `shrink-0` sibling of the scrolling body, so it stays on screen with the keyboard open. */}
          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {cancelled ? null : (
              <Button type="submit" variant="destructive" disabled={submitting}>
                {submitting ? "Annulation…" : "Annuler la période"}
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
 * AC-5.3 / EC-7 — what will happen, said before the vendor commits.
 *
 * ⚠️ **Every value is the server's fold**, and the three cases are genuinely different statements rather than one
 * sentence with a date in it: a cabinet that keeps working, one that goes back to read-only, and one already
 * suspended — where the cancellation changes nothing about the refusal it is meeting, and saying « repassera en
 * lecture seule » would credit this action with a consequence it did not cause.
 */
function Consequence({ preview }: { preview: PlatformCancellationPreview | null }) {
  if (!preview) {
    // No entitlement row at all (FR-13's failure state): there is no date to move, and the fiche says so above.
    return (
      <p className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="note">
        Ce cabinet n&apos;a aucun droit d&apos;usage enregistré : l&apos;annulation retirera cette période du
        journal, mais il n&apos;y a pas de date de fin à recalculer.
      </p>
    );
  }

  const alreadySuspended = preview.state === "Suspended";

  return (
    <div
      className={
        preview.makesReadOnly
          ? "rounded-md border border-destructive/40 p-3 text-sm"
          : "rounded-md border border-border bg-muted/40 p-3 text-sm"
      }
      role="note"
    >
      <p className="font-medium">
        {preview.endsOn === null
          ? "Après l'annulation, ce cabinet n'aura plus de date de fin."
          : `Après l'annulation, la couverture s'arrêtera le ${formatDate(preview.endsOn)}.`}
      </p>
      <p className="mt-1 text-muted-foreground">
        {alreadySuspended
          ? "Ce cabinet est suspendu : il est déjà en lecture seule, et cette annulation n'y change rien."
          : preview.makesReadOnly
            ? "Le cabinet repassera en lecture seule : il ne pourra plus enregistrer de nouveaux actes, mais il gardera l'accès à tous ses dossiers, ses exports et ses documents."
            : "Le cabinet pourra continuer à enregistrer de nouveaux actes."}
      </p>
      <p className="mt-1 text-muted-foreground">
        État après l&apos;annulation : {preview.stateLabel}.
      </p>
    </div>
  );
}

/**
 * What happened, read back from the server rather than from the preview the vendor confirmed — the ledger may have
 * moved between the page render and the click, and this is the answer that is true now.
 */
function Outcome({ cancelled }: { cancelled: PlatformPeriodCancelled }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">Période annulée.</p>
      <p className="mt-1 text-muted-foreground">
        État : {cancelled.stateLabel}
        {cancelled.endsOn ? ` · jusqu'au ${formatDate(cancelled.endsOn)}` : " · sans échéance"}
        {cancelled.previousEndsOn ? ` (auparavant ${formatDate(cancelled.previousEndsOn)})` : ""}
      </p>
      {cancelled.makesReadOnly ? (
        <p className="mt-1 text-muted-foreground">
          Ce cabinet est désormais en lecture seule. Ses dossiers, ses exports et ses documents restent accessibles.
        </p>
      ) : null}
      <p className="mt-1 text-muted-foreground">
        La période reste dans le journal, barrée, avec son motif — rien n&apos;a été supprimé.
      </p>
    </div>
  );
}
