"use client"

import { useCallback, useEffect, useState } from "react"
import { format, parseISO } from "date-fns"
import { fr } from "date-fns/locale"
import { ChevronDown, Sparkles } from "lucide-react"

import { newsApi, type NewsEntryDto, type NewsFeedDto } from "@/lib/api/news"
import { clearNewsBadge } from "@/lib/hooks/use-news-badge"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"

/** Cards shown before « Nouveautés plus anciennes »; the index on the side always lists every one. */
const PAGE_SIZE = 10

type FeedState =
  | { kind: "loading" }
  | { kind: "failed"; message: string }
  | { kind: "absent" }
  | { kind: "ready"; feed: NewsFeedDto }

const day = (sentOn: string) => parseISO(sentOn)
const longDate = (sentOn: string) => format(day(sentOn), "d MMMM yyyy", { locale: fr })
const shortDate = (sentOn: string) => format(day(sentOn), "d MMM", { locale: fr })
const monthLabel = (sentOn: string) => {
  const label = format(day(sentOn), "MMMM yyyy", { locale: fr })
  return label.charAt(0).toUpperCase() + label.slice(1)
}
const anchorOf = (id: string) => `nouveaute-${id}`

/** « Nouveautés » — what the vendor e-mailed the cabinet, kept as a page. Opening it is what marks it read. */
export function NewsFeed() {
  const [state, setState] = useState<FeedState>({ kind: "loading" })
  const [visible, setVisible] = useState(PAGE_SIZE)
  const [pendingScroll, setPendingScroll] = useState<string | null>(null)

  const load = useCallback(async () => {
    setState({ kind: "loading" })
    try {
      const feed = await newsApi.feed()
      if (!feed) {
        setState({ kind: "absent" })
        return
      }
      setState({ kind: "ready", feed })
      // The cards keep their « Nouveau » for this visit; only the badge drops. A failed mark costs a badge, nothing more.
      if (feed.unread > 0) {
        newsApi.markRead().then(clearNewsBadge, () => undefined)
      }
    } catch (err) {
      setState({ kind: "failed", message: getErrorMessage(err, "Impossible de charger les nouveautés.") })
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  useEffect(() => {
    if (!pendingScroll) return
    document.getElementById(anchorOf(pendingScroll))?.scrollIntoView({ behavior: "smooth", block: "start" })
    setPendingScroll(null)
  }, [pendingScroll, visible])

  const unread = state.kind === "ready" ? state.feed.unread : 0

  const header = (
    <PageHeader
      title="Nouveautés"
      titleBadge={
        unread > 0 ? (
          <Badge variant="secondary" className="tabular-nums">
            {unread} non lue{unread > 1 ? "s" : ""}
          </Badge>
        ) : undefined
      }
    />
  )

  if (state.kind === "loading") {
    return (
      <>
        {header}
        <div className="space-y-6" aria-busy="true" aria-label="Chargement des nouveautés">
          {[0, 1].map((i) => (
            <div key={i} className="h-48 animate-pulse rounded-xl border bg-muted/40 md:ms-28" />
          ))}
        </div>
      </>
    )
  }

  if (state.kind === "failed") {
    return (
      <>
        {header}
        <LoadFailureNotice message={state.message} onRetry={() => void load()} />
      </>
    )
  }

  if (state.kind === "absent") {
    return (
      <>
        {header}
        <EmptyState icon={Sparkles} title="Les nouveautés ne sont pas proposées sur cette installation" />
      </>
    )
  }

  const entries = state.feed.entries
  if (entries.length === 0) {
    return (
      <>
        {header}
        <EmptyState
          icon={Sparkles}
          title="Aucune nouveauté pour l'instant"
          description="Chaque nouveauté d'Apexa vous arrive par e-mail et reste ici."
        />
      </>
    )
  }

  const reveal = (id: string) => {
    const index = entries.findIndex((e) => e.id === id)
    if (index >= visible) setVisible(Math.ceil((index + 1) / PAGE_SIZE) * PAGE_SIZE)
    setPendingScroll(id)
  }

  return (
    <>
      {header}
      <div className="grid gap-8 xl:grid-cols-[minmax(0,1fr)_17rem] xl:items-start">
        <div className="min-w-0 space-y-6">
          <div className="relative">
            <span
              aria-hidden="true"
              className="pointer-events-none absolute inset-y-6 start-[calc(6rem-1px)] hidden w-0.5 rounded-full bg-border md:block"
            />
            <ol className="space-y-6">
              {entries.slice(0, visible).map((entry) => (
                <NewsTimelineEntry key={entry.id} entry={entry} />
              ))}
            </ol>
          </div>
          {entries.length > visible && (
            <div className="md:ms-28">
              <Button variant="outline" className="bg-card shadow-xs" onClick={() => setVisible((v) => v + PAGE_SIZE)}>
                <ChevronDown aria-hidden="true" />
                Nouveautés plus anciennes
              </Button>
            </div>
          )}
        </div>

        <NewsIndex entries={entries} onPick={reveal} />
      </div>
    </>
  )
}

function NewsTimelineEntry({ entry }: { entry: NewsEntryDto }) {
  const count = entry.items.length
  return (
    <li className="relative md:grid md:grid-cols-[5rem_minmax(0,1fr)] md:gap-x-8">
      <div className="hidden pt-5 text-end md:block">
        <p className="text-sm font-semibold text-foreground">{shortDate(entry.sentOn)}</p>
        <p className="text-xs text-muted-foreground">{format(day(entry.sentOn), "yyyy")}</p>
        <span
          aria-hidden="true"
          className="absolute start-[calc(6rem-0.625rem)] top-[1.4rem] hidden rounded-full bg-background p-1 md:block"
        >
          <span
            className={cn(
              "block size-3 rounded-full",
              entry.isUnread ? "bg-primary ring-4 ring-primary/15" : "border-2 border-muted-foreground/40 bg-background",
            )}
          />
        </span>
      </div>

      <article
        id={anchorOf(entry.id)}
        aria-labelledby={`${anchorOf(entry.id)}-titre`}
        className={cn(
          "min-w-0 scroll-mt-6 overflow-hidden rounded-xl border bg-card",
          entry.isUnread ? "shadow-md" : "shadow-xs",
        )}
      >
        <header
          className={cn(
            "flex flex-col gap-2 px-4 pt-4 sm:px-6 sm:pt-5",
            entry.isUnread ? "border-b bg-muted/40 pb-4" : "pb-1",
          )}
        >
          <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
            {entry.isUnread && (
              <span className="rounded-md bg-primary px-2 py-0.5 text-2xs font-bold uppercase tracking-wide text-primary-foreground">
                Nouveau
              </span>
            )}
            <time dateTime={entry.sentOn} className="text-sm text-muted-foreground md:sr-only">
              {longDate(entry.sentOn)}
            </time>
            <span className="hidden text-sm text-muted-foreground md:inline">
              {count} changement{count > 1 ? "s" : ""}
            </span>
          </div>
          <h2
            id={`${anchorOf(entry.id)}-titre`}
            className={cn(
              "font-semibold leading-snug tracking-tight text-foreground",
              entry.isUnread ? "text-lg sm:text-xl" : "text-base sm:text-lg",
            )}
          >
            {entry.subject}
          </h2>
        </header>
        <ul className="divide-y px-4 pb-1 sm:px-6">
          {entry.items.map((item, index) => (
            <li key={index} className="flex items-start gap-3 py-3 sm:gap-3.5">
              <span
                aria-hidden="true"
                className="flex size-9 shrink-0 items-center justify-center rounded-lg border bg-muted/50 text-lg"
              >
                {item.emoji}
              </span>
              <p className="min-w-0 pt-1.5 text-sm leading-relaxed text-foreground/90 sm:text-base">{item.text}</p>
            </li>
          ))}
        </ul>
      </article>
    </li>
  )
}

/** Every news by month, for reaching an old one without paging. Desktop only: below `xl:` the list itself is the index. */
function NewsIndex({ entries, onPick }: { entries: NewsEntryDto[]; onPick: (id: string) => void }) {
  const months: { label: string; entries: NewsEntryDto[] }[] = []
  for (const entry of entries) {
    const label = monthLabel(entry.sentOn)
    const last = months[months.length - 1]
    if (last?.label === label) last.entries.push(entry)
    else months.push({ label, entries: [entry] })
  }

  return (
    <nav
      aria-label="Toutes les nouveautés"
      className="hidden rounded-xl border bg-card p-2 shadow-sm xl:sticky xl:top-4 xl:block"
    >
      <h2 className="px-3 pb-2 pt-2 text-2xs font-semibold uppercase tracking-[0.08em] text-muted-foreground">
        Toutes les nouveautés
      </h2>
      <div className="max-h-[70dvh] space-y-3 overflow-y-auto">
        {months.map((month) => (
          <div key={month.label}>
            <p className="px-3 pb-1 text-xs font-semibold text-foreground/80">{month.label}</p>
            <ul>
              {month.entries.map((entry) => (
                <li key={entry.id}>
                  <button
                    type="button"
                    onClick={() => onPick(entry.id)}
                    className={cn(
                      "flex w-full items-start gap-2.5 rounded-lg px-3 py-2 text-start text-sm leading-snug transition-colors hover:bg-muted",
                      "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                      entry.isUnread ? "font-semibold text-foreground" : "text-muted-foreground",
                    )}
                  >
                    <span
                      aria-hidden="true"
                      className={cn(
                        "mt-1.5 size-1.5 shrink-0 rounded-full",
                        entry.isUnread ? "bg-primary" : "border border-muted-foreground/50",
                      )}
                    />
                    <span className="min-w-0">
                      {entry.subject}
                      {entry.isUnread && <span className="sr-only"> (non lue)</span>}
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </div>
    </nav>
  )
}
