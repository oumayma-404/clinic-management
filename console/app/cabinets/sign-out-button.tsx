"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { Button } from "@/components/ui/button";

/** Signing out clears the HttpOnly cookie server-side — the page cannot reach it itself, by design. */
export function SignOutButton() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  return (
    <Button
      type="button"
      variant="outline"
      disabled={busy}
      onClick={async () => {
        setBusy(true);
        await fetch("/bff/session", { method: "DELETE" });
        router.replace("/login");
        router.refresh();
      }}
    >
      {busy ? "Déconnexion…" : "Se déconnecter"}
    </Button>
  );
}
