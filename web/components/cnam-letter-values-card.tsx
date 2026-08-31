"use client"

import { Fragment, useState, useEffect } from "react"
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Label } from "@/components/ui/label"
import { CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { Coins } from "lucide-react"
import { dentalActsApi } from "@/lib/api/dental-acts"
import type { CnamLetterValueDto } from "@/lib/api/types"
import { ApiError } from "@/lib/api/client"
import { formatDT, parseAmountInput, quoteFr } from "@/lib/format"
import { toast } from "sonner"

/**
 * « La convention en vigueur fixe cette valeur à N » — offered, never applied behind the admin's back.
 *
 * <p>The server corrects a row only while it is **untouched since seeding**; a value this clinic's admin has
 * edited is deliberately left alone (clobbering a deliberate entry is worse than a stale default). That decision
 * is only defensible if the divergence is *visible somewhere*, and this is the somewhere. One component rendered
 * from both trees, so the phone stack and the desktop table cannot drift apart on the wording or on which rows
 * qualify — the reason the mobile pass split this card in two in the first place.</p>
 *
 * <p>Renders nothing when the convention settles no value for this lettre clé (`Vd`/`Rd` — a null is « we do not
 * know », and inventing a figure would be the same class of defect as the stale one).</p>
 */
function ConventionPrompt({
  value,
  applying,
  onApply,
}: {
  value: CnamLetterValueDto
  applying: boolean
  onApply: () => void
}) {
  if (value.conventionValue == null) {
    return null
  }
  // Millime tolerance: both figures are decimals on the wire, so `30` must not read as different from `30.000`.
  if (Math.abs(value.value - value.conventionValue) < 0.0005) {
    return null
  }

  return (
    <div role="status" className="space-y-2 rounded-md border border-warning/40 bg-warning-wash p-2">
      <p className="text-xs text-warning-ink">
        La convention en vigueur fixe «&nbsp;{value.lettreCle}&nbsp;» à{" "}
        <span className="font-semibold">{formatDT(value.conventionValue)}</span>. Valeur enregistrée&nbsp;:{" "}
        {formatDT(value.value)}.
      </p>
      <Button
        size="sm"
        variant="outline"
        className="w-full sm:w-auto"
        disabled={applying}
        onClick={onApply}
      >
        {applying ? "Application…" : "Appliquer la valeur conventionnelle"}
      </Button>
    </div>
  )
}

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
  // Bumped by « Réessayer » so a failed load is recoverable without a browser reload.
  const [retryToken, setRetryToken] = useState(0)

  // Refetch in place on mount and whenever the parent bumps reloadToken; the `active` guard prevents a
  // setState after unmount if torn down mid-request.
  useEffect(() => {
    let active = true
    const run = async () => {
      try {
        setLoading(true)
        setError(null)
        const data = await dentalActsApi.listLetterValues()
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
  }, [reloadToken, retryToken])

  /**
   * The row's version as the server holds it *now*. Falls back to the rendered row's own if the re-read fails —
   * a stale token still means a 409 the user can act on, whereas refusing to save on a transient network blip
   * would be a worse answer than the problem.
   */
  const currentVersion = async (v: CnamLetterValueDto): Promise<number> => {
    try {
      const rows = await dentalActsApi.listLetterValues()
      return rows.find((r) => r.id === v.id)?.version ?? v.version
    } catch {
      return v.version
    }
  }

  const save = async (v: CnamLetterValueDto) => {
    const parsed = parseAmountInput(drafts[v.id] ?? "")
    if (!Number.isFinite(parsed) || parsed < 0) {
      toast.error("La valeur doit être un nombre positif.")
      return
    }
    try {
      setSavingId(v.id)
      // Band B — a per-row action re-reads the row's version immediately before writing. The rendered row is as
      // old as the last refetch, and this value prices every reimbursement estimate in the product.
      await dentalActsApi.updateLetterValue(v.id, parsed, await currentVersion(v))
      toast.success(`Valeur de ${quoteFr(v.lettreCle)} mise à jour.`)
      onChanged() // parent bumps reloadToken → in-place refetch, no remount / no lost sibling draft
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la mise à jour.")
    } finally {
      setSavingId(null)
    }
  }

  // Accepting the convention's figure is an ordinary edit of this clinic's own value — the same endpoint the field
  // above uses, deliberately: there is no second « apply the convention » write path that could diverge from it,
  // and the row ends up stamped as touched, exactly as if the admin had typed the number themselves.
  const applyConvention = async (v: CnamLetterValueDto) => {
    if (v.conventionValue == null) {
      return
    }
    try {
      setSavingId(v.id)
      await dentalActsApi.updateLetterValue(v.id, v.conventionValue, await currentVersion(v))
      toast.success(`Valeur conventionnelle appliquée à ${quoteFr(v.lettreCle)}.`)
      onChanged()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de la mise à jour.")
    } finally {
      setSavingId(null)
    }
  }

  // The provenance + revision cadence, once for the card rather than per row. Read off the first row the
  // convention settles a value for: the three fields are null together, so a row with a source always has both.
  const convention = values.find((v) => v.conventionSource != null)

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
        {/* The shared primitive on the theme's destructive family, plus a retry — this was a hand-written
            `border-red-200 bg-red-50 … dark:` copy whose only remedy was a browser reload. */}
        <FormErrorBanner
          className="mb-4"
          message={error}
          action={{ label: "Réessayer", onClick: () => setRetryToken((t) => t + 1) }}
        />
        {loading ? (
          <p role="status" className="text-center text-muted-foreground">
            Chargement des valeurs…
          </p>
        ) : values.length === 0 ? (
          <EmptyState
            icon={Coins}
            size="compact"
            title="Aucune valeur de lettre clé"
            description="Sans valeur en dinars par lettre clé, aucun remboursement CNAM ne peut être estimé. Les valeurs sont créées avec le catalogue du cabinet."
          />
        ) : (
          <>
            {/*
              ⚠️ Below `md:` this is a **stacked form, not a table** (finding: the save button was the last of
              four columns). At 390px the row needed ~465px, so an admin could type a new valeur de la lettre
              clé and never reach the « Enregistrer » that persists it — with nothing on screen saying a column
              existed off to the right.

              Not a `<CardList>`: this is a form. Every row's middle cell is an editable `<Input>` with its own
              per-row save, so there is no value to *read* and no row identity to make a title of. The stack
              gives each lettre clé a real label, a full-width field and a full-width button.
            */}
            <ul className={`${CARDS_ONLY} divide-y rounded-md border`}>
              {values.map((v) => {
                const dirty = (drafts[v.id] ?? "") !== String(v.value)
                return (
                  <li key={v.id} className="space-y-2 p-3">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <Label htmlFor={`vlc-${v.id}`} className="flex items-center gap-2">
                        <Badge variant="outline">{v.lettreCle}</Badge>
                        <span className="text-muted-foreground">Valeur (TND)</span>
                      </Label>
                      {v.isProvisional && (
                        <Badge variant="outline" className="border-warning/50 text-warning-ink">
                          À vérifier
                        </Badge>
                      )}
                    </div>
                    {/* `text` + `inputMode="decimal"`, never `type="number"` (J8). The `.replace(",", ".")` this
                    field's handler already carried was **dead code**: a number input never yields a comma, it
                    returns an EMPTY value for the rejected keystroke. Parsing now goes through the shared
                    `parseAmountInput`. */}
                    <Input
                      id={`vlc-${v.id}`}
                      type="text"
                      inputMode="decimal"
                      value={drafts[v.id] ?? ""}
                      onChange={(e) => setDrafts((d) => ({ ...d, [v.id]: e.target.value }))}
                    />
                    <Button
                      variant="outline"
                      className="w-full"
                      disabled={!dirty || savingId === v.id}
                      onClick={() => save(v)}
                    >
                      {savingId === v.id ? "Enregistrement…" : "Enregistrer"}
                    </Button>
                    <ConventionPrompt
                      value={v}
                      applying={savingId === v.id}
                      onApply={() => applyConvention(v)}
                    />
                  </li>
                )
              })}
            </ul>
            {/* `containerClassName={TABLE_ONLY}` — the table must be ABSENT below `md:`, not merely narrow, or
                its own scroll container survives beside the stack. The outer `overflow-x-auto` that used to
                wrap this is gone too: `ui/table.tsx` already provides one, so it was a scroller inside a
                scroller. */}
            <Table containerClassName={TABLE_ONLY}>
              <TableHeader>
                <TableRow>
                  <TableHead>Lettre clé</TableHead>
                  <TableHead>Valeur (TND)</TableHead>
                  <TableHead>Statut</TableHead>
                  <TableHead className="text-right">Action</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {values.map((v) => {
                  const dirty = (drafts[v.id] ?? "") !== String(v.value)
                  return (
                    // The prompt is its own full-width row beneath the value's, not a fourth thing crammed into
                    // the « Statut » cell: it carries a sentence and a button, and this table is already four
                    // columns wide on a 820px tablet portrait.
                    <Fragment key={v.id}>
                    <TableRow>
                      <TableCell>
                        <Badge variant="outline">{v.lettreCle}</Badge>
                      </TableCell>
                      <TableCell>
                        {/* Same conversion as the card list above (J8). */}
                        <Input
                          type="text"
                          inputMode="decimal"
                          aria-label={`Valeur (TND) pour ${v.lettreCle}`}
                          value={drafts[v.id] ?? ""}
                          onChange={(e) => setDrafts((d) => ({ ...d, [v.id]: e.target.value }))}
                          className="max-w-32"
                        />
                      </TableCell>
                      <TableCell>
                        {v.isProvisional && (
                          <Badge variant="outline" className="border-warning/50 text-warning-ink">
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
                    {v.conventionValue != null && Math.abs(v.value - v.conventionValue) >= 0.0005 && (
                      <TableRow>
                        <TableCell colSpan={4} className="pt-0">
                          <ConventionPrompt
                            value={v}
                            applying={savingId === v.id}
                            onApply={() => applyConvention(v)}
                          />
                        </TableCell>
                      </TableRow>
                    )}
                    </Fragment>
                  )
                })}
              </TableBody>
            </Table>
          </>
        )}
        {/*
          The revision cadence is the point of this footnote, not the citation. The shipped defect was not that a
          number moved — the convention revises the lettres clés every three years, so one always will — it was
          that nothing on any screen suggested one ever would, so a stale value looked like a settled one for
          years. Rendered from the server's own constants so the sentence cannot drift from the table it describes.
        */}
        {!loading && convention && (
          <p className="mt-4 text-xs text-muted-foreground">
            Source&nbsp;: {convention.conventionSource}. La convention révise ces valeurs tous les{" "}
            {convention.conventionRevisionIntervalYears}&nbsp;ans (indexées sur le SMIG/l&apos;IPC)&nbsp;:
            vérifiez-les à chaque révision. Les lettres clés que la convention ne fixe pas restent «&nbsp;à
            vérifier&nbsp;».
          </p>
        )}
      </CardContent>
    </Card>
  )
}
