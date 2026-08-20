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
import {
  CLINIC_ACCOUNT_NOT_FOUND_CODE,
  SECOND_FACTOR_NOT_ENROLLED_CODE,
  type PlatformSecondFactorReset,
} from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";

/**
 * « Réinitialiser un second facteur » (`hosted-security-hardening` FR-1.4) — the console's fifth write, and the only
 * one that acts on a person rather than on a cabinet.
 *
 * ⚠️ **Why this exists.** Clearing a second factor may never rest on the password alone, so somebody has to vouch
 * for whoever lost their authenticator. A recovery code they still hold does it with no vendor involved, and their
 * own administrator does it otherwise — but a cabinet with a **single administrator** who kept no codes has neither.
 * Nothing they possess proves who they are, so a human must, and the only humans left are here. This replaces an
 * SSH session and `dotnet run -- reset-user-totp`, which answered support calls off the console's own record.
 *
 * ⚠️ **The address is typed, not picked from a list, and that is deliberate.** The console has no roster of a
 * cabinet's staff and this feature adds none: the vendor already has the person on the telephone. It keeps « nous ne
 * pouvons pas voir vos dossiers » exactly as narrow as it was, and a mis-keyed address can only reach an account at
 * the cabinet already open — the API scopes the reset to the clinic in its URL. The administrator's address is
 * pre-filled because they are who calls, and it is editable because a doctor or a secretary can lose a phone too.
 *
 * ⚠️ **The outcome names the person, not « c'est fait ».** A mistyped character that happens to match a colleague is
 * the failure mode here, and reading the name and role back is the only chance to catch it while ringing back still
 * fixes it.
 *
 * ⚠️ **The motif is mandatory**, `required` here as a courtesy and refused in French by the server, which is the
 * guard. Unlike a suspension's it has no domain row to live on — the reset leaves no trace — so the journal row is
 * the entire record of the operation, and it is what stands between this panel and a social-engineered telephone
 * call.
 *
 * ⚠️ **Bottom sheet below `lg:`, centred dialog above**, with the destructive action a `shrink-0` sibling of a
 * scrolling body, so it stays on screen with the on-screen keyboard open and at a 380 px landscape height — the
 * shape `suspend-dialog.tsx` sets.
 */
