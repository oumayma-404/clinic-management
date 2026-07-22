"use client"

import { useState, useEffect } from "react"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
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
import { ClipboardList, Pencil, Trash2, Plus, AlertTriangle, CheckCircle2 } from "lucide-react"
import { dentalActsApi } from "@/lib/api/dental-acts"
import type { DentalActDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDT } from "@/lib/format"
import { toast } from "sonner"

interface DentalActsTableProps {
  onEdit: (act: DentalActDto) => void
  onAdd: () => void
  onChanged: () => void
  // Bumped by the parent (after any write or realtime signal) to trigger an in-place refetch.
  reloadToken?: number
}

export function DentalActsTable({ onEdit, onAdd, onChanged, reloadToken }: DentalActsTableProps) {
  const [acts, setActs] = useState<DentalActDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [actToDelete, setActToDelete] = useState<DentalActDto | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [confirming, setConfirming] = useState(false)

  useEffect(() => {
    let active = true
    const run = async () => {
      try {
        setLoading(true)
        setError(null)
        // Admin screen: include deactivated rows too.
        const data = await dentalActsApi.list(undefined, undefined, true)
        if (active) setActs(data)
      } catch (err) {
        if (active) setError(err instanceof ApiError ? err.message : "Échec du chargement du catalogue.")
      } finally {
        if (active) setLoading(false)
      }
    }
    run()
    return () => {
      active = false
    }
  }, [reloadToken])

  const confirmDelete = async () => {
    if (!actToDelete) return
    try {
      setDeleting(true)
      await dentalActsApi.deactivate(actToDelete.id)
      toast.success(`Acte « ${actToDelete.codeActe} » désactivé.`)
      setActToDelete(null)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la désactivation.")
    } finally {
      setDeleting(false)
    }
  }

  const handleConfirmData = async () => {
    try {
      setConfirming(true)
      await dentalActsApi.confirmData()
      toast.success("Données du catalogue confirmées.")
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la confirmation.")
    } finally {
      setConfirming(false)
    }
  }

  const hasProvisional = acts.some((a) => a.isProvisional)

  if (loading) {
    return (
      <Card>
        <CardContent className="p-6">
          <p className="text-center text-muted-foreground">Chargement du catalogue…</p>
        </CardContent>
      </Card>
    )
  }

  return (
    <>
      {hasProvisional && (
        <div className="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950 dark:text-amber-200">
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-4 w-4 shrink-0" />
            <span>
              Données provisoires « à vérifier ». Confirmez-les avec la nomenclature en vigueur avant toute
              utilisation clinique. Rien n'est bloqué en attendant.
            </span>
          </div>
          <Button size="sm" variant="outline" onClick={handleConfirmData} disabled={confirming} className="gap-2">
            <CheckCircle2 className="h-4 w-4" />
            {confirming ? "Confirmation…" : "Confirmer les données"}
          </Button>
        </div>
      )}

      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="flex items-center gap-2">
              <ClipboardList className="h-5 w-5" />
              Catalogue des actes dentaires
              <Badge variant="secondary" className="ml-2">
                {acts.length} {acts.length === 1 ? "acte" : "actes"}
              </Badge>
            </CardTitle>
            <Button onClick={onAdd} size="sm" className="gap-2">
              <Plus className="h-4 w-4" />
              Ajouter un acte
            </Button>
          </div>
        </CardHeader>
        <CardContent>
          {error && (
            <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
              {error}
            </div>
          )}
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Code acte</TableHead>
                  <TableHead>Désignation</TableHead>
                  <TableHead>Lettre clé</TableHead>
                  <TableHead>Coefficient</TableHead>
                  <TableHead>Catégorie</TableHead>
                  <TableHead className="text-right">Tarif</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {acts.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={8} className="h-24 text-center">
                      <p className="text-muted-foreground">Aucun acte dans le catalogue</p>
                    </TableCell>
                  </TableRow>
                ) : (
                  acts.map((act) => (
                    <TableRow key={act.id} className={act.isActive ? "" : "opacity-50"}>
                      <TableCell className="font-mono text-sm font-medium text-foreground">{act.codeActe}</TableCell>
                      <TableCell className="text-foreground">{act.designationFr}</TableCell>
                      <TableCell>
                        <Badge variant="outline">{act.lettreCle}</Badge>
                      </TableCell>
                      <TableCell className="text-muted-foreground">{act.coefficient ?? "—"}</TableCell>
                      <TableCell className="text-muted-foreground">{act.category}</TableCell>
                      <TableCell className="text-right text-muted-foreground">
                        {act.defaultFee != null ? formatDT(act.defaultFee) : "—"}
                      </TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          {!act.isActive && <Badge variant="secondary">Inactif</Badge>}
                          {act.requiresAccordPrealable && (
                            <Badge variant="outline" className="border-sky-400 text-sky-700 dark:text-sky-300">
                              Accord préalable
                            </Badge>
                          )}
                          {act.isProvisional && (
                            <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                              À vérifier
                            </Badge>
                          )}
                        </div>
                      </TableCell>
                      <TableCell className="text-right">
                        <div className="flex justify-end gap-2">
                          <Button variant="ghost" size="sm" onClick={() => onEdit(act)} className="h-8 gap-1">
                            <Pencil className="h-3 w-3" />
                            Modifier
                          </Button>
                          {act.isActive && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setActToDelete(act)}
                              className="h-8 gap-1 text-destructive hover:text-destructive"
                            >
                              <Trash2 className="h-3 w-3" />
                              Désactiver
                            </Button>
                          )}
                        </div>
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </div>
        </CardContent>
      </Card>

      <AlertDialog open={actToDelete !== null} onOpenChange={(open) => !open && setActToDelete(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Désactiver cet acte ?</AlertDialogTitle>
            <AlertDialogDescription>
              L'acte <span className="font-semibold">{actToDelete?.codeActe}</span> sera désactivé et
              n'apparaîtra plus dans le sélecteur d'actes des plans de traitement. Les plans déjà enregistrés
              ne sont pas modifiés.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Désactivation…" : "Désactiver"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
