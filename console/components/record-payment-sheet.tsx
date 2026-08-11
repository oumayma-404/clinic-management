"use client";

import { useRouter } from "next/navigation";
import { useId, useState } from "react";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import type { PlatformPaymentRecorded } from "@/lib/api/platform";
import { formatDate } from "@/lib/format";

/**
 * « Enregistrer un paiement » (`platform-console` US-4) — the console's only write.
 *
 * ⚠️ **Full screen below `lg:`, a dialog above it, and the primary action is pinned in both.** The form has eight
 * fields, so on a phone a `max-h-[85dvh]` panel would put « Enregistrer » under the fold the moment the on-screen
 * keyboard opens — which is the AC-25 defect `sheet-vh` exists to catch. The body scrolls inside `flex-1` and the
 * footer is a `shrink-0` sibling, so the action stays on screen at every height including a 380 px landscape one.
 *
 * ⚠️ **Dismissible by a visible control AND `Escape`, and it confirms before discarding typed input.** Radix gives
 * the second and the focus trap; the confirmation is here, because the whole cost of losing this form is retyping
 * a payment reference off a bank statement. It only asks when something has actually been typed — a confirmation
 * on an untouched form is the dialog people learn to dismiss without reading.
 *
 * ⚠️ **One idempotency key per opened sheet** (AC-4.6). It is minted when the sheet opens and reused for every
 * submission from it, so the second tap of a double-click carries the first tap's key and the server answers with
 * the first outcome. Re-minting per submit would defeat the whole mechanism; minting once per mount would make a
 * *deliberate* second payment impossible.
 *
 * ⚠️ **The refusal shown is the server's own sentence.** Nothing here rewords a refusal or infers one from a
 * status — the server is the only participant that knows why a payment was refused.
 */
