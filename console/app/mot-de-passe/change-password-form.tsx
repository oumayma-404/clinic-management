"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { readRefusal } from "@/lib/refusal";

interface ChangePasswordFormProps {
  /**
   * The server's minimum length, or `null` when it could not be read.
   *
   * ⚠️ **`null` means « say nothing and check nothing », never a default number.** A literal here would be
   * exactly the second authority this prop exists to delete — the server enforces the floor on every set-path,
   * so an unread value costs a courtesy hint rather than a wrong refusal.
   */
  passwordMinLength: number | null;
}

export function ChangePasswordForm({ passwordMinLength }: ChangePasswordFormProps) {
  const router = useRouter();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);
    setBusy(true);

    try {
      const response = await fetch("/bff/password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword, newPassword }),
      });

      if (!response.ok) {
        // The form stays open with its input intact — never closed on error.
        setError((await readRefusal(response)).error);
        return;
      }

      router.replace("/login");
      router.refresh();
    } catch {
      setError("Impossible de joindre la console. Vérifiez votre connexion, puis réessayez.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} className="space-y-4" noValidate>
      {error && (
        <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/10 p-3 text-sm">
          {error}
        </p>
      )}

      <div className="space-y-2">
        <Label htmlFor="currentPassword">Mot de passe actuel</Label>
        <Input
          id="currentPassword"
          type="password"
          autoComplete="current-password"
          required
          value={currentPassword}
          onChange={(event) => setCurrentPassword(event.target.value)}
        />
      </div>

      <div className="space-y-2">
        <Label htmlFor="newPassword">Nouveau mot de passe</Label>
        <Input
          id="newPassword"
          type="password"
          autoComplete="new-password"
          required
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
        />
        {passwordMinLength !== null && (
          <p className="text-sm text-muted-foreground">Au moins {passwordMinLength} caractères.</p>
        )}
      </div>

      <Button type="submit" className="w-full" disabled={busy}>
        {busy ? "Enregistrement…" : "Changer le mot de passe"}
      </Button>
    </form>
  );
}
