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
import { type PlatformPasswordReset } from "@/lib/api/platform";
import { formatDateTime } from "@/lib/format";

/**
 * « Réinitialiser un mot de passe » — the sibling of `reset-second-factor-dialog.tsx`, for the credential beside
 * the factor.
 *
 * ⚠️ **Why this exists.** There are three ways back from a forgotten password and each fails in the same case. The
 * cabinet's own administrator can reset a colleague's — useless when the person locked out *is* the only
 * administrator. The person can reset it themselves from the login screen — useless when the address on the account
 * is unreachable, which is ordinary for a cabinet whose e-mail was set up once by somebody who has left. And
 * `reset-admin-password` works, but only for whoever holds a shell on the server: before this, that meant a support
 * call answered out of a bash history, with no row in this console's journal and no way for the practice to learn
 * afterwards that anything happened.
 *
 * ⚠️ **The address is typed, not picked from a list**, for the reason its sibling states: the console has no roster
 * of a cabinet's staff and this adds none. The administrator's address is pre-filled because they are who calls, and
 * editable because a doctor or a secretary forgets a password too.
 *
 * ⚠️ **The temporary password is displayed once and read out by voice.** It is deliberately absent from the e-mail
 * the person receives — see {@link PlatformPasswordReset} — so this panel is the only place it will ever exist. The
 * outcome says so plainly rather than leaving the vendor to discover it by closing the sheet.
 *
 * ⚠️ **The second factor is untouched, and the panel says so.** A vendor who believes this reset restored full
 * access will tell the caller they can now sign in, and the caller will meet a six-digit prompt they cannot answer.
 * That is a separate call with its own journal row, deliberately: collapsing the two would let one telephone call
 * defeat both proofs.
 *
 * ⚠️ **Bottom sheet below `lg:`, centred dialog above**, the destructive action a `shrink-0` sibling of a scrolling
 * body so it survives an on-screen keyboard and a 380 px landscape height — `suspend-dialog.tsx`'s shape.
 */
export function ResetClinicPasswordDialog({
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
  const [done, setDone] = useState<PlatformPasswordReset | null>(null);
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

    // ⚠️ Two different confirmations, and the second is the one that matters. An unsaved motif is a nuisance; a
    // one-time password that has not been read out is gone for good, and the only remedy is another reset — which
    // invalidates the one the caller may already be typing.
    if (
      done !== null &&
      !window.confirm(
        "Fermer ? Le mot de passe temporaire ne sera plus affiché. Assurez-vous de l'avoir communiqué.",
      )
    ) {
      return;
    }

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
      const response = await fetch("/bff/mot-de-passe-cabinet", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clinicId, email: email.trim(), reason: reason.trim() }),
      });

      // Read the text and parse under a guard: `response.json().catch(() => ({}))` discards the status, which is the
      // only fact left when a body is unreadable.
      const raw = await response.text();
      let body: { error?: string; code?: string } & Partial<PlatformPasswordReset> = {};
      if (raw.length > 0) {
        try {
          body = JSON.parse(raw) as typeof body;
        } catch {
          body = {};
        }
      }

      if (!response.ok) {
        setError(body.error ?? `Le serveur a refusé l'opération (${response.status}).`);
        // Both refusals leave the panel open with the address intact: they are answered by asking the person on the
        // telephone another question, not by re-reading this page. Nothing was written, so there is nothing to
        // refresh.
        return;
      }

      setDone(body as PlatformPasswordReset);
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
          aria-label={`Réinitialiser le mot de passe d'un compte de ${clinicName}`}
        >
          Réinitialiser un mot de passe
        </Button>
      </SheetTrigger>

      <SheetContent
        side="bottom"
        className="max-h-[85dvh] overflow-hidden p-0 lg:inset-auto lg:top-1/2 lg:left-1/2 lg:max-h-[85dvh] lg:w-[calc(100%-2rem)] lg:max-w-lg lg:-translate-x-1/2 lg:-translate-y-1/2 lg:rounded-xl lg:border"
      >
        <form onSubmit={submit} className="flex max-h-[85dvh] min-h-0 flex-col">
          <SheetHeader className="shrink-0 border-b border-border">
            <SheetTitle>Réinitialiser un mot de passe</SheetTitle>
            {/* The cabinet is named, so several open tabs cannot re-credential an account at the wrong practice. */}
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
                  {/* A native textarea with the Input's own classes, as the two other motif panels use: a motif is a
                      sentence, and `text-base` keeps it at 16 px so a phone does not zoom on focus. */}
                  <textarea
                    id={`${fieldId}-reason`}
                    required
                    rows={3}
                    value={reason}
                    onChange={(e) => setReason(e.target.value)}
                    disabled={submitting}
                    placeholder="Ex. : appel du Dr Ben Salah, mot de passe oublié, adresse e-mail du cabinet inaccessible"
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
 * carry — and here the operation produces a credential rather than merely removing a protection, so the caller who
 * lied about who they are leaves with something usable.
 *
 * ⚠️ **What is NOT reset is stated as plainly as what is.** A vendor who believes this restored access will tell the
 * caller to sign in, and the caller will meet a code prompt they cannot answer.
 */