export function ResetSecondFactorDialog({
  clinicId,
  clinicName,
  adminEmail,
}: {
  clinicId: string;
  clinicName: string;
  adminEmail: string | null;
}) {
  const router = useRouter();
  const fieldId = useId();

  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<PlatformSecondFactorReset | null>(null);
  const [email, setEmail] = useState(adminEmail ?? "");
  const [reason, setReason] = useState("");

  function openChanged(next: boolean) {
    if (next) {
      setError(null);
      setDone(null);
      setEmail(adminEmail ?? "");
      setReason("");
      setOpen(true);
      return;
    }

    if (submitting) {
      return;
    }

    // Escape, the close control and the overlay tap all arrive here. It asks only when a motif has been typed — a
    // prompt on an untouched form is the one people learn to dismiss without reading.
    if (
      reason.trim() !== "" &&
      done === null &&
      !window.confirm("Abandonner cette réinitialisation ? Le motif saisi sera perdu.")
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
      const response = await fetch("/bff/second-facteur", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, email: email.trim(), reason: reason.trim() }),
      });

      // Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is the
      // only fact left when a body is unreadable.
      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformSecondFactorReset> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'opération (${response.status}).`);
        // ⚠️ Both refusals leave the panel open with the address intact: they are answered by asking the person on
        // the telephone another question, not by re-reading this page. Nothing was written, so there is nothing to
        // refresh — unlike the suspension dialog, whose refusals mean the fiche is stale.
        return;
      }

      setDone(body as PlatformSecondFactorReset);
      setReason("");
      // The journal section at the foot of the fiche gains a row for this.
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
          aria-label={`Réinitialiser le second facteur d'un compte de ${clinicName}`}
        >
          Réinitialiser un second facteur
        </Button>
      </SheetTrigger>

      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Réinitialiser un second facteur</SheetTitle>
            {/* The cabinet is named, so several open tabs cannot disarm an account at the wrong practice. */}
            <SheetDescription>{clinicName}</SheetDescription>
          </SheetHeader>

          <div className="min-h-0 flex-1 space-y-4 overflow-y-auto p-4">
            {done ? <Outcome done={done} /> : <Consequence />}

            {error ? (
              <p className="rounded-md border border-destructive/40 p-3 text-sm text-destructive" role="alert">
                {error}
              </p>
            ) : null}

            {done ? null : (
              <>
                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-email`}>Adresse e-mail du compte</Label>
                  <Input
                    id={`${fieldId}-email`}
                    type="email"
                    required
                    autoComplete="off"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    disabled={submitting}
                    placeholder="prenom.nom@cabinet.tn"
                  />
                  <p className="text-xs text-muted-foreground">
                    Demandez-la à la personne au téléphone. Elle doit appartenir à ce cabinet : la console
                    n&apos;affiche pas la liste des comptes, et une adresse d&apos;un autre cabinet est refusée.
                  </p>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor={`${fieldId}-reason`}>Motif de la réinitialisation</Label>
                  {/* A native textarea with the Input's own classes, as the suspension panel uses: a motif is a
                      sentence, and `text-base` keeps it at 16 px so a phone does not zoom on focus. */}
                  <textarea
                    id={`${fieldId}-reason`}
                    required
                    rows={3}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    disabled={submitting}
                    placeholder="Ex. : appel du Dr Ben Salah, téléphone perdu, codes de récupération non conservés"
                    className="w-full rounded-md border border-input bg-transparent px-3 py-2 text-base"
                  />
                  <p className="text-xs text-muted-foreground">
                    Obligatoire. C&apos;est la seule trace de cette opération : elle n&apos;en laisse aucune sur le
                    compte lui-même. Notez comment vous avez vérifié l&apos;identité de la personne.
                  </p>
                </div>
              </>
            )}
          </div>

          {/* Pinned: a `shrink-0` sibling of the scrolling body, so it stays on screen with the keyboard open. */}
          <div className="flex shrink-0 flex-col gap-2 border-t border-border p-4 sm:flex-row-reverse">
            {done ? null : (
              <Button type="submit" variant="destructive" disabled={submitting}>
                {submitting ? "Réinitialisation…" : "Réinitialiser"}
              </Button>
            )}
            <Button type="button" variant="outline" onClick={() => openChanged(false)} disabled={submitting}>
              {done ? "Fermer" : "Revenir"}
            </Button>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  );
}

/**
 * What the reset does, said before the vendor commits.
 *
 * ⚠️ **« Vérifiez son identité » comes first**, because it is the only control on this operation that a screen can
 * carry. Everything else here is reversible by the account owner within minutes; handing their account to somebody
 * who telephoned pretending to be them is not.
 *
 * ⚠️ **What the practice keeps is stated too**, as every refusal in this product does: this removes a credential, not
 * access to records, and a vendor who thinks otherwise will hesitate over a routine support call.
 */
function Consequence() {
  return (
    <div className="rounded-md border border-destructive/40 p-3 text-sm" role="note">
      <p className="font-medium">Vérifiez d&apos;abord l&apos;identité de la personne.</p>
      <p className="mt-1 text-muted-foreground">
        Cette opération rend un compte accessible avec son mot de passe seul, le temps qu&apos;un nouveau second
        facteur soit enrôlé. C&apos;est le seul risque réel de cette page : quelqu&apos;un qui téléphone en se faisant
        passer pour un praticien obtiendrait exactement ce qu&apos;il cherche.
      </p>
      <p className="mt-2 text-muted-foreground">
        Le compte perdra son application d&apos;authentification et ses codes de récupération, et ses sessions
        ouvertes seront fermées. À sa prochaine connexion il devra en enrôler un nouveau. Ses dossiers, ses documents
        et le reste du cabinet ne sont pas touchés.
      </p>
      <p className="mt-2 text-muted-foreground">
        La personne est prévenue par e-mail et dans l&apos;application, et il lui est dit que c&apos;est le support
        qui est intervenu.
      </p>
    </div>
  );
}

/**
 * Who was actually disarmed, read back from the server rather than assumed from the address that was typed — see the
 * component's own remarks for why that distinction is the point of this panel.
 */
function Outcome({ done }: { done: PlatformSecondFactorReset }) {
  return (
    <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
      <p className="font-medium">Second facteur réinitialisé.</p>
      <p className="mt-1 text-muted-foreground">
        {done.targetName ?? "Compte sans nom renseigné"}
        {done.targetEmail ? ` · ${done.targetEmail}` : ""} · {done.targetRole}
      </p>
      <p className="mt-2 text-muted-foreground">
        Si ce n&apos;est pas la personne que vous aviez au téléphone, rappelez-la : elle devra enrôler un nouveau
        second facteur, et celle que vous vouliez aider n&apos;a rien reçu.
      </p>
      <p className="mt-2 text-muted-foreground">
        Le {formatDateTime(done.resetAt)}. L&apos;opération et son motif sont inscrits au journal des accès.
      </p>
    </div>
  );
}
