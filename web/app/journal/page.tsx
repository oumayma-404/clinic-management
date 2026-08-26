"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { AlertTriangle, FileClock, Cpu } from "lucide-react"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { AccessDeniedCard } from "@/components/ui/access-denied-card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { useSession } from "@/lib/auth/session"
import { auditApi, type AuditEntryDto, type AuditPageDto } from "@/lib/api/audit"
import { useUrlFilterSeed, useUrlFilters } from "@/lib/hooks/use-url-filters"
import { formatDateTime } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"

/** Sentinel for « tous » in a Select — Radix forbids an empty-string item value. */
const ALL = "__all__"

/**
 * `Insert | Update | Delete` with their French labels. An **exhaustive** map over the wire values, so an action
 * the server starts emitting without a label here is a visible gap rather than a blank cell — the same reasoning
 * as `dashboard-links.ts`. Unknown values still pass through verbatim (`actionLabel` from the server wins).
 */
const ACTIONS = [
  { value: "Insert", label: "Création" },
  { value: "Update", label: "Modification" },
  { value: "Delete", label: "Suppression" },
] as const

function actionTone(action: string): string {
  if (action === "Delete") return "bg-destructive-wash text-destructive"
  if (action === "Insert") return "bg-success-wash text-success"
  return "bg-muted text-muted-foreground"
}

/** The actor, with a marker when it is a process rather than a person. */
function Actor({ entry }: { entry: AuditEntryDto }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      {entry.isSystemActor && <Cpu className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />}
      <span className={entry.isSystemActor ? "text-muted-foreground" : undefined}>{entry.actorLabel}</span>
    </span>
  )
}

