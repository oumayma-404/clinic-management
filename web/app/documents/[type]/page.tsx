import { Suspense } from "react"
import Link from "next/link"
import { FileX } from "lucide-react"
import { DocumentEditorContent } from "@/components/document-editor-content"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { AppLoader } from "@/components/ui/app-loader"
import { Button } from "@/components/ui/button"
import { documentTypeLabel, isWithdrawnDocumentType } from "@/lib/documents"

export default async function DocumentEditorPage({ params }: { params: Promise<{ type: string }> }) {
  const { type } = await params

  /*
   * ⚠️ **This route never validated its segment, and that is exactly why the refusal has to live here.**
   *
   * The two official CNAM forms (`bulletin-cnam`, `arret-travail`) went with the rest of the CNAM interface —
   * `features/cnam-ui-withdrawal/notes.md` — so `document-editor-content.tsx` has no branch for either any
   * more. Without this gate, « Modifier » on an already-saved bulletin would mount the GENERIC editor over it:
   * an empty free-form document on top of a legal form, one « Enregistrer » away from replacing its content.
   *
   * ⚠️ The gate is on the ROUTE, not inside the editor, and that is deliberate: the editor is a client
   * component that calls a long list of hooks, and an early return keyed on a route param would change hook
   * order the moment somebody navigates between two document types. Here it simply never mounts.
   *
   * ⚠️ Reading a saved one is untouched. `DocumentPreviewDialog` frames the **server-rendered** PDF, and both
   * overlay renderers are still on the API — so Imprimer and Télécharger still produce the real form. Only
   * editing is withdrawn.
   */
  if (isWithdrawnDocumentType(type)) {
    return (
      <ClinicGuard>
        <AppShell width="3xl">
          <div className="flex flex-col items-center gap-4 rounded-lg border bg-card p-8 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted">
              <FileX className="h-6 w-6 text-muted-foreground" aria-hidden="true" />
            </div>
            <div className="space-y-2">
              <h1 className="text-lg font-semibold text-foreground">
                {documentTypeLabel(type)}&nbsp;: formulaire indisponible
              </h1>
              {/* Says what still works, in the same breath. « Indisponible » alone reads as « vos documents sont
                  perdus » to somebody who has one in the patient's dossier. */}
              <p className="text-sm text-muted-foreground">
                Ce formulaire n&apos;est plus proposé dans l&apos;application. Les documents déjà enregistrés
                restent dans le dossier du patient&nbsp;: ils s&apos;ouvrent, s&apos;impriment et se téléchargent
                normalement depuis son onglet «&nbsp;Documents&nbsp;».
              </p>
            </div>
            <div className="flex flex-wrap justify-center gap-2">
              <Button asChild>
                <Link href="/documents">Retour aux documents</Link>
              </Button>
              <Button asChild variant="outline">
                <Link href="/patients">Ouvrir un dossier patient</Link>
              </Button>
            </div>
          </div>
        </AppShell>
      </ClinicGuard>
    )
  }

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
