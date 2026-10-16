"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { ClipboardCheck, Printer } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { AppLoader } from "@/components/ui/app-loader"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import { ModeSegmented } from "@/components/ui/mode-segmented"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { useSession } from "@/lib/auth/session"
import { relayApi, type RelayReviewItemDto } from "@/lib/api/relay"
import type { PagedResponse } from "@/lib/api/paging"
import { useUrlFilterSeed, useUrlFilters } from "@/lib/hooks/use-url-filters"
import { formatDateTime, quoteFr } from "@/lib/format"
import { getErrorMessage, showErrorToast } from "@/lib/errors"

type ListKey = "a-verifier" | "a-reprendre"

const LISTS: { value: ListKey; label: string }[] = [
  { value: "a-verifier", label: "À vérifier" },
  { value: "a-reprendre", label: "À reprendre" },
]

/** One side of a line: what it holds, and who put it there. A side with no record says why, never a dash. */
function Side({ summary, by, at, missing }: { summary?: string | null; by?: string | null; at?: string | null; missing: string }) {
  return (
    <div className="min-w-0 space-y-0.5">
      <p className={summary ? "break-words" : "text-muted-foreground italic"}>{summary ?? missing}</p>
      {(by || at) && (
        <p className="text-xs text-muted-foreground">
          {[by, at ? formatDateTime(at) : null].filter(Boolean).join(" · ")}
        </p>
      )}
    </div>
  )
}

function CloudSide({ item }: { item: RelayReviewItemDto }) {
  return (
    <Side
      summary={item.cloudSummary}
      by={item.cloudChangedBy}
      at={item.cloudChangedAtUtc}
      missing={item.kind === "ToReEnter" ? "Absent du cloud" : "Supprimé dans le cloud"}
    />
  )
}

function CabinetSide({ item }: { item: RelayReviewItemDto }) {
  return (
    <Side
      summary={item.cabinetSummary}
      by={item.cabinetChangedBy}
      at={item.cabinetChangedAtUtc}
      missing={item.kind === "CloudOnly" ? "Non modifié au cabinet" : "Supprimé au cabinet"}
    />
  )
}

/** The record the line is about, with why it is listed and — AC-7.5 — the paper to redo. */
function Record({ item }: { item: RelayReviewItemDto }) {
  return (
    <div className="min-w-0 space-y-1">
      <p className="font-medium break-words">{item.cabinetSummary ?? item.cloudSummary ?? item.tableLabel}</p>
      <p className="text-xs text-muted-foreground">{item.kindLabel}</p>
      {item.warning && <p className="text-xs font-medium text-destructive">{item.warning}</p>}
    </div>
  )
}

