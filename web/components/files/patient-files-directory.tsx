"use client"

import { useCallback, useState } from "react"
import { useUrlFilterSeed, useUrlFilters } from "@/lib/hooks/use-url-filters"
import Link from "next/link"

import { PageHeader } from "@/components/ui/page-header"
import { FilterChip, ListToolbar } from "@/components/ui/list-toolbar"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { InitialsAvatar, toneIndexFor } from "@/components/ui/initials-avatar"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { FilePlus2, FolderOpen, SearchX } from "lucide-react"

import { patientFilesApi, type PatientFileDirectorySort } from "@/lib/api/patient-files"
import type { PatientFileSummaryDto } from "@/lib/api/types"
import { usePagedList } from "@/lib/hooks/use-paged-list"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { formatDateFr, formatFileSize, quoteFr } from "@/lib/format"
import { ZONES, zoneChipClass } from "@/lib/zones"
import { cn } from "@/lib/utils"

/**
 * « Fichiers » — the clinic's file drawers, one card per patient.
 *
 * <p>It is a way <b>in</b>, not a second file manager: every card links to the patient's own files page, which
 * owns uploading, folders, renaming, preview and deletion. What this screen adds is the one thing that page
 * cannot show — <i>which</i> patient to open — and the figure that answers it, which until now required opening a
 * record to find out.</p>
 *
 * <h3>Four decisions worth knowing</h3>
 *
 * <p><b>A card grid, not a table with a card fallback.</b> The rule everywhere else is that a
 * <c>&lt;Table&gt;</c> never ships alone (§ 6) — because a table is how you compare rows across columns, and
 * comparison is what a 320 px screen cannot do. There is nothing to compare here: a row is a name and a count,
 * and the gesture is « find this person, open their files ». So the card <i>is</i> the surface at every width,
 * which also means one tree instead of two and no hinge at which the two could drift.</p>
 *
 * <p><b>Every narrowing decision belongs to the server.</b> Search, « Avec fichiers » and the ordering are all
 * request parameters applied before the page is cut. None of them may become a <c>.filter()</c> over
 * <c>items</c>: over a page that means « those of these 25 », which shrinks pages unpredictably and hides every
 * match sitting on another one — this repo's own list-pagination trap, and the reason searching moved into SQL in
 * the first place. It is also why « le plus de fichiers » is a sort the database performs and not a
 * <c>[...items].sort()</c>, which would only ever order the twenty-five rows in hand.</p>
 *
 * <p><b>A failed read is not an empty drawer.</b> « Aucun patient » and « je n'ai pas pu lire » are opposite
 * facts with the same picture, and on a screen whose whole job is to say whether a file exists, guessing wrong is
 * the expensive direction. The <c>error</c> flag stays distinct from <c>items.length === 0</c> and is rendered
 * through <c>LoadFailureNotice</c>.</p>
 *
 * <p><b>The header lives here rather than in the route.</b> Its subtitle quotes the read's own total, so the fact
 * above the grid and the grid itself can never disagree — and there is no count while the first fetch is in
 * flight, because <c>totalCount</c> starts at 0 and « 0 patients » beside a loading skeleton is a real number,
 * confidently wrong, on the screen that answers « avons-nous ce dossier ? ».</p>
 */

/**
 * The pastel per-card palette, indexed by <c>toneIndexFor</c> so a card and the initials disc inside it are
 * always the same hue.
 *
 * <p>⚠️ Complete literal class strings, never composed — Tailwind scans source text, so a `bg-chart-N/25` built
 * at runtime is never generated and renders as no colour at all, which looks like a design choice rather than a
 * bug. Same rule as `lib/zones.ts` and `ui/initials-avatar.tsx`.</p>
 *
 * <p>The hue lands on a corner glow and on the count tile's ground — <b>never on text</b>. Tinted ink measures
 * ~3.3:1 against its own wash, under the 4.5:1 floor this codebase holds, and the fix is not a darker step per
 * hue but the recognition that the tile carries the colour while the figure only needs to be legible: every
 * number and label below is `--foreground` or `--muted-foreground`.</p>
 *
 * <p>It is decorative and deterministic, and it means nothing beyond « different person ». Status keeps its own
 * family (`ui/status-tone.ts`) and place keeps `lib/zones.ts`; a reader who learned that amber meant something
 * here would be learning a falsehood.</p>
 */