function Consequence() {
  return (
    <div className="rounded-md border border-destructive/40 p-3 text-sm" role="note">
      <p className="font-medium">Vérifiez d&apos;abord l&apos;identité de la personne.</p>
      <p className="mt-1 text-muted-foreground">
        Cette opération remplace le mot de passe d&apos;un compte par un mot de passe temporaire, affiché ici une
        seule fois. C&apos;est le seul risque réel de cette page : quelqu&apos;un qui téléphone en se faisant passer
        pour un praticien obtiendrait un identifiant qui fonctionne.
      </p>
      <p className="mt-2 text-muted-foreground">
        Son second facteur n&apos;est <strong>pas</strong> touché : le code à six chiffres lui sera toujours demandé.
        Si elle a aussi perdu son téléphone, c&apos;est l&apos;autre bouton de cette section, et cela fait deux
        opérations distinctes au journal.
      </p>
      <p className="mt-2 text-muted-foreground">
        Ses sessions ouvertes seront fermées et elle devra choisir son propre mot de passe à la première connexion.
        Ses dossiers, ses documents et le reste du cabinet ne sont pas touchés.
      </p>
      <p className="mt-2 text-muted-foreground">
        La personne est prévenue par e-mail et dans l&apos;application, et il lui est dit que c&apos;est le support
        qui est intervenu. Le mot de passe temporaire, lui, n&apos;est jamais envoyé par e-mail : vous le lui
        communiquez de vive voix.
      </p>
    </div>
  );
}

/**
 * Who was re-credentialled, read back from the server rather than assumed from the address that was typed, and the
 * one-time password itself.
 *
 * ⚠️ **The password is the loudest thing here and is marked as read-once.** The vendor is on the telephone while
 * reading it; burying it under three sentences of explanation is how it gets missed and the reset gets run twice —
 * which invalidates the password the caller is halfway through typing.
 */
function Outcome({ done }: { done: PlatformPasswordReset }) {
  return (
    <div className="space-y-3">
      <div className="rounded-md border border-border bg-muted/40 p-3 text-sm" role="status">
        <p className="font-medium">Mot de passe réinitialisé.</p>
        <p className="mt-1 text-muted-foreground">
          {done.targetName ?? "Compte sans nom renseigné"}
          {done.targetEmail ? ` · ${done.targetEmail}` : ""} · {done.targetRole}
        </p>
        <p className="mt-2 text-muted-foreground">
          Si ce n&apos;est pas la personne que vous aviez au téléphone, rappelez-la : son mot de passe a changé et
          celle que vous vouliez aider n&apos;a rien reçu.
        </p>
      </div>

      <div className="rounded-md border border-primary/40 p-3">
        <p className="text-xs font-medium tracking-wide text-muted-foreground uppercase">
          Mot de passe temporaire — affiché une seule fois
        </p>
        {/* `break-all` + a monospace face: it is read aloud character by character, and a generated password can
            contain runs no word-break would split — which at a narrow width would push the panel sideways. */}
        <p className="mt-1 font-mono text-lg break-all select-all">{done.oneTimePassword}</p>
        <p className="mt-2 text-xs text-muted-foreground">
          Communiquez-le de vive voix maintenant. Il n&apos;est enregistré nulle part et cette page ne pourra pas le
          réafficher : la seule solution serait une deuxième réinitialisation, qui annulerait celui-ci.
        </p>
      </div>

      <p className="text-sm text-muted-foreground">
        Le {formatDateTime(done.resetAt)}. L&apos;opération et son motif sont inscrits au journal des accès.
      </p>
    </div>
  );
}