export default function RelayReturnsPage() {
  const { user, isLoading: sessionLoading } = useSession()
  const isAdmin = user?.role === "admin"

  const initial = useUrlFilterSeed()
  const [list, setList] = useState<ListKey>(initial.get("liste") === "a-reprendre" ? "a-reprendre" : "a-verifier")
  const [showDone, setShowDone] = useState(initial.get("traitees") === "1")
  const reEnter = list === "a-reprendre"

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [data, setData] = useState<PagedResponse<RelayReviewItemDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [marking, setMarking] = useState<string | null>(null)

  const signature = JSON.stringify([list, showDone])
  const firstRun = useRef(true)
  useEffect(() => {
    if (firstRun.current) {
      firstRun.current = false
      return
    }
    setPage(1)
  }, [signature])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setData(await relayApi.reviewItems({ reEnter, includeReviewed: showDone, page, pageSize }))
      setError(null)
    } catch (err) {
      // A failed read must never read as « rien à vérifier » — that would say the cut left nothing behind.
      setError(getErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }, [reEnter, showDone, page, pageSize])

  useEffect(() => {
    if (isAdmin) void load()
  }, [isAdmin, load])

  useUrlFilters({
    liste: reEnter ? "a-reprendre" : undefined,
    traitees: showDone ? "1" : undefined,
    page: page > 1 ? page : undefined,
  })

  const mark = async (item: RelayReviewItemDto) => {
    if (marking) return
    setMarking(item.id)
    try {
      await relayApi.markReviewItemSeen(item.id)
      toast.success(reEnter ? "Marqué « Repris »" : "Marqué « Vu »")
      await load()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setMarking(null)
    }
  }

  const verb = reEnter ? "Repris" : "Vu"
  const markButton = (item: RelayReviewItemDto, className?: string) =>
    item.reviewedAtUtc ? (
      <span className="text-xs text-muted-foreground">
        {verb} le {formatDateTime(item.reviewedAtUtc)}
      </span>
    ) : (
      <Button
        size="sm"
        variant="outline"
        className={`coarse:min-h-11 print:hidden ${className ?? ""}`}
        disabled={marking !== null}
        aria-label={`Marquer ${quoteFr(verb)} : ${item.cabinetSummary ?? item.cloudSummary ?? item.tableLabel}`}
        onClick={() => void mark(item)}
      >
        {marking === item.id ? "…" : verb}
      </Button>
    )

  const items = data?.items ?? []

  return (
    <ClinicGuard>
      <AppShell width={isAdmin ? "7xl" : "none"} gutter={isAdmin} contentClassName={isAdmin ? "space-y-6" : undefined}>
        {sessionLoading ? (
          <AppLoader />
        ) : !isAdmin ? (
          <AccessDeniedCard
            title="Réservé aux administrateurs"
            description="Ce que le PC de secours a laissé à vérifier ou à reprendre après une coupure est réservé aux administrateurs du cabinet."
          />
        ) : (
          <>
            <PageHeader
              title="Retours du PC de secours"
              actions={
                <Button variant="outline" className="print:hidden" onClick={() => window.print()}>
                  <Printer className="size-4" aria-hidden="true" />
                  Imprimer
                </Button>
              }
            />

            <Card className="print:hidden">
              <CardContent className="flex flex-wrap items-center gap-x-6 gap-y-3">
                <ModeSegmented value={list} onChange={setList} options={LISTS} ariaLabel="Liste" className="sm:w-80" />
                <div className="flex items-center gap-2">
                  <Switch id="relay-returns-done" checked={showDone} onCheckedChange={setShowDone} />
                  <Label htmlFor="relay-returns-done">Afficher les lignes traitées</Label>
                </div>
              </CardContent>
            </Card>

            {error ? (
              <LoadFailureNotice message={error} onRetry={() => void load()} />
            ) : loading && !data ? (
              <Card>
                <CardContent className="space-y-3 py-6">
                  {Array.from({ length: 4 }).map((_, i) => (
                    <div key={i} className="h-12 animate-pulse rounded-md bg-muted/60" />
                  ))}
                </CardContent>
              </Card>
            ) : items.length === 0 ? (
              showDone ? (
                <EmptyState
                  icon={ClipboardCheck}
                  title={reEnter ? "Aucun enregistrement à reprendre" : "Aucune modification à vérifier"}
                  description={
                    reEnter
                      ? "Aucune coupure reprise en main n'a laissé de travail sur le PC de secours."
                      : "Aucun retour de coupure n'a trouvé de modification faite des deux côtés."
                  }
                />
              ) : (
                <EmptyState
                  icon={ClipboardCheck}
                  title={reEnter ? "Rien à reprendre" : "Rien à vérifier"}
                  description="Toutes les lignes sont traitées."
                  action={
                    <Button variant="outline" onClick={() => setShowDone(true)}>
                      Afficher les lignes traitées
                    </Button>
                  }
                />
              )
            ) : (
              <>
                <div className={TABLE_ONLY_LG}>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Enregistrement</TableHead>
                        <TableHead>Dans le cloud</TableHead>
                        <TableHead>Au cabinet</TableHead>
                        <TableHead className="print:hidden">
                          <span className="sr-only">Action</span>
                        </TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {items.map((item) => (
                        <TableRow key={item.id}>
                          <TableCell>
                            <Record item={item} />
                          </TableCell>
                          <TableCell>
                            <CloudSide item={item} />
                          </TableCell>
                          <TableCell>
                            <CabinetSide item={item} />
                          </TableCell>
                          <TableCell className="text-end">{markButton(item)}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>

                <div className={CARDS_ONLY_LG}>
                  <CardList
                    items={items}
                    ariaLabel={reEnter ? "À reprendre" : "Modifications à vérifier"}
                    getKey={(i) => i.id}
                    title={(i) => i.cabinetSummary ?? i.cloudSummary ?? i.tableLabel}
                    subtitle={(i) => i.kindLabel}
                    underTitle={(i) =>
                      i.warning ? <p className="text-xs font-medium text-destructive">{i.warning}</p> : null
                    }
                    status={(i) =>
                      i.reviewedAtUtc ? (
                        <Badge variant="secondary">{verb}</Badge>
                      ) : null
                    }
                    fields={(i) => [
                      { label: "Dans le cloud", value: <CloudSide item={i} /> },
                      { label: "Au cabinet", value: <CabinetSide item={i} /> },
                    ]}
                    primaryAction={(i) => (i.reviewedAtUtc ? null : markButton(i, "w-full"))}
                  />
                </div>

                {data && (
                  <DataTablePagination
                    page={data}
                    onPageChange={setPage}
                    onPageSizeChange={(size) => {
                      setPageSize(size)
                      setPage(1)
                    }}
                    loading={loading}
                    label={["ligne", "lignes"]}
                  />
                )}
              </>
            )}
          </>
        )}
      </AppShell>
    </ClinicGuard>
  )
}
