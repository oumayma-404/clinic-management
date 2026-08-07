"use client"

import { useEffect } from "react"

/**
 * Root error boundary (App Router). Catches errors thrown in the root layout itself — where `error.tsx`
 * cannot help because the layout (and its providers/styles) failed to render. It replaces the whole
 * document, so it renders its own <html>/<body> (AC-3).
 *
 * <p>⚠️ <b>The hard-coded hex colours below are correct and must stay.</b> Everywhere else in this codebase a
 * literal palette value is a defect — the rule is design tokens only. Here the whole point of the boundary is
 * that the root layout did not render, so nothing may be assumed about the stylesheet: `bg-background`,
 * `text-muted-foreground` and the oklch custom properties they resolve to may all be absent, and a page styled
 * with them would degrade to unstyled black-on-white with invisible buttons — at exactly the moment the user
 * most needs to be told something. Inline styles with literal values are the only treatment that cannot fail.
 * They are the light-theme values on purpose: with no stylesheet there is no `.dark` class to react to either.</p>
 *
 * <p>Same two-exits reasoning as `error.tsx`: `reset()` re-renders the tree, but a deterministic layout-level
 * throw re-throws immediately, so a plain full-document link to `/` is the guaranteed way out. It is an
 * `<a href>` rather than a `next/link` deliberately — the router lives in the tree that just failed, and a full
 * navigation is what actually rebuilds the app. The digest is the only handle support can match to a server log.</p>
 */
export default function GlobalError({
  error,
  reset,
}: {
  error: Error & { digest?: string }
  reset: () => void
}) {
  useEffect(() => {
    console.error(error)
  }, [error])

  return (
    <html lang="fr">
      <body
        style={{
          margin: 0,
          minHeight: "100vh",
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          justifyContent: "center",
          gap: "1rem",
          padding: "1.5rem",
          textAlign: "center",
          fontFamily: "system-ui, -apple-system, sans-serif",
          color: "#0f172a",
          background: "#ffffff",
        }}
      >
        <h2 style={{ fontSize: "1.25rem", fontWeight: 600, margin: 0 }}>
          Une erreur s&apos;est produite
        </h2>
        <p style={{ maxWidth: "28rem", fontSize: "0.875rem", color: "#64748b", margin: 0 }}>
          Une erreur inattendue est survenue. Réessayez, ou revenez au tableau de bord.
        </p>
        <div style={{ display: "flex", flexWrap: "wrap", gap: "0.5rem", justifyContent: "center" }}>
          <button
            type="button"
            onClick={reset}
            style={{
              padding: "0.5rem 1rem",
              borderRadius: "0.375rem",
              border: "none",
              background: "#0f172a",
              color: "#ffffff",
              fontSize: "0.875rem",
              cursor: "pointer",
            }}
          >
            Réessayer
          </button>
          <a
            href="/"
            style={{
              padding: "0.5rem 1rem",
              borderRadius: "0.375rem",
              border: "1px solid #cbd5e1",
              background: "#ffffff",
              color: "#0f172a",
              fontSize: "0.875rem",
              textDecoration: "none",
            }}
          >
            Retour au tableau de bord
          </a>
        </div>
        {error.digest && (
          <p style={{ fontSize: "0.75rem", color: "#64748b", margin: 0 }}>
            Référence : <span style={{ fontFamily: "ui-monospace, monospace" }}>{error.digest}</span>
          </p>
        )}
      </body>
    </html>
  )
}
