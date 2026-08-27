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
  CLINIC_ALREADY_SUSPENDED_CODE,
  CLINIC_NOT_SUSPENDED_CODE,
  type PlatformSuspensionChanged,
} from "@/lib/api/platform";
import { formatDate } from "@/lib/format";

/**
 * « Suspendre ce cabinet » / « Lever la suspension » (`platform-console` US-6) — the console's third write.
 *
 * ⚠️ **One component for both directions, and the direction is a prop rather than a toggle.** `suspended` comes from
 * the server's own `state === "Suspended"`, so the control the vendor sees is the one the cabinet's actual state
 * admits; a toggle would act on whatever the page last managed to read. The two are one component because the panel,
 * the confirm-before-discard and the outcome are identical — only the question changes.
 *
 * ⚠️ **Suspension is never presented as a payment matter** (AC-6.3). The words are « suspendre » and « lever », never
 * « bloquer pour non-paiement », the panel says out loud that no paid day is consumed, and the state after the write
 * is stated in **text** — the border colour beside it carries nothing a reader could not get from the sentence.
 *
 * ⚠️ **The motif is mandatory when suspending** (AC-6.1), `required` here as a courtesy and refused in French by the
 * server, which is the guard. It is the only answer to « suspendu pourquoi ? » the cabinet's own screen can give.
 *
 * ⚠️ **AC-6.5 is the panel's first sentence**: the cabinet is named and the consequence — no new work can be
 * recorded — is stated before the vendor can commit, along with what keeps working, because a practice locked out of
 * its own records and one locked out of recording new ones are very different events.
 *
 * ⚠️ **Bottom sheet below `lg:`, centred dialog above**, with the destructive action a `shrink-0` sibling of a
 * scrolling body, so it stays on screen with the on-screen keyboard open and at a 380 px landscape height.
 */
