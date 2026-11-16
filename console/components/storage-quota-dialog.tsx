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
import type { PlatformStorageQuotaChanged } from "@/lib/api/platform";
import { formatBytes } from "@/lib/format";

const GIGABYTE = 1024 * 1024 * 1024;

/**
 * « Modifier le quota » (`features/camera-photo-import`, phase 3) — the vendor raising a cabinet's storage ceiling, or
 * returning it to the deployment's default. A motif is mandatory and journaled; the figures in force are stated before
 * the press, so lowering a ceiling under what the cabinet already stores is a decision taken with the number in view.
 *
 * ⚠️ `suspend-dialog.tsx`'s panel, deliberately: bottom sheet below `lg:`, centred above, the action pinned outside the
 * scrolling body so it stays on screen with the keyboard open.
 */
export function StorageQuotaDialog({
  clinicId,
  clinicName,
  usedBytes,
  quotaBytes,
  isCustom,
}: {
  clinicId: string;
  clinicName: string;
  usedBytes: number;
  quotaBytes: number;
  isCustom: boolean;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [changed, setChanged] = useState<PlatformStorageQuotaChanged | null>(null);
  const [gigabytes, setGigabytes] = useState("");
  const [useDefault, setUseDefault] = useState(false);
  const [reason, setReason] = useState("");

  const typed = Number(gigabytes.replace(",", "."));
  const typedValid = useDefault || (Number.isFinite(typed) && typed >= 1 && typed <= 2048);
  const belowUsage = !useDefault && typedValid && typed * GIGABYTE < usedBytes;

  function openChanged(next: boolean) {
    if (next) {
      setError(null);
      setChanged(null);
      setGigabytes(String(Math.round(quotaBytes / GIGABYTE)));
      setUseDefault(false);
      setReason("");
      setOpen(true);
      return;
    }

    if (submitting) return;
    // Any edit counts — a new figure or « par défaut » ticked is lost on Escape as surely as a typed motif.
    const edited =
      reason.trim() !== "" || useDefault || gigabytes !== String(Math.round(quotaBytes / GIGABYTE));
    if (edited && changed === null && !window.confirm("Abandonner ce changement ? Ce qui a été saisi sera perdu.")) {
      return;
    }
    setOpen(false);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    if (!typedValid) return;
    setSubmitting(true);
    setError(null);

    try {
      const response = await fetch("/bff/stockage", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clinicId,
          quotaBytes: useDefault ? null : Math.round(typed * GIGABYTE),
          reason: reason.trim(),
        }),
      });

      // Read the text and parse under a guard: an unreadable body still has a status worth naming.
      const raw = await response.text();
      let body: { error?: string } & Partial<PlatformStorageQuotaChanged> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'opération (${response.status}).`);
        return;
      }

      setChanged(body as PlatformStorageQuotaChanged);
      setReason("");
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
        <Button type="button" variant="outline" size="sm" aria-label={`Modifier le quota de stockage de ${clinicName}`}>
          Modifier le quota
        </Button>
      </SheetTrigger>

      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Quota de stockage</SheetTitle>
            <SheetDescription>
              {clinicName} · {formatBytes(usedBytes)} utilisés sur {formatBytes(quotaBytes)}
              {isCustom ? " (quota du cabinet)" : " (quota par défaut)"}
            </SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {changed ? (
              <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
                <p className="font-medium">Quota enregistré.</p>
                <p className="mt-1 text-muted-foreground">
                  {formatBytes(changed.storageUsedBytes)} utilisés sur {formatBytes(changed.storageQuotaBytes)}
                  {changed.storageQuotaIsCustom ? " (quota du cabinet)." : " (quota par défaut)."}
                </p>
              </div>
            ) : (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-gb`}>Nouveau quota (Go)</Label>
                  <input
                    id={`${fieldId}-gb`}
                    inputMode="decimal"
                    value={gigabytes}
                    onChange={(e) => setGigabytes(e.target.value)}
                    disabled={submitting || useDefault}
                    className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base tabular-nums disabled:opacity-50"
                  />
                  <label className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={useDefault}
                      onChange={(e) => setUseDefault(e.target.checked)}
                      disabled={submitting}
                    />
                    Quota par défaut
                  </label>
                  {!typedValid ? (
                    <p className="text-xs text-destructive" role="alert">
                      Entre 1 Go et 2 048 Go.
                    </p>
                  ) : belowUsage ? (
                    <p className="text-xs text-amber-700 dark:text-amber-400" role="status">
                      Plus bas que ce que le cabinet stocke déjà : rien n&apos;est supprimé, mais ses prochains envois
                      seront refusés.
                    </p>
                  ) : null}
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-reason`}>Motif</Label>
                  <textarea
                    id={`${fieldId}-reason`}
                    required
                    rows={3}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    disabled={submitting}
                    placeholder="Ex. : photos de chaque patient — demande du Dr Ben Salah"
                    className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                  />
                </div>
              </>
            )}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}
          </div>

          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {changed ? null : (
              <Button type="submit" disabled={submitting || !typedValid || reason.trim() === ""}>
                {submitting ? "Enregistrement…" : "Enregistrer le quota"}
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