export function RecordPaymentSheet({
  clinicId,
  clinicName,
  endsOn,
}: {
  clinicId: string;
  clinicName: string;
  endsOn: string | null;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [recorded, setRecorded] = useState<PlatformPaymentRecorded | null>(null);
  const [idempotencyKey, setIdempotencyKey] = useState("");
  const [dirty, setDirty] = useState(false);

  const [duration, setDuration] = useState("12");
  const [complimentary, setComplimentary] = useState(false);
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("Transfer");
  const [reference, setReference] = useState("");
  const [note, setNote] = useState("");

  function openChanged(next: boolean) {
    if (next) {
      // A fresh key per opening: two payments genuinely recorded one after the other must both land (EC-6), and
      // one key for the lifetime of the page would silently replay the first.
      setIdempotencyKey(crypto.randomUUID());
      setError(null);
      setRecorded(null);
      setDirty(false);
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape and the close control both arrive here, so the confirmation covers every way out — including the
    // overlay tap, which is the easiest one to hit by accident on a phone.
    if (dirty && recorded === null && !window.confirm("Abandonner ce paiement ? Les valeurs saisies seront perdues.")) {
      return;
    }

    setOpen(false);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const response = await fetch("/bff/paiements", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clinicId,
          idempotencyKey,
          complimentary,
          durationMonths: Number(duration),
          amountDt: complimentary || amount.trim() === "" ? null : Number(amount.replace(",", ".")),
          method: complimentary ? null : method,
          reference: reference.trim() === "" ? null : reference.trim(),
          note: note.trim() === "" ? null : note.trim(),
        }),
      });

      // ⚠️ Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which
      // is the only fact left when a body is unreadable — the rule `failed-read-as-empty` states.
      const raw = await response.text();
      let body: { error?: string } & Partial<PlatformPaymentRecorded> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'enregistrement (${response.status}).`);
        return;
      }

      setRecorded(body as PlatformPaymentRecorded);
      setDirty(false);
      // The fiche re-reads: the state, the end date and the payment history all move with this write (AC-4.3).
      router.refresh();
    } catch {
      setError("Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.");
    } finally {
      setSubmitting(false);
    }
  }

  function edited<T>(setter: (value: T) => void) {
    return (value: T) => {
      setDirty(true);
      setter(value);
    };
  }

  return (
    <Sheet open={open} onOpenChange={openChanged}>
      <SheetTrigger asChild>
        <Button type="button">Enregistrer un paiement</Button>
      </SheetTrigger>

      {/*
        Full height below `lg:` and a centred panel above it. `dvh`, never `vh`: a `vh`-sized panel does not shrink
        when the keyboard opens and the pinned footer goes off screen. The width override is `lg:`-prefixed because
        that is this application's own presentation boundary — the same one the portfolio's table/card split uses.
      */}
      <SheetContent
        side="bottom"
        className="inset-0 h-dvh max-h-dvh rounded-none p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:h-auto lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex h-full min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Enregistrer un paiement</SheetTitle>
            <SheetDescription>
              {clinicName}
              {endsOn ? ` · se termine le ${formatDate(endsOn)}` : " · sans échéance"}
            </SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {recorded ? <Outcome recorded={recorded} /> : null}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            <div className="space-y-1.5">
              <Label htmlFor={`${fieldId}-duration`}>Durée (mois)</Label>
              <Input
                id={`${fieldId}-duration`}
                type="number"
                inputMode="numeric"
                min={1}
                required
                value={duration}
                onChange={(e) => edited(setDuration)(e.target.value)}
                disabled={submitting}
              />
            </div>

            <div className="flex items-start gap-3">
              <input
                id={`${fieldId}-complimentary`}
                type="checkbox"
                className="mt-1 size-4"
                checked={complimentary}
                onChange={(e) => edited(setComplimentary)(e.target.checked)}
                disabled={submitting}
              />
              <div>
                <Label htmlFor={`${fieldId}-complimentary`}>Période offerte</Label>
                <p className="text-xs text-muted-foreground">
                  Enregistrée comme « offert », sans montant — ce n&apos;est pas la même chose qu&apos;un paiement
                  de 0,000 DT.
                </p>
              </div>
            </div>

            {!complimentary ? (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-amount`}>Montant (DT)</Label>
                  <Input
                    id={`${fieldId}-amount`}
                    type="text"
                    inputMode="decimal"
                    placeholder="1200,000"
                    value={amount}
                    onChange={(e) => edited(setAmount)(e.target.value)}
                    disabled={submitting}
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-method`}>Moyen de paiement</Label>
                  {/* A native select: it gets the platform's own picker on a phone, which no custom listbox
                      matches for a four-option choice, and it is keyboard-reachable for free. */}
                  <select
                    id={`${fieldId}-method`}
                    className="min-h-11 w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                    value={method}
                    onChange={(e) => edited(setMethod)(e.target.value)}
                    disabled={submitting}
                  >
                    <option value="Transfer">Virement</option>
                    <option value="Cash">Espèces</option>
                    <option value="Cheque">Chèque</option>
                    <option value="Card">Carte bancaire</option>
                  </select>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-reference`}>Référence</Label>
                  <Input
                    id={`${fieldId}-reference`}
                    value={reference}
                    onChange={(e) => edited(setReference)(e.target.value)}
                    placeholder="N° de virement, de chèque ou de reçu"
                    disabled={submitting}
                  />
                </div>
              </>
            ) : null}

            <div className="space-y-1.5">
              <Label htmlFor={`${fieldId}-note`}>Note (facultatif)</Label>
              <Input
                id={`${fieldId}-note`}
                value={note}
                onChange={(e) => edited(setNote)(e.target.value)}
                disabled={submitting}
              />
            </div>
          </div>

          {/* Pinned: a `shrink-0` sibling of the scrolling body, so it stays on screen with the keyboard open. */}
          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            <Button type="submit" disabled={submitting}>
              {submitting ? "Enregistrement…" : "Enregistrer le paiement"}
            </Button>
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {recorded ? "Fermer" : "Annuler"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * AC-4.3 — the new state and end date, immediately, and AC-4.6's « déjà enregistré » said in words.
 *
 * ⚠️ A replay is a **success**, not a warning: the second tap found the money already taken, which is the outcome
 * the vendor wanted. Telling them it failed would invite a third attempt.
 */
function Outcome({ recorded }: { recorded: PlatformPaymentRecorded }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">
        {recorded.alreadyRecorded ? "Ce paiement était déjà enregistré." : "Paiement enregistré."}
      </p>
      <p className="mt-1 text-muted-foreground">
        État : {recorded.stateLabel}
        {recorded.endsOn ? ` · jusqu'au ${formatDate(recorded.endsOn)}` : " · sans échéance"}
        {recorded.previousEndsOn ? ` (auparavant ${formatDate(recorded.previousEndsOn)})` : ""}
      </p>
    </div>
  );
}
