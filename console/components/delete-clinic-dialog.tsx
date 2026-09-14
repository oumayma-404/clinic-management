"use client";

import { useRouter } from "next/navigation";
import { useEffect, useId, useState } from "react";

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
import {
  CLINIC_NOT_FOUND_CODE,
  type PlatformClinicDeleted,
  type PlatformClinicDeletionPreview,
} from "@/lib/api/platform";
import { formatCount } from "@/lib/format";

/**
 * « Supprimer définitivement ce cabinet » (`clinic-account-removal`) — the console's only irreversible control.
 *
 * ⚠️ **The panel states what will be destroyed before it accepts anything**, and the figures come from the
 * deletion's own plan rather than from a second set of counting queries: a destructive click made on an
 * understatement is the failure this exists to prevent. While that read is in flight the field and the button are
 * **absent**, not merely disabled — a form that can be filled in before anybody has been told what it does is a
 * form somebody fills in.
 *
 * ⚠️ **A typed name, not a « je comprends » tick.** The failure this is guarding against is a *wrong row* — several
 * cabinets are named « Cabinet Test N » — and a checkbox cannot catch that; typing the name means the vendor has
 * read the cabinet they are about to destroy. It is checked **again server-side** against the cabinet the id
 * resolved to, so this half is a courtesy: the browser comparing two of its own strings would agree with itself.
 *
 * ⚠️ **The panel does not close on success.** It shows what was removed and which addresses are free, because that
 * list is the whole reason the action was taken and there is nowhere left to read it afterwards — the cabinet is
 * gone and the fiche behind this panel is about to 404.
 *
 * ⚠️ Bottom sheet below `lg:`, centred dialog above, with the destructive action a `shrink-0` sibling of a
 * scrolling body so it stays on screen with the keyboard open — `suspend-dialog.tsx`'s shape.
 */
