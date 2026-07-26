import { Suspense } from "react"
import Link from "next/link"
import { DocumentEditorContent } from "@/components/document-editor-content"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"

export default async function DocumentEditorPage({ params }: { params: Promise<{ type: string }> }) {
  const { type } = await params

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          {type === "honoraires" ? (
            // The honoraires editor is retired (finding #13): notes d'honoraires are now created as invoices
            // in the Factures module. Guard the route so a direct/legacy link lands on a clear notice instead
            // of the old euro-denominated form (whose save/PDF path is rejected server-side).
            <div className="flex-1 flex items-center justify-center p-6">
              <div className="max-w-md space-y-4 text-center">
                <h1 className="text-xl font-semibold text-foreground">Note d&apos;honoraires</h1>
                <p className="text-muted-foreground">
                  Les notes d&apos;honoraires sont désormais gérées dans le module Factures.
                </p>
                <Link
                  href="/factures"
                  className="inline-flex h-10 items-center justify-center rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90"
                >
                  Aller aux Factures
                </Link>
              </div>
            </div>
          ) : (
            <Suspense
              fallback={
                <div className="flex-1 flex items-center justify-center">
                  <div className="text-center">
                    <div className="w-12 h-12 border-4 border-blue-600 border-t-transparent rounded-full animate-spin mx-auto"></div>
                    <p className="mt-4 text-muted-foreground">Chargement...</p>
                  </div>
                </div>
              }
            >
              <DocumentEditorContent />
            </Suspense>
          )}
        </div>
      </div>
    </ClinicGuard>
  )
}
