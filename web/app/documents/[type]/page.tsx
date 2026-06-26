import { Suspense } from "react"
import { DocumentEditorContent } from "@/components/document-editor-content"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"

export default function DocumentEditorPage() {
  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
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
        </div>
      </div>
    </ClinicGuard>
  )
}