export function DeleteClinicDialog({ clinicId, clinicName }: { clinicId: string; clinicName: string }) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [preview, setPreview] = useState<PlatformClinicDeletionPreview | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [typedName, setTypedName] = useState("");
  const [reason, setReason] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [deleted, setDeleted] = useState<PlatformClinicDeleted | null>(null);

  useEffect(() => {
    if (!open || preview !== null || deleted !== null) {
      return;
    }

    let live = true;

    void (async () => {
      try {
        const response = await fetch(`/bff/suppression?clinicId=${encodeURIComponent(clinicId)}`);
        const raw = await response.text();
        let body: { error?: string } & Partial<PlatformClinicDeletionPreview> = {};
        if (raw.length > 0) {
          try {
            body = JSON.parse(raw) as typeof body;
          } catch {
            body = {};
          }
        }

        if (!live) {
          return;
        }

        if (!response.ok) {
          // A failed read must never render as « ce cabinet ne contient rien » — that is the one sentence that
          // would make this button safe-looking and wrong.
          setPreviewError(body.error ?? `Lecture impossible (${response.status}).`);
          return;
        }

        setPreview(body as PlatformClinicDeletionPreview);
      } catch {
        if (live) {
          setPreviewError("Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.");
        }
      }
    })();

    return () => {
      live = false;
    };
  }, [open, preview, deleted, clinicId]);

  function openChanged(next: boolean) {
    if (next) {
      setError(null);
      setPreviewError(null);
      setPreview(null);
      setDeleted(null);
      setTypedName("");
      setReason("");
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape, the close control and the overlay tap all arrive here. It only asks once something has been typed —
    // a prompt on an untouched form is the one people learn to dismiss without reading.
    if (
      deleted === null &&
      (typedName.trim() !== "" || reason.trim() !== "") &&
      !window.confirm("Abandonner cette suppression ? Rien ne sera supprimé.")
    ) {
      return;
    }

    setOpen(false);

    // Only after a deletion: the fiche behind this panel describes a cabinet that no longer exists, and re-reading
    // is what replaces it with « ce cabinet n'existe plus » instead of a page of stale figures.
    if (deleted !== null) {
      router.refresh();
    }
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      const response = await fetch("/bff/suppression", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, confirmationName: typedName, reason: reason.trim() }),
      });

      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformClinicDeleted> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'opération (${response.status}).`);

        // The cabinet is already gone — somebody else deleted it, or this tab is old. Re-reading the fiche is what
        // says so; the refusal stays up to explain why nothing happened here.
        if (body.code === CLINIC_NOT_FOUND_CODE) {
          router.refresh();
        }
        return;
      }

      setDeleted(body as PlatformClinicDeleted);
    } catch {
      setError("Impossible de joindre le serveur. Vérifiez que le tunnel est ouvert, puis réessayez.");
    } finally {
      setSubmitting(false);
    }
  }

  const ready = preview !== null && deleted === null;

  return (
    <Sheet open={open} onOpenChange={openChanged}>
      <SheetTrigger asChild>
        <Button
          type="button"
          variant="destructive"
          size="sm"
          aria-label={`Supprimer définitivement le cabinet ${clinicName}`}
        >
          Supprimer définitivement
        </Button>
      </SheetTrigger>

      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>
              {deleted ? "Cabinet supprimé" : "Supprimer définitivement ce cabinet"}
            </SheetTitle>
            {/* Named here as well as in the sentence below: several tabs, several cabinets called « Test ». */}
            <SheetDescription>{clinicName}</SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {deleted ? (
              <Outcome deleted={deleted} />
            ) : previewError ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {previewError} Rien n&apos;a été supprimé. Fermez ce panneau et réessayez : tant que ce que contient
                le cabinet n&apos;a pas pu être lu, la suppression n&apos;est pas proposée.
              </p>
            ) : preview === null ? (
              <p className="text-sm text-muted-foreground" role="status">
                Lecture de ce que contient ce cabinet…
              </p>
            ) : (
              <Consequence preview={preview} />
            )}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            {ready ? (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-name`}>Nom du cabinet</Label>
                  <Input
                    id={`${fieldId}-name`}
                    required
                    autoComplete="off"
                    value={typedName}
                    onChange={(e) => setTypedName(e.target.value)}
                    disabled={submitting}
                    placeholder={clinicName}
                  />
                  <p className="text-xs text-muted-foreground">
                    Recopiez le nom affiché ci-dessus pour confirmer. Il est vérifié par le serveur contre le
                    cabinet que cette fiche a ouvert.
                  </p>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-reason`}>Motif de la suppression</Label>
                  <textarea
                    id={`${fieldId}-reason`}
                    required
                    rows={3}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    disabled={submitting}
                    placeholder="Ex. : cabinet de test créé pour essayer le produit — adresse à libérer"
                    className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                  />
                  <p className="text-xs text-muted-foreground">
                    Obligatoire. C&apos;est la seule chose qui restera de ce cabinet, au journal des accès, et la
                    seule réponse à « pourquoi ces données ont-elles disparu ? » que vos collègues pourront lire.
                  </p>
                </div>
              </>
            ) : null}
          </div>

          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {ready ? (
              <Button type="submit" variant="destructive" disabled={submitting}>
                {submitting ? "Suppression…" : "Supprimer définitivement"}
              </Button>
            ) : null}
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {deleted ? "Fermer" : "Revenir"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * What the deletion will destroy, in the order somebody reads it: the named records, then the total, then the
 * files, then what is *not* destroyed, then the addresses that come back.
 *
 * ⚠️ **The total is stated even when every named figure is zero**, because an empty list of tallies means « none of
 * the nine things we name » and never « nothing at all » — a cabinet always holds its own settings, its catalogues
 * and its journal.
 */
function Consequence({ preview }: { preview: PlatformClinicDeletionPreview }) {
  return (
    <div className="space-y-3 text-sm">
      <p className="text-destructive" role="note">
        Cette action est <strong>définitive</strong> et il n&apos;y a pas de corbeille : rien de ce qui suit ne peut
        être récupéré ensuite, sauf depuis une archive du cabinet téléchargée avant.
      </p>

      {preview.tallies.length > 0 ? (
        <ul className="space-y-1">
          {preview.tallies.map((tally) => (
            <li key={tally.label} className="flex justify-between gap-3 border-b border-border/60 pb-1">
              <span className="text-muted-foreground">{tally.label}</span>
              <span className="font-medium tabular-nums">{formatCount(tally.rows)}</span>
            </li>
          ))}
        </ul>
      ) : null}

      <p className="text-muted-foreground">
        {formatCount(preview.rowsTotal)} lignes en tout, dont les paramètres du cabinet, ses catalogues et son
        journal d&apos;activité · {formatCount(preview.fileCount)} fichier(s) ({formatSize(preview.fileBytes)}).
      </p>

      {/* Stated because a reader who has just been told the journal goes would reasonably assume this does too —
          and it is the one thing that will still say the cabinet existed. */}
      <p className="text-muted-foreground">
        Ce qui reste : la trace de cette suppression au journal des accès de la console, avec votre compte, la date
        et le motif que vous saisissez ci-dessous.
      </p>

      {preview.freedEmails.length > 0 ? (
        <div>
          <p className="text-muted-foreground">
            Adresse(s) libérée(s) — réutilisables pour une nouvelle inscription :
          </p>
          <ul className="mt-1 space-y-0.5">
            {preview.freedEmails.map((email) => (
              <li key={email} className="font-mono text-xs break-all">
                {email}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <p className="text-muted-foreground">
          Aucune adresse ne sera libérée : ce cabinet n&apos;a aucun compte avec mot de passe.
        </p>
      )}
    </div>
  );
}

/** What was removed. Shown in place of closing, because the freed addresses have nowhere else left to be read. */
function Outcome({ deleted }: { deleted: PlatformClinicDeleted }) {
  return (
    <div className="space-y-3 text-sm" role="status">
      <p>
        <strong>{deleted.clinicName}</strong> a été supprimé : {formatCount(deleted.rowsDeleted)} lignes et{" "}
        {formatCount(deleted.filesDeleted)} fichier(s). La suppression est inscrite au journal des accès.
      </p>

      {deleted.freedEmails.length > 0 ? (
        <div>
          <p className="text-muted-foreground">
            Ces adresses sont de nouveau disponibles pour une inscription :
          </p>
          <ul className="mt-1 space-y-0.5">
            {deleted.freedEmails.map((email) => (
              <li key={email} className="font-mono text-xs break-all">
                {email}
              </li>
            ))}
          </ul>
        </div>
      ) : null}

      <p className="text-muted-foreground">
        Cette fiche ne correspond plus à aucun cabinet. Fermez ce panneau pour revenir au portefeuille.
      </p>
    </div>
  );
}

/**
 * « 12,4 Mo ». Here rather than in `lib/format.ts` because the console has no other size to render, and the unit
 * spelling (« o / Ko / Mo / Go ») is the one `web/lib/format.ts` uses, not the English one.
 */
function formatSize(bytes: number): string {
  if (bytes <= 0) {
    return "0 o";
  }

  const units = ["o", "Ko", "Mo", "Go"];
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
  const value = bytes / 1024 ** exponent;

  return `${value.toLocaleString("fr-TN", { maximumFractionDigits: exponent === 0 ? 0 : 1 })} ${units[exponent]}`;
}
