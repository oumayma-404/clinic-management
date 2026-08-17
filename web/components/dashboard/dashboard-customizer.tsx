"use client"

import { Check, SlidersHorizontal } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Separator } from "@/components/ui/separator"
import { Switch } from "@/components/ui/switch"
import {
  DASHBOARD_BLOCKS,
  DASHBOARD_BLOCK_KEYS,
  DASHBOARD_FORM_LABELS,
  DASHBOARD_SECTION_KEYS,
  DASHBOARD_SECTION_TITLES,
  blocksInSection,
  type DashboardBlockKey,
} from "@/lib/dashboard-blocks"

interface DashboardCustomizerProps {
  hidden: Set<DashboardBlockKey>
  onToggle: (key: DashboardBlockKey) => void
  onResetToDefaults: () => void
  onShowAll: () => void
  saving?: boolean
  disabled?: boolean
}

/**
 * « Personnaliser » — the panel that decides which blocks this dashboard shows.
 *
 * <p>Rendered from `DASHBOARD_BLOCKS`, the same exhaustive registry the page renders from, so the panel and the
 * page cannot disagree about what exists. A block added without a customiser entry is a `tsc` error in that file,
 * not a figure a user finds they cannot switch off.</p>
 *
 * <p>A Popover rather than a modal: the whole point is to see the effect on the dashboard behind it. Toggling is
 * immediate and per-switch — there is no Save button, because a settings panel with a Save button that you opened
 * to hide one card is a form, and this is a control. The saving state is surfaced as a quiet inline note rather
 * than a spinner that blocks the switches.</p>
 */
export function DashboardCustomizer({
  hidden,
  onToggle,
  onResetToDefaults,
  onShowAll,
  saving = false,
  disabled = false,
}: DashboardCustomizerProps) {
  const visibleCount = DASHBOARD_BLOCK_KEYS.length - hidden.size

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="gap-2" disabled={disabled}>
          <SlidersHorizontal className="h-4 w-4" />
          Personnaliser
          {/* The count is the only signal that the dashboard is not showing everything. Without it a user who hid
              six cards months ago has no way to know why a figure a colleague mentions is not on their screen. */}
          {hidden.size > 0 && (
            <span className="rounded bg-muted px-1.5 py-0.5 text-xs font-medium tabular-nums text-muted-foreground">
              {visibleCount}/{DASHBOARD_BLOCK_KEYS.length}
            </span>
          )}
        </Button>
      </PopoverTrigger>

      <PopoverContent align="end" className="max-h-[70dvh] w-80 overflow-y-auto p-0">
        <div className="space-y-1 p-4 pb-3">
          <p className="text-sm font-semibold">Afficher sur le tableau de bord</p>
          <p className="text-xs text-muted-foreground">
            Vos choix ne concernent que vous et sont conservés sur tous vos appareils.
          </p>
        </div>

        <Separator />

        <div className="space-y-4 p-4">
          {DASHBOARD_SECTION_KEYS.map((section) => {
            const keys = blocksInSection(section)
            if (keys.length === 0) return null

            return (
              <div key={section} className="space-y-3">
                <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {DASHBOARD_SECTION_TITLES[section]}
                </p>
                {keys.map((key) => {
                  const id = `dashboard-block-${key}`
                  const block = DASHBOARD_BLOCKS[key]
                  return (
                    <div key={key} className="flex items-center justify-between gap-3">
                      {/* A real <Label htmlFor> rather than adjacent text: the whole row should be a hit target,
                          and a switch whose label is not associated with it is unusable by a screen reader. */}
                      {/* `flex-col items-start` because `ui/label.tsx` is a flex ROW: without it the form line
                          renders as a second column beside the name and reads as an accidental table. */}
                      <Label
                        htmlFor={id}
                        className="cursor-pointer flex-col items-start gap-0.5 text-sm font-normal leading-snug"
                      >
                        {block.label}
                        {/* What it actually is on the page. Six identically-shaped rows under « La journée » say
                            nothing about the fact that they are chips at the top rather than cards below. */}
                        <span className="text-xs font-normal text-muted-foreground">
                          {DASHBOARD_FORM_LABELS[block.form]}
                        </span>
                      </Label>
                      <Switch
                        id={id}
                        checked={!hidden.has(key)}
                        onCheckedChange={() => onToggle(key)}
                        aria-label={block.label}
                      />
                    </div>
                  )
                })}
              </div>
            )
          })}
        </div>

        <Separator />

        <div className="flex items-center justify-between gap-2 p-3">
          <span className="text-xs text-muted-foreground" role="status" aria-live="polite">
            {saving ? (
              "Enregistrement…"
            ) : (
              <span className="inline-flex items-center gap-1">
                <Check className="h-3 w-3" aria-hidden="true" />
                Enregistré
              </span>
            )}
          </span>
          <div className="flex gap-1">
            <Button variant="ghost" size="sm" className="h-8 text-xs" onClick={onShowAll}>
              Tout afficher
            </Button>
            <Button variant="ghost" size="sm" className="h-8 text-xs" onClick={onResetToDefaults}>
              Par défaut
            </Button>
          </div>
        </div>
      </PopoverContent>
    </Popover>
  )
}