export default function JournalPage() {
  const { user, isLoading: sessionLoading } = useSession()
  const isAdmin = user?.role === "admin"

  /*
   * ⚠️ Seeded FROM the query string, so a reload and a shared link both work.
   *
   * The six filters lived in component state alone and the URL said nothing, so « le journal de Salma sur la
   * semaine du 20 » was a view nobody could come back to or send to anyone — over 79 pages of ledger, on the one
   * screen whose whole purpose is answering a question about the past. A missing or unreadable param falls back to
   * the default rather than refusing: a stale bookmark shows the full journal, never an error about a query string.
   */
  const initial = useUrlFilterSeed()
  const [entityType, setEntityType] = useState(initial.get("entityType") ?? ALL)
  const [action, setAction] = useState(initial.get("action") ?? ALL)
  /** « Auteur » — the filter this screen had no way to express, over 79 pages of ledger. */
  const [actor, setActor] = useState(initial.get("userId") ?? ALL)
  const [entityId, setEntityId] = useState(initial.get("entityId") ?? "")
  const [from, setFrom] = useState(initial.get("from") ?? "")
  const [to, setTo] = useState(initial.get("to") ?? "")

  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [data, setData] = useState<AuditPageDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // AC-22 applies here too: changing any filter returns to page 1. Keyed on the serialised values, never on an
  // object identity — see `use-paged-list.ts` for why an identity-keyed effect would undo the user's page click.
  const filterSignature = JSON.stringify([entityType, action, actor, entityId, from, to])
  const firstRunRef = useRef(true)
  useEffect(() => {
    if (firstRunRef.current) {
      firstRunRef.current = false
      return
    }
    setPage(1)
  }, [filterSignature])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await auditApi.list({
        entityType: entityType === ALL ? undefined : entityType,
        action: action === ALL ? undefined : action,
        userId: actor === ALL ? undefined : actor,
        entityId: entityId.trim() || undefined,
        from: from || undefined,
        to: to || undefined,
        page,
        pageSize,
      })
      setData(result)
      setError(null)
    } catch (err) {
      // A failed read must never render as « aucune activité » — that would assert the practice did nothing.
      setError(getErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }, [entityType, action, actor, entityId, from, to, page, pageSize])

  useEffect(() => {
    if (isAdmin) void load()
  }, [isAdmin, load])

  const hasFilters =
    entityType !== ALL || action !== ALL || actor !== ALL || entityId.trim() !== "" || from !== "" || to !== ""
  const clearFilters = () => {
    setEntityType(ALL)
    setAction(ALL)
    setActor(ALL)
    setEntityId("")
    setFrom("")
    setTo("")
  }

  // Mirrors the six filters back into the query string — the write half of the seed above.
  useUrlFilters({
    entityType: entityType === ALL ? undefined : entityType,
    action: action === ALL ? undefined : action,
    userId: actor === ALL ? undefined : actor,
    entityId: entityId.trim() || undefined,
    from: from || undefined,
    to: to || undefined,
    page: page > 1 ? page : undefined,
  })

  const entries = data?.items ?? []

  return (
    <ClinicGuard>
      <AppShell
        width={isAdmin ? "7xl" : "none"}
        gutter={isAdmin}
        contentClassName={isAdmin ? "space-y-6" : undefined}
      >
        {sessionLoading ? (
          <p className="p-8 text-center text-muted-foreground">Chargement…</p>
        ) : !isAdmin ? (
          <AccessDeniedCard
            title="Réservé aux administrateurs"
            description="Le journal d'activité retrace qui a créé, modifié ou supprimé chaque dossier. Il est réservé aux administrateurs de la clinique."
          />
        ) : (
          <>
            <PageHeader
              title="Journal d'activité"
              subtitle="Qui a créé, modifié ou supprimé quoi, et quand. Les entrées les plus récentes en premier."
            />

            <Card>
              {/* Six controls now, not five. `lg:grid-cols-3` rather than a sixth column: at 1024 px six
                  columns give each filter ~150 px, which is narrower than « Tous les auteurs ». */}
              <CardContent className="grid gap-4 pt-6 sm:grid-cols-2 lg:grid-cols-3">
                <div className="space-y-1.5">
                  <Label htmlFor="journal-type">Type</Label>
                  <Select value={entityType} onValueChange={setEntityType}>
                    <SelectTrigger id="journal-type">
                      <SelectValue placeholder="Tous les types" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL}>Tous les types</SelectItem>
                      {/* Served with the page — the types this clinic actually has rows for. */}
                      {(data?.entityTypes ?? []).map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="journal-action">Action</Label>
                  <Select value={action} onValueChange={setAction}>
                    <SelectTrigger id="journal-action">
                      <SelectValue placeholder="Toutes les actions" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL}>Toutes les actions</SelectItem>
                      {ACTIONS.map((a) => (
                        <SelectItem key={a.value} value={a.value}>
                          {a.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="journal-actor">Auteur</Label>
                  <Select value={actor} onValueChange={setActor}>
                    <SelectTrigger id="journal-actor">
                      <SelectValue placeholder="Tous les auteurs" />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={ALL}>Tous les auteurs</SelectItem>
                      {/* Served with the page — the actors this clinic actually has rows for, people first. */}
                      {(data?.actors ?? []).map((option) => (
                        <SelectItem key={option.value} value={option.value}>
                          {option.label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="journal-from">Du</Label>
                  <Input
                    id="journal-from"
                    type="date"
                    value={from}
                    onChange={(e) => setFrom(e.target.value)}
                  />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="journal-to">Au</Label>
                  <Input id="journal-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
                </div>

                <div className="space-y-1.5">
                  <Label htmlFor="journal-entity">Identifiant du dossier</Label>
                  <Input
                    id="journal-entity"
                    placeholder="Tout l'historique d'un dossier"
                    value={entityId}
                    onChange={(e) => setEntityId(e.target.value)}
                  />
                </div>
              </CardContent>
            </Card>

            {error ? (
              <Card>
                <CardContent className="flex flex-col items-center gap-3 py-10 text-center">
                  <AlertTriangle className="size-8 text-destructive" aria-hidden="true" />
                  <p className="text-sm text-muted-foreground">{error}</p>
                  <Button variant="outline" onClick={() => void load()}>
                    Réessayer
                  </Button>
                </CardContent>
              </Card>
            ) : loading && !data ? (
              // A skeleton distinct from empty: a card list has no header row, so « vide », « en cours » and
              // « votre filtre ne correspond à rien » would otherwise be the same blank rectangle.
              <Card>
                <CardContent className="space-y-3 py-6">
                  {Array.from({ length: 5 }).map((_, i) => (
                    <div key={i} className="h-12 animate-pulse rounded-md bg-muted/60" />
                  ))}
                </CardContent>
              </Card>
            ) : entries.length === 0 ? (
              <EmptyState
                icon={FileClock}
                title={hasFilters ? "Aucune activité pour ces filtres" : "Aucune activité enregistrée"}
                description={
                  hasFilters
                    ? "Aucune entrée du journal ne correspond aux filtres choisis."
                    : "Le journal se remplit dès qu'un dossier est créé, modifié ou supprimé."
                }
                action={
                  hasFilters ? (
                    <Button variant="outline" onClick={clearFilters}>
                      Effacer les filtres
                    </Button>
                  ) : undefined
                }
              />
            ) : (
              <>
                {/* Two trees, not a reflow: a `display:block` table strips the implicit roles, and a screen
                    reader would announce « Dr Ben Ali Patient Suppression 12/03 » with no field names. */}
                <div className={TABLE_ONLY}>
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Quand</TableHead>
                        <TableHead>Qui</TableHead>
                        <TableHead>Action</TableHead>
                        <TableHead>Dossier</TableHead>
                        <TableHead>Détail</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {entries.map((entry) => (
                        <TableRow key={entry.id}>
                          <TableCell className="whitespace-nowrap text-muted-foreground">
                            {formatDateTime(entry.occurredAt)}
                          </TableCell>
                          <TableCell>
                            <Actor entry={entry} />
                          </TableCell>
                          <TableCell>
                            <Badge className={actionTone(entry.action)} variant="secondary">
                              {entry.actionLabel}
                            </Badge>
                          </TableCell>
                          <TableCell>
                            <span className="text-foreground">{entry.entityLabel}</span>
                            <span className="ms-2 font-mono text-2xs text-muted-foreground">{entry.entityId}</span>
                          </TableCell>
                          <TableCell className="text-muted-foreground">{entry.changedFields ?? "—"}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>

                <div className={CARDS_ONLY}>
                  <CardList
                    items={entries}
                    ariaLabel="Journal d'activité"
                    getKey={(e) => e.id}
                    title={(e) => e.entityLabel}
                    subtitle={(e) => <Actor entry={e} />}
                    status={(e) => (
                      <Badge className={actionTone(e.action)} variant="secondary">
                        {e.actionLabel}
                      </Badge>
                    )}
                    fields={(e) => [
                      { label: "Quand", value: formatDateTime(e.occurredAt) },
                      { label: "Détail", value: e.changedFields },
                      { label: "Dossier", value: e.entityId },
                    ]}
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
                    label={["entrée", "entrées"]}
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
