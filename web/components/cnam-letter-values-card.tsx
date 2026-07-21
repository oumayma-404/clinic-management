"use client"

import { useState, useEffect } from "react"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Coins } from "lucide-react"
import { cnamNomenclatureApi } from "@/lib/api/cnam-nomenclature"
import type { CnamLetterValueDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { toast } from "sonner"

interface CnamLetterValuesCardProps {
  onChanged: () => void
  // Bumped by the parent to trigger an in-place refetch (instead of remounting via `key`, which discarded
  // any half-typed VLC draft here and could setState after unmount).
  reloadToken?: number
}

export function CnamLetterValuesCard({ onChanged, reloadToken }: CnamLetterValuesCardProps) {
  const [values, setValues] = useState<CnamLetterValueDto[]>([])
  const [drafts, setDrafts] = useState<Record<string, string>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [savingId, setSavingId] = useState<string | null>(null)

  // Refetch in place on mount and whenever the parent bumps reloadToken; the `active` guard prevents a
  // setState after unmount if torn down mid-request.
  useEffect(() => {
    let active = true
    const run = async () => {
      try {
        setLoading(true)
        setError(null)
        const data = await cnamNomenclatureApi.listLetterValues()
        if (!active) return
        setValues(data)
        setDrafts(Object.fromEntries(data.map((v) => [v.id, String(v.value)])))
      } catch (err) {
        if (active) setError(err instanceof ApiError ? err.message : "Échec du chargement des valeurs.")
      } finally {
        if (active) setLoading(false)
      }
    }
    run()
    return () => {
      active = false
    }
  }, [reloadToken])

  const save = async (v: CnamLetterValueDto) => {
    const parsed = Number.parseFloat((drafts[v.id] ?? "").replace(",", "."))
    if (!Number.isFinite(parsed) || parsed < 0) {
      toast.error("La valeur doit être un nombre positif.")
      return
    }
    try {
      setSavingId(v.id)
      await cnamNomenclatureApi.updateLetterValue(v.id, parsed)
      toast.success(`Valeur de « ${v.lettreCle} » mise à jour.`)
      onChanged() // parent bumps reloadToken → in-place refetch, no remount / no lost sibling draft
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la mise à jour.")
    } finally {
      setSavingId(null)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <Coins className="h-5 w-5" />
          Valeurs de la lettre clé (VLC)
        </CardTitle>
        <CardDescription>
          Valeur en dinars par lettre clé, utilisée pour l'estimation indicative du remboursement (non
          contractuelle).
        </CardDescription>
      </CardHeader>
      <CardContent>
        {error && (
          <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200">
            {error}
          </div>
        )}
        {loading ? (
          <p className="text-center text-muted-foreground">Chargement des valeurs…</p>
        ) : (
          <div className="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Lettre clé</TableHead>
                  <TableHead>Valeur (TND)</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Action</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {values.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={4} className="h-16 text-center text-muted-foreground">
                      Aucune valeur configurée
                    </TableCell>
                  </TableRow>
                ) : (
                  values.map((v) => {
                    const dirty = (drafts[v.id] ?? "") !== String(v.value)
                    return (
                      <TableRow key={v.id}>
                        <TableCell>
                          <Badge variant="outline">{v.lettreCle}</Badge>
                        </TableCell>
                        <TableCell>
                          <Input
                            type="number"
                            min="0"
                            step="0.001"
                            aria-label={`Valeur (TND) pour ${v.lettreCle}`}
                            value={drafts[v.id] ?? ""}
                            onChange={(e) => setDrafts((d) => ({ ...d, [v.id]: e.target.value }))}
                            className="max-w-32"
                          />
                        </TableCell>
                        <TableCell>
                          {v.isProvisional && (
                            <Badge variant="outline" className="border-amber-400 text-amber-700 dark:text-amber-300">
                              À vérifier
                            </Badge>
                          )}
                        </TableCell>
                        <TableCell className="text-right">
                          <Button
                            size="sm"
                            variant="outline"
                            disabled={!dirty || savingId === v.id}
                            onClick={() => save(v)}
                          >
                            {savingId === v.id ? "…" : "Enregistrer"}
                          </Button>
                        </TableCell>
                      </TableRow>
                    )
                  })
                )}
              </TableBody>
            </Table>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
