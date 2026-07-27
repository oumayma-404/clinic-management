"use client"

import { useCallback, useEffect, useState } from "react"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useRouter } from "next/navigation"
import { toast } from "sonner"
import { Loader2, HandCoins } from "lucide-react"
import { billingApi } from "@/lib/api/billing"
import { ApiError } from "@/lib/api/client"
import type { ReceivableDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"

export function ReceivablesTable() {
  const router = useRouter()
  const [rows, setRows] = useState<ReceivableDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Bumped by a realtime event to re-run the load below. « Créances » is a debt list a colleague settles
  // from another screen; without this it kept showing balances that had already been paid.
  const [reloadKey, setReloadKey] = useState(0)

  useClinicRealtime(
    [RealtimeResource.Invoices, RealtimeResource.TreatmentPlans],
    useCallback(() => setReloadKey((k) => k + 1), []),
  )

  useEffect(() => {
    let active = true
    const load = async () => {
      setLoading(true)
      setError(null)
      try {
        const data = await billingApi.getReceivables()
        if (active) setRows(data)
      } catch (e) {
        const msg = e instanceof ApiError ? e.message : "Erreur lors du chargement des créances."
        if (active) {
          setError(msg)
          toast.error(msg)
        }
      } finally {
        if (active) setLoading(false)
      }
    }
    load()
    return () => {
      active = false
    }
  }, [reloadKey])

  const total = rows.reduce((sum, r) => sum + r.totalOutstanding, 0)

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center justify-between">
          <span className="flex items-center gap-2">
            <HandCoins className="h-5 w-5 text-muted-foreground" />
            Créances{rows.length > 0 ? ` (${rows.length})` : ""}
          </span>
          {rows.length > 0 && (
            <span className="text-sm font-normal text-muted-foreground">
              Total dû : <span className="font-semibold text-foreground">{formatDT(total)}</span>
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent>
        {loading ? (
          <div className="flex items-center justify-center py-12 text-muted-foreground">
            <Loader2 className="mr-2 h-5 w-5 animate-spin" /> Chargement…
          </div>
        ) : error ? (
          <div className="py-12 text-center text-sm text-destructive">{error}</div>
        ) : rows.length === 0 ? (
          <div className="py-12 text-center text-sm text-muted-foreground">
            Aucune créance — tous les patients sont à jour.
          </div>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Patient</TableHead>
                <TableHead className="text-right">Solde dû</TableHead>
                <TableHead>Échéance la plus ancienne</TableHead>
                <TableHead className="text-right">Retard</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow
                  key={r.patientId}
                  className="cursor-pointer"
                  onClick={() => router.push(`/patients/${r.patientId}`)}
                >
                  <TableCell className="font-medium">{r.patientName}</TableCell>
                  <TableCell className="text-right font-semibold">{formatDT(r.totalOutstanding)}</TableCell>
                  <TableCell>{r.oldestOverdueDate ? formatDateFr(r.oldestOverdueDate) : "—"}</TableCell>
                  <TableCell className="text-right">
                    {r.daysOverdue != null && r.daysOverdue > 0 ? (
                      <Badge variant="destructive">En retard · {r.daysOverdue} j</Badge>
                    ) : (
                      <span className="text-muted-foreground">—</span>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  )
}
