import { Suspense } from "react"
import Link from "next/link"
import { DocumentEditorContent } from "@/components/document-editor-content"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { AppLoader } from "@/components/ui/app-loader"

export default async function DocumentEditorPage({ params }: { params: Promise<{ type: string }> }) {
  const { type } = await params

  return (
    <ClinicGuard>
      {/* The editor owns the whole area and scrolls its own columns, so `<main>` is a flex column with no
          gutter and no page scroller — both branches below stretch through their own `flex-1`.
          ⚠️ `AppShell` is intentionally not a client component, which is what lets this page stay `async`. */}
      <AppShell width="none" gutter={false} mainClassName="flex flex-col overflow-hidden">
        {/* ⚠️ `honoraires` used to be intercepted here and sent to Factures. It is an ordinary printable
            document again — TND, no number, no ledger row — so it goes through the editor like every other
            type. The numbered fiscal note is still the Factures module's, and nothing here creates one. */}
        <Suspense
          fallback={
            <AppLoader className="flex-1" />
          }
        >
          <DocumentEditorContent />
        </Suspense>
      </AppShell>
    </ClinicGuard>
  )
}