export function SuspendDialog({
  clinicId,
  clinicName,
  suspended,
  endsOn,
}: {
  clinicId: string;
  clinicName: string;
  suspended: boolean;
  endsOn: string | null;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [changed, setChanged] = useState<PlatformSuspensionChanged | null>(null);
  const [reason, setReason] = useState("");

  function openChanged(next: boolean) {
    if (next) {
      setError(null);
      setChanged(null);
      setReason("");
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape, the close control and the overlay tap all arrive here, so the confirmation covers every way out. It
    // only asks when a motif has actually been typed — a prompt on an untouched form is the one people learn to
    // dismiss without reading.
    if (
      reason.trim() !== "" &&
      changed === null &&
      !window.confirm("Abandonner cette suspension ? Le motif saisi sera perdu.")
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
      const response = await fetch("/bff/suspensions", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, suspend: !suspended, reason: reason.trim() }),
      });

      // ⚠️ Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is
      // the only fact left when a body is unreadable — the rule `failed-read-as-empty` states.
      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformSuspensionChanged> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'opération (${response.status}).`);
        // Both refusals mean the fiche in front of the vendor is out of date — somebody else has suspended or
        // released this cabinet since it was drawn. Re-reading is what puts the right control on screen; the refusal
        // stays up to explain why nothing happened here.
        if (body.code === CLINIC_ALREADY_SUSPENDED_CODE || body.code === CLINIC_NOT_SUSPENDED_CODE) {
          router.refresh();
        }
        return;
      }

      setChanged(body as PlatformSuspensionChanged);
      setReason("");
      // The fiche re-reads: the state badge, the suspension section and the control itself all move with this write.
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
          variant={suspended ? "outline" : "destructive"}
          size="sm"
          aria-label={
            suspended ? `Lever la suspension de ${clinicName}` : `Suspendre le cabinet ${clinicName}`
          }
        >
          {suspended ? "Lever la suspension" : "Suspendre ce cabinet"}
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
            <SheetTitle>{suspended ? "Lever la suspension" : "Suspendre ce cabinet"}</SheetTitle>
            {/* AC-6.5: the cabinet is named, so several open tabs cannot suspend the wrong practice. */}
            <SheetDescription>
              {clinicName} ·{" "}
              {endsOn ? `couverture jusqu'au ${formatDate(endsOn)}` : "sans échéance de couverture"}
            </SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {changed ? (
              <Outcome changed={changed} />
            ) : suspended ? (
              <LiftingConsequence clinicName={clinicName} endsOn={endsOn} />
            ) : (
              <SuspensionConsequence clinicName={clinicName} />
            )}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            {changed || suspended ? null : (
              <div className="space-y-1.5">
                <Label htmlFor={`${fieldId}-reason`}>Motif de la suspension</Label>
                {/* A native textarea with the Input's own classes, as the payment sheet uses a native select: a
                    motif is a sentence, and `text-base` keeps it at 16 px so a phone does not zoom on focus. */}
                <textarea
                  id={`${fieldId}-reason`}
                  required
                  rows={3}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  disabled={submitting}
                  placeholder="Ex. : facturation frauduleuse signalée par un patient"
                  className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                />
                <p className="text-xs text-muted-foreground">
                  Obligatoire. Il reste inscrit sur le cabinet et c&apos;est la seule réponse à « suspendu
                  pourquoi ? » que le cabinet et vos collègues pourront lire ensuite.
                </p>
              </div>
            )}
          </div>

          {/* Pinned: a `shrink-0` sibling of the scrolling body, so it stays on screen with the keyboard open. */}
          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {changed ? null : (
              <Button type="submit" variant={suspended ? "default" : "destructive"} disabled={submitting}>
                {submitting
                  ? suspended
                    ? "Levée…"
                    : "Suspension…"
                  : suspended
                    ? "Lever la suspension"
                    : "Suspendre le cabinet"}
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {changed ? "Fermer" : "Revenir"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * AC-6.5 — what suspending does, said before the vendor commits.
 *
 * ⚠️ **What keeps working is stated first**, as every refusal sentence in this product does: this is read by somebody
 * deciding whether a practice deserves it, and « le cabinet perd tout » would be both false and the wrong basis for
 * that decision.
 *
 * ⚠️ **« Aucun jour payé n'est consommé » is here rather than only in the API's remarks** (AC-6.4): it is the fact
 * that makes suspension a reversible measure, and a vendor who does not know it will reach for a cancellation
 * instead — which is not reversible at all.
 */
function SuspensionConsequence({ clinicName }: { clinicName: string }) {
  return (
    <div className="rounded-md border border-destructive/40 p-3 text-sm" role="note">
      <p className="font-medium">
        {clinicName} ne pourra plus enregistrer de nouveaux actes.
      </p>
      <p className="mt-1 text-muted-foreground">
        Le cabinet gardera l&apos;accès à tous ses dossiers, ses exports et ses documents : seules les écritures sont
        refusées. Il lira « Suspendu » et jamais « Expiré », donc personne ne lui laissera croire qu&apos;un paiement
        y changerait quelque chose.
      </p>
      <p className="mt-1 text-muted-foreground">
        Aucun jour payé n&apos;est consommé pendant la suspension, et la levée rend au cabinet exactement la
        couverture qu&apos;il avait.
      </p>
    </div>
  );
}

/**
 * The mirror, and the sentence that stops a lift being read as a fix.
 *
 * ⚠️ **A lift restores nothing and grants nothing.** On a cabinet whose cover ran out while it was suspended, the
 * practice is still read-only afterwards — for a different reason, which the outcome then names. Promising « il
 * pourra de nouveau travailler » here would be a claim this write cannot keep.
 */
function LiftingConsequence({ clinicName, endsOn }: { clinicName: string; endsOn: string | null }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="note">
      <p className="font-medium">La suspension de {clinicName} sera levée.</p>
      <p className="mt-1 text-muted-foreground">
        Le cabinet retrouvera exactement la couverture qu&apos;il avait :{" "}
        {endsOn ? `jusqu'au ${formatDate(endsOn)}` : "sans échéance"}. Rien n&apos;est accordé et rien n&apos;a été
        consommé — si cette date est déjà passée, le cabinet restera en lecture seule, cette fois pour échéance.
      </p>
      <p className="mt-1 text-muted-foreground">
        Le motif, son auteur et sa date disparaissent de la fiche. Les deux opérations restent inscrites au journal
        des accès de la console.
      </p>
    </div>
  );
}

/**
 * What actually happened, read back from the server rather than assumed from the button that was pressed — the one
 * case that matters is a lift landing on a cabinet that is still expired, which only the server's own state rule
 * knows.
 */
function Outcome({ changed }: { changed: PlatformSuspensionChanged }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">{changed.isSuspended ? "Cabinet suspendu." : "Suspension levée."}</p>
      <p className="mt-1 text-muted-foreground">
        État : {changed.stateLabel}
        {changed.endsOn ? ` · couverture jusqu'au ${formatDate(changed.endsOn)}` : " · sans échéance"}
      </p>
      <p className="mt-1 text-muted-foreground">
        {changed.makesReadOnly
          ? changed.isSuspended
            ? "Le cabinet est en lecture seule : ses dossiers, ses exports et ses documents restent accessibles."
            : "Le cabinet reste en lecture seule, non plus pour suspension mais parce que sa couverture est arrivée à échéance : cela se corrige par un paiement."
          : "Le cabinet peut enregistrer de nouveaux actes."}
      </p>
    </div>
  );
}
