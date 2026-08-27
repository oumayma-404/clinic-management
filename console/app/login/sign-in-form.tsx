"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { readJson, readRefusal } from "@/lib/refusal";

/**
 * The console's sign-in, its enrolment step and its recovery path — one component, three modes.
 *
 * ⚠️ **One component because they share the credential fields and, more importantly, the state between them.**
 * An account that signs in with a password and is told « enrol your factor first » must arrive at the enrolment
 * form with its address and password intact: re-typing them is not friction here, it is the moment somebody
 * decides the second factor is the problem. The server drives that transition through the refusal's `code`
 * (`totp_enrolment_required`), never through a French sentence this file would have to match.
 */

type Mode = "login" | "enrol" | "recovery" | "codes";

export function SignInForm() {
  const router = useRouter();

  const [mode, setMode] = useState<Mode>("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [totpCode, setTotpCode] = useState("");
  const [recoveryCode, setRecoveryCode] = useState("");
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [remaining, setRemaining] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const response = await fetch("/bff/session", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(
          mode === "recovery"
            ? { action: "recovery", email, password, recoveryCode }
            : { action: mode === "enrol" ? "enrol" : "login", email, password, totpCode },
        ),
      });

      if (!response.ok) {
        const body = await readRefusal(response);

        // The one branch that is a destination rather than a message: the account exists and its password is
        // right, but the factor has never been bound. Sending it to the enrolment form is AC-1.3a; showing the
        // sentence and stopping would leave the operator with no way forward from a correct password.
        if (body.code === "totp_enrolment_required") {
          setMode("enrol");
          setTotpCode("");
          setError(
            "Ce compte doit d'abord enrôler son second facteur. Saisissez un code généré à partir du secret " +
              "fourni par la commande « platform-account ».",
          );
          return;
        }

        setError(body.error);
        return;
      }

      const body = await readJson<{ recoveryCodes?: string[]; recoveryCodesRemaining?: number | null }>(
        response,
      );

      if (mode === "enrol") {
        // Shown once and never retrievable — so the flow stops here deliberately, on a screen the operator has
        // to acknowledge, rather than signing in and navigating away from the only copy that will ever exist.
        setRecoveryCodes(body?.recoveryCodes ?? []);
        setMode("codes");
        return;
      }

      setRemaining(body?.recoveryCodesRemaining ?? null);
      router.replace("/cabinets");
      router.refresh();
    } catch {
      setError("Impossible de joindre la console. Vérifiez votre connexion, puis réessayez.");
    } finally {
      setBusy(false);
    }
  }

  if (mode === "codes") {
    return (
      <div className="space-y-4" role="status">
        <div className="rounded-md border border-border bg-muted/40 p-3">
          <h2 className="text-base font-semibold">Codes de récupération</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            Notez-les maintenant : ils ne seront plus jamais affichés. Chacun ne sert qu&apos;une fois.
          </p>
        </div>

        <ul className="grid gap-2 font-mono text-sm sm:grid-cols-2">
          {recoveryCodes.map((code) => (
            <li key={code} className="rounded border border-border px-3 py-2 break-all">
              {code}
            </li>
          ))}
        </ul>

        <Button
          type="button"
          className="w-full"
          onClick={() => {
            setMode("login");
            setTotpCode("");
            setError(null);
          }}
        >
          J&apos;ai noté mes codes — se connecter
        </Button>
      </div>
    );
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error && (
        // role="alert" so a screen reader announces it: on a phone this banner is the only feedback channel.
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          {error}
        </p>
      )}

      {remaining !== null && (
        <p role="status" className="rounded-md border border-border bg-muted/40 p-3 text-sm">
          Il vous reste {remaining} code(s) de récupération.
        </p>
      )}

      <div className="space-y-2">
        <Label htmlFor="email">Adresse e-mail</Label>
        <Input
          id="email"
          name="email"
          type="email"
          autoComplete="username"
          required
          value={email}
          onChange={(event) => setEmail(event.target.value)}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="password">Mot de passe</Label>
        <Input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </div>

      {mode === "recovery" ? (
        <div className="space-y-2">
          <Label htmlFor="recoveryCode">Code de récupération</Label>
          <Input
            id="recoveryCode"
            name="recoveryCode"
            // Not `type="password"`: the operator is copying this from paper and needs to see what they typed.
            autoComplete="one-time-code"
            required
            className="font-mono"
            value={recoveryCode}
            onChange={(event) => setRecoveryCode(event.target.value)}
          />
          <p className="text-sm text-muted-foreground">
            Le code est utilisé une seule fois, même si la connexion échoue ensuite.
          </p>
        </div>
      ) : (
        <div className="space-y-2">
          <Label htmlFor="totpCode">Code de vérification</Label>
          <Input
            id="totpCode"
            name="totpCode"
            // inputMode numeric raises the digit keypad on a phone; `type="text"` keeps a leading zero, which
            // `type="number"` would silently eat.
            inputMode="numeric"
            autoComplete="one-time-code"
            required
            className="font-mono tracking-widest"
            value={totpCode}
            onChange={(event) => setTotpCode(event.target.value)}
          />
        </div>
      )}

      <Button type="submit" className="w-full" disabled={busy}>
        {busy ? "Vérification…" : mode === "enrol" ? "Enrôler le second facteur" : "Se connecter"}
      </Button>

      <div className="text-center text-sm">
        <button
          type="button"
          // A real button with a real hit area — never a bare link at 12 px, which on a finger is the control
          // somebody needs precisely when they have already lost their phone.
          className="touch-target rounded px-2 py-1 text-muted-foreground underline underline-offset-4 hover:text-foreground"
          onClick={() => {
            setMode(mode === "recovery" ? "login" : "recovery");
            setError(null);
          }}
        >
          {mode === "recovery"
            ? "Revenir au code de vérification"
            : "Utiliser un code de récupération"}
        </button>
      </div>
    </form>
  );
}