const CARD_TONES = [
  { glow: "bg-chart-1/25", tile: "bg-chart-1/20" },
  { glow: "bg-chart-2/25", tile: "bg-chart-2/20" },
  { glow: "bg-chart-3/25", tile: "bg-chart-3/20" },
  { glow: "bg-chart-4/25", tile: "bg-chart-4/20" },
  { glow: "bg-chart-5/25", tile: "bg-chart-5/20" },
] as const

/** The three orderings. `name` is the default, and is what an unset control means. */
const SORT_OPTIONS: ReadonlyArray<{ value: PatientFileDirectorySort; label: string }> = [
  { value: "name", label: "Nom (A → Z)" },
  { value: "files", label: "Le plus de fichiers" },
  { value: "recent", label: "Ajout le plus récent" },
]

/**
 * « Nom Prénom », the order a Tunisian practice files under and the order the patients table already sorts by.
 * Falls back to a named placeholder rather than an empty heading: a nameless row is a data problem worth seeing.
 */
const fullNameOf = (summary: PatientFileSummaryDto) =>
  `${summary.lastName} ${summary.firstName}`.trim() || "Patient sans nom"

export function PatientFilesDirectory() {
  /*
   * ⚠️ Seeded from the query string and mirrored back into it, so F5 keeps the view and a link can be shared.
   *
   * All three narrowings lived in component state alone and the URL said nothing about them, so « les patients
   * avec fichiers, par ajout le plus récent » was a view nobody could return to or send to a colleague — on a
   * screen whose whole purpose is « chez qui est le scanner ? ». An unreadable `sort` falls back to « name »
   * rather than refusing: a stale bookmark shows the default order, never an error about a query parameter.
   */
  const initial = useUrlFilterSeed()
  const [searchQuery, setSearchQuery] = useState(initial.get("search") ?? "")
  const [withFilesOnly, setWithFilesOnly] = useState(initial.get("withFiles") === "1")
  const [sort, setSort] = useState<PatientFileDirectorySort>(() => {
    const stored = initial.get("sort")
    return stored === "files" || stored === "recent" ? stored : "name"
  })
  // Bumped to refetch the current page — by « Réessayer », and by a peer's upload arriving over realtime.
  const [refreshKey, setRefreshKey] = useState(0)

  useUrlFilters({
    search: searchQuery.trim() || undefined,
    withFiles: withFilesOnly ? "1" : undefined,
    sort: sort === "name" ? undefined : sort,
  })

  const fetchPage = useCallback(
    ({ page, pageSize, search }: { page: number; pageSize: number; search?: string }) =>
      patientFilesApi.getPatientSummaries({
        page,
        pageSize,
        search,
        withFilesOnly: withFilesOnly || undefined,
        sort,
      }),
    [withFilesOnly, sort],
  )

  const {
    items: summaries,
    page: pageInfo,
    loading,
    refreshing,
    error,
    setPage,
    setPageSize,
    isSearching,
  } = usePagedList<PatientFileSummaryDto>({
    fetchPage,
    search: searchQuery,
    // Ticking « Avec fichiers » or changing the ordering returns to page 1: page 4 of a 2-page result is an
    // empty grid over data that matched.
    filters: [withFilesOnly, sort],
    refreshKey,
  })

  /*
   * Both keys, and both are load-bearing. `files` fires when anyone uploads, renames or deletes a file, which is
   * the figure on every card; `patients` fires when a patient is created, renamed or archived, which changes who
   * is in the directory at all. Watching one of the two is how the dashboard went stale under a colleague's edit.
   */
  const refresh = () => setRefreshKey((key) => key + 1)
  useClinicRealtime(RealtimeResource.Files, refresh)
  useClinicRealtime(RealtimeResource.Patients, refresh)

  const isFiltered = isSearching || withFilesOnly

  const clearFilters = () => {
    setSearchQuery("")
    setWithFilesOnly(false)
  }

  const total = pageInfo.totalCount
  // Singular at 0 as well as at 1 — « 0 résultat », the convention `ui/data-table-pagination.tsx` already
  // renders (« 0 dépense ») and the one French grammar wants after zéro.
  const plural = total > 1 ? "s" : ""
  // No figure while the first read is in flight, and none at all when it failed — see the class remarks.
  const subtitle =
    loading || error
      ? undefined
      : isSearching
        ? `${total} résultat${plural}`
        : withFilesOnly
          ? `${total} patient${plural} avec des fichiers`
          : `${total} patient${plural}`

  return (
    <div className="space-y-6">
      <PageHeader title="Fichiers" subtitle={subtitle} />

      <ListToolbar
        search={{
          value: searchQuery,
          onChange: setSearchQuery,
          placeholder: "Nom ou téléphone…",
          // ⚠️ Not « Rechercher un patient » — that is already the accessible name of the header's global
          // lookup, which is on screen at the same time. Two controls with one name leaves a screen-reader
          // user choosing between two identical « Rechercher un patient » edit fields that do different
          // things: one navigates to a record, this one narrows the grid below it.
          label: "Rechercher un patient dans les fichiers",
        }}
      >
        <FilterChip
          label="Avec fichiers"
          active={withFilesOnly}
          onToggle={() => setWithFilesOnly((on) => !on)}
        />
        {/*
          The ordering is not a filter — it removes nothing — but it lives in this row because it is the other
          control that decides what the eye meets first, and a second row holding one <Select> would read as a
          section. Named by `aria-label` rather than a visible <Label>: each option text names the axis itself
          (« Le plus de fichiers »), so a « Trier par » caption would spend a line repeating them.
        */}
        <Select value={sort} onValueChange={(value) => setSort(value as PatientFileDirectorySort)}>
          <SelectTrigger aria-label="Trier les patients" className="w-full sm:w-[196px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {SORT_OPTIONS.map((option) => (
              <SelectItem key={option.value} value={option.value}>
                {option.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </ListToolbar>

      {/* Distinct from every empty state below, and never in place of one: it says a read failed, which is a
          claim about us, where « aucun patient » is a claim about the clinic. */}
      {error && (
        <LoadFailureNotice
          message="La liste des dossiers n'a pas pu être chargée."
          detail="Les fichiers d'un patient restent accessibles depuis sa fiche."
          onRetry={refresh}
        />
      )}

      {loading ? (
        <DirectorySkeleton />
      ) : summaries.length === 0 ? (
        // Nothing when the read failed: the notice above already says why the grid is empty, and an
        // « Aucun patient enregistré » under it would contradict it.
        error ? null : (
          <EmptyState
            // `/fichiers` is a « Quotidien » destination (`lib/zones.ts`), so even the nothing-here screen
            // carries the hue the rail is highlighting.
            chipClassName={zoneChipClass(ZONES.daily)}
            icon={isFiltered ? SearchX : FolderOpen}
            title={
              isSearching
                ? `Aucun résultat pour ${quoteFr(searchQuery.trim())}`
                : withFilesOnly
                  ? "Aucun patient n'a encore de fichier"
                  : "Aucun patient enregistré"
            }
            description={
              isFiltered
                ? undefined
                : "Les radiographies, comptes rendus et scans déposés sur une fiche patient apparaissent ici."
            }
            /*
              ⚠️ No « Ajouter un patient » on a filtered-empty grid. The patient may well exist and the search
              simply mistyped — « Ben Salh » finds nothing and the record is under « Ben Salah » — so an add
              button here is an invitation to create the duplicate this product cannot merge afterwards.
            */
            secondaryAction={
              isFiltered ? (
                <button
                  type="button"
                  onClick={clearFilters}
                  className="touch-target text-sm font-medium underline underline-offset-4 hover-hover:hover:no-underline"
                >
                  {isSearching ? "Effacer la recherche" : "Effacer les filtres"}
                </button>
              ) : undefined
            }
          />
        )
      ) : (
        <>
          {/* `refreshing` dims what is on screen instead of blanking it, so a debounced search does not strobe
              the grid between keystrokes. */}
          <ul
            aria-label="Dossiers de fichiers des patients"
            className={cn(
              "grid gap-3 sm:grid-cols-2 xl:grid-cols-3",
              refreshing && "opacity-60 transition-opacity motion-reduce:transition-none",
            )}
          >
            {summaries.map((summary) => (
              <PatientFileCard key={summary.patientId} summary={summary} />
            ))}
          </ul>

          <DataTablePagination
            page={pageInfo}
            onPageChange={setPage}
            onPageSizeChange={setPageSize}
            loading={refreshing}
            label={["patient", "patients"]}
          />
        </>
      )}
    </div>
  )
}

/**
 * One patient's drawer, as a card.
 *
 * <p>The whole card is a single <c>&lt;Link&gt;</c> — one interactive element, not a clickable <c>div</c> with
 * controls inside it — so it is one tab stop, it announces as a link named after the patient, and the tap target
 * is the card rather than the 14 px of text on it.</p>
 *
 * <p>The name <b>wraps</b> and is never truncated: « Mohamed Ali Ben Romdh… » is not a weaker label, it is a
 * different person. The same rule `ui/card-list.tsx` states for its own headings.</p>
 */
function PatientFileCard({ summary }: { summary: PatientFileSummaryDto }) {
  const name = fullNameOf(summary)
  const tone = CARD_TONES[toneIndexFor(name)]
  const hasFiles = summary.fileCount > 0

  return (
    <li>
      <Link
        href={`/patients/${summary.patientId}/files`}
        className={cn(
          "group relative flex h-full flex-col gap-4 overflow-hidden rounded-2xl border bg-card p-4",
          "outline-none transition-shadow duration-150 ease-out motion-reduce:transition-none",
          "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
          "hover-hover:hover:border-primary/40 hover-hover:hover:shadow-md",
        )}
      >
        {/*
          The pastel. A soft corner glow rather than a filled card: at full strength a tinted ground reads as a
          status — « something about this patient is amber » — where a blurred corner reads as decoration and
          leaves the card's own surface plain for the text on it.

          Only for a patient who HAS files. An empty drawer gets a plain card, because the colour is what makes
          the populated ones findable in a page of twenty-five, and spending it on every card spends it on none.

          ⚠️ `overflow-hidden` on the link is what keeps the blur inside the rounded corner — and it is also why
          nothing here uses `.touch-target`, whose pseudo-element is simply clipped inside such a box (§ 2). It
          needs none: the card is already far past 44 px in both directions.
        */}
        {hasFiles && (
          <span
            aria-hidden="true"
            className={cn(
              "pointer-events-none absolute -right-8 -top-10 size-28 rounded-full blur-2xl",
              tone.glow,
            )}
          />
        )}

        <div className="relative flex items-start gap-3">
          <InitialsAvatar name={name} className="size-10 text-xs" />
          <div className="min-w-0 flex-1">
            <p className="font-medium leading-snug text-foreground [overflow-wrap:anywhere]">{name}</p>
            {/* Omitted, not rendered as « — »: the phone is genuinely nullable, and absence is
                self-explanatory where a dash is a value the reader has to decode (§ 6). */}
            {summary.phoneNumber && (
              <p className="mt-0.5 truncate font-mono text-2xs tracking-tight text-muted-foreground">
                {summary.phoneNumber}
              </p>
            )}
          </div>
        </div>

        {/*
          `mt-auto` so the count sits on the card's floor whatever the name did above it — a two-line name must
          not push its neighbour's figure out of alignment across the row.

          ⚠️ `flex-wrap` is measured, not defensive. A card is widest at 1440 px (three columns, ~330 px) and
          NARROWEST at 820 px — tablet portrait, two columns beside a 256 px rail, ~240 px — which is narrower
          than the same card gets on a 390 px phone at one column. Without the wrap, « 4 fichiers » broke over
          two lines against « Dernier ajout » on exactly the device this app is held on most. With it, the date
          takes its own line when the two do not fit and both stay whole.
        */}
        <div className="relative mt-auto flex flex-wrap items-end justify-between gap-x-3 gap-y-2">
          <div className="flex min-w-0 items-center gap-2.5">
            <span
              aria-hidden="true"
              className={cn(
                "flex size-11 shrink-0 items-center justify-center rounded-xl",
                hasFiles ? tone.tile : "bg-muted",
              )}
            >
              {hasFiles ? (
                <span className="font-mono text-base font-semibold tabular-nums leading-none text-foreground">
                  {summary.fileCount}
                </span>
              ) : (
                <FilePlus2 className="size-5 text-muted-foreground" strokeWidth={1.75} />
              )}
            </span>
            <div className="min-w-0">
              {/* The figure is stated once in words, which is why the tile above is `aria-hidden`:
                  « 1 fichier » / « 12 fichiers » agree in number, where a bare count would leave a screen
                  reader saying « 12 » with no noun. */}
              {/* `whitespace-nowrap`: « 4 fichiers » is the card's headline fact and breaking it after the
                  numeral reads as two facts. The row above wraps instead. */}
              <p className="whitespace-nowrap text-sm font-medium text-foreground">
                {hasFiles
                  ? `${summary.fileCount} ${summary.fileCount === 1 ? "fichier" : "fichiers"}`
                  : "Aucun fichier"}
              </p>
              <p className="mt-0.5 truncate text-2xs text-muted-foreground">
                {hasFiles ? formatFileSize(summary.totalBytes) : "Déposer le premier"}
              </p>
            </div>
          </div>

          {/* An absolute date, not « il y a 3 jours »: a clinic reads a record by its date, and a relative
              phrase has to be converted back before it can be compared with anything else on the screen. */}
          {summary.lastUploadedAt && (
            // `ms-auto` keeps it right-aligned on the line it wraps onto: `justify-between` has nothing to push
            // against once it is the only item on its row, so without this the date would jump to the left edge.
            <div className="ms-auto shrink-0 text-end">
              <p className="font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground">
                Dernier ajout
              </p>
              <p className="mt-0.5 text-xs tabular-nums text-foreground">
                {formatDateFr(summary.lastUploadedAt)}
              </p>
            </div>
          )}
        </div>
      </Link>
    </li>
  )
}

/**
 * The loading state, shaped like the grid it becomes.
 *
 * <p>A card grid has no header row, so « chargement », « vide » and « votre filtre est trop étroit » are
 * otherwise the same blank rectangle (§ 13). Placeholders rather than a spinner for the same reason: the shape
 * arriving before the data is what stops the page jumping when it lands.</p>
 */
function DirectorySkeleton() {
  return (
    <div
      role="status"
      aria-label="Chargement des dossiers…"
      aria-busy="true"
      className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3"
    >
      {Array.from({ length: 6 }).map((_, index) => (
        <div key={index} className="space-y-4 rounded-2xl border bg-card p-4">
          <div className="flex items-start gap-3">
            <div className="size-10 shrink-0 animate-pulse rounded-full bg-muted" />
            <div className="flex-1 space-y-2">
              <div className="h-4 w-3/5 animate-pulse rounded bg-muted" />
              <div className="h-3 w-2/5 animate-pulse rounded bg-muted" />
            </div>
          </div>
          <div className="flex items-center gap-2.5">
            <div className="size-11 shrink-0 animate-pulse rounded-xl bg-muted" />
            <div className="flex-1 space-y-2">
              <div className="h-3.5 w-1/3 animate-pulse rounded bg-muted" />
              <div className="h-3 w-1/4 animate-pulse rounded bg-muted" />
            </div>
          </div>
        </div>
      ))}
    </div>
  )
}
