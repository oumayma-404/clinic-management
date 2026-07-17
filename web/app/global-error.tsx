"use client"

import { useEffect } from "react"

/**
 * Root error boundary (App Router). Catches errors thrown in the root layout itself — where `error.tsx`
 * cannot help because the layout (and its providers/styles) failed to render. It replaces the whole
 * document, so it renders its own <html>/<body> with inline styles and a plain button (AC-3).
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
          Une erreur inattendue est survenue. Veuillez réessayer.
        </p>
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
      </body>
    </html>
  )
}
