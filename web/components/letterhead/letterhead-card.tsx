"use client"

import { useState } from "react"
import { toast } from "sonner"
import { ChevronDown, FileImage, Loader2 } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { LetterheadImportDialog } from "@/components/letterhead/letterhead-import-dialog"
import { clinicsApi } from "@/lib/api/clinics"
import { showErrorToast } from "@/lib/errors"
import { useClinicLetterhead } from "@/lib/letterhead/use-clinic-letterhead"
import { cn } from "@/lib/utils"

interface LetterheadCardProps {
  /** The settings page's own icon-chip classes, so this card reads as one of its sections. */
  chipClassName: string
  /** `PUT /api/clinics/letterhead` is AdminOnly; anyone else reads the card without its controls. */
  canEdit: boolean
}

interface BandUrls {
  header: string
  footer: string | null
  body: string | null
}

/**
 * « En-tête des documents » — whether the cabinet's documents print on its own paper, shown as a small A4 page.
 * Import, replace and withdraw all live here; the cutting itself is `LetterheadImportDialog`.
 */
export function LetterheadCard({ chipClassName, canEdit }: LetterheadCardProps) {
  const [collapsed, setCollapsed] = useState(true)
  const { letterhead, failed, header, footer, body, reload, set } = useClinicLetterhead()
  const [importOpen, setImportOpen] = useState(false)
  const [confirmRemove, setConfirmRemove] = useState(false)
  const [removing, setRemoving] = useState(false)

  const remove = async () => {
    if (!letterhead) return
    setRemoving(true)
    try {
      set(await clinicsApi.removeLetterhead(letterhead.version))
      toast.success("En-tête retiré")
      setConfirmRemove(false)
    } catch (error) {
      showErrorToast(error)
      reload()
    } finally {
      setRemoving(false)
    }
  }

  const onPaper = letterhead?.hasHeader === true
  const bands: BandUrls | null = header
    ? { header: header.url, footer: footer?.url ?? null, body: body?.url ?? null }
    : null

  return (
    <Card>
      <CardHeader className="pb-3">
        <button
          type="button"
          onClick={() => setCollapsed((value) => !value)}
          aria-expanded={!collapsed}
          className="touch-target flex w-full flex-wrap items-center gap-2.5 py-2 text-left transition-opacity hover:opacity-70"
        >
          <CardTitle className="flex min-w-0 flex-1 items-center gap-2.5 text-base leading-snug">
            <span aria-hidden="true" className={chipClassName}>
              <FileImage className="size-4" strokeWidth={1.75} />
            </span>
            En-tête des documents
          </CardTitle>
          {letterhead && (
            <Badge variant="outline" className="font-normal">
              {onPaper ? "Papier du cabinet" : "En-tête texte"}
            </Badge>
          )}
          <ChevronDown
            className={cn("size-4 shrink-0 text-muted-foreground transition-transform", collapsed && "-rotate-90")}
          />
        </button>
      </CardHeader>

      {!collapsed && (
        <CardContent>
          {failed ? (
            <div className="flex flex-wrap items-center gap-3 text-sm" role="alert">
              <span>L'en-tête n'a pas pu être chargé.</span>
              <Button variant="outline" size="sm" onClick={reload}>
                Réessayer
              </Button>
            </div>
          ) : !letterhead ? (
            <Loader2 className="size-5 animate-spin text-muted-foreground" aria-label="Chargement" />
          ) : (
            <div className="flex flex-col gap-5 sm:flex-row sm:items-start">
              <MiniPage bands={onPaper ? bands : null} onPaper={onPaper} />
              <div className="min-w-0 flex-1 space-y-3">
                <p className="text-sm">
                  {onPaper
                    ? "Ordonnances, certificats, devis et notes d'honoraires sont imprimés sur votre papier à en-tête."
                    : "Vos documents portent un en-tête texte avec le nom et les coordonnées du cabinet."}
                </p>
                {canEdit ? (
                  <div className="flex flex-wrap gap-2">
                    <Button onClick={() => setImportOpen(true)}>
                      {onPaper ? "Remplacer" : "Importer mon papier à en-tête"}
                    </Button>
                    {onPaper && (
                      <Button variant="outline" onClick={() => setConfirmRemove(true)}>
                        Retirer
                      </Button>
                    )}
                  </div>
                ) : (
                  <p className="text-xs text-muted-foreground">Modifiable par un administrateur.</p>
                )}
              </div>
            </div>
          )}
        </CardContent>
      )}

      {canEdit && letterhead && (
        <LetterheadImportDialog
          open={importOpen}
          onOpenChange={setImportOpen}
          version={letterhead.version}
          onSaved={set}
        />
      )}

      <AlertDialog open={confirmRemove} onOpenChange={setConfirmRemove}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Retirer le papier à en-tête du cabinet ?</AlertDialogTitle>
            <AlertDialogDescription>
              Les prochains documents reprendront l'en-tête texte. Ceux déjà émis gardent le papier sur lequel ils
              ont été imprimés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={removing}>Retour</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              disabled={removing}
              onClick={(event) => {
                event.preventDefault()
                void remove()
              }}
            >
              Retirer l'en-tête
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  )
}

/** An A4 sheet at a glance: the bands where they print, grey lines where the document goes. */
function MiniPage({ bands, onPaper }: { bands: BandUrls | null; onPaper: boolean }) {
  return (
    <div
      aria-hidden="true"
      className="relative mx-auto flex aspect-[210/297] w-36 shrink-0 flex-col overflow-hidden rounded-sm bg-white shadow-md ring-1 ring-border sm:mx-0"
    >
      {onPaper && bands ? (
        <img src={bands.header} alt="" className="w-full" />
      ) : (
        <div className="space-y-1 px-3 pt-3">
          <div className="h-1.5 w-16 rounded-full bg-black/40" />
          <div className="h-1 w-20 rounded-full bg-black/15" />
          <div className="h-1 w-14 rounded-full bg-black/15" />
        </div>
      )}
      <div
        className="flex-1 space-y-1.5 px-3 pt-4"
        style={onPaper && bands?.body ? { backgroundImage: `url(${bands.body})`, backgroundSize: "100% 100%" } : undefined}
      >
        <div className="mx-auto h-1.5 w-12 rounded-full bg-black/30" />
        {[18, 24, 20, 22, 16].map((width, index) => (
          <div key={index} className="h-1 rounded-full bg-black/10" style={{ width: `${width * 4}px` }} />
        ))}
      </div>
      {onPaper && bands?.footer && <img src={bands.footer} alt="" className="w-full" />}
    </div>
  )
}
