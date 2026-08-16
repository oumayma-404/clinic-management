"use client"

import { cn } from "@/lib/utils"

export interface ModeSegmentedOption<T extends string> {
  value: T
  label: string
}

interface ModeSegmentedProps<T extends string> {
  value: T
  onChange: (value: T) => void
  options: ModeSegmentedOption<T>[]
  /** Names the group for assistive tech — required, since the options alone rarely say what is being chosen. */
  ariaLabel: string
  disabled?: boolean
  /** `sm` is the in-field variant (« Patient existant / Nouveau patient »); `default` is the header one. */
  size?: "default" | "sm"
  className?: string
}

/**
 * A two-or-three-way mutually exclusive choice, shown as one control rather than as several switches.
 *
 * <p>It exists because « Créneau occupé » and « Nouveau patient » shipped as two independent `Switch`es stacked at
 * the right edge of the same card. They are not independent — they describe a single question (*what is being
 * booked, and for whom*) with three answers, only one of which can hold — and two toggles is the one shape that
 * cannot say so. A user reading them had no way to tell that turning the first on made the second meaningless,
 * which the code knew perfectly well: switching « Créneau occupé » on silently cleared the patient, the name
 * fields and every act.</p>
 *
 * <h3>Two decisions worth knowing</h3>
 *
 * <p><b>It is a real `radiogroup`, not a row of toggle buttons.</b> A `role="radio"` set announces « 1 sur 3 » and
 * moves with the arrow keys, which is what makes the exclusivity audible rather than merely visible.</p>
 *
 * <p><b>The touch floor is grown, never overlaid.</b> The options are siblings a few pixels apart, so
 * `.touch-target` would overhang its neighbour and — the later sibling painting last — steal its taps. Hence
 * `min-h-9 coarse:min-h-11` on each option, per `.claude/rules/frontend-web.md` § 2.</p>
 *
 * <p><b>⚠️ The active option is `bg-card`, never `bg-background`</b>, and the track is a full `bg-muted` with a
 * border. This is the agenda view switch's lesson one control over: a dialog paints on `--background` (0.977),
 * so an active pill of the same token is *the surface it sits on* — invisible but for a hairline shadow — and
 * `--muted` (0.964) is 1.3 % away from it, so a `/40` track is no track at all. The result reads as flat words
 * with no control around them, which is exactly what it looked like. Same class string as
 * `appointment-calendar.tsx`'s switch, deliberately: two segmented controls in one product must not disagree
 * about what « selected » looks like.</p>
 */
export function ModeSegmented<T extends string>({
  value,
  onChange,
  options,
  ariaLabel,
  disabled = false,
  size = "default",
  className,
}: ModeSegmentedProps<T>) {
  return (
    <div
      role="radiogroup"
      aria-label={ariaLabel}
      className={cn(
        "inline-flex w-full rounded-lg border bg-muted p-1",
        className,
      )}
    >
      {options.map((option) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            disabled={disabled}
            onClick={() => onChange(option.value)}
            className={cn(
              "flex flex-1 items-center justify-center rounded-md px-3 text-center font-medium transition-colors",
              "min-h-9 coarse:min-h-11",
              size === "sm" ? "text-xs md:text-xs" : "text-sm",
              active
                ? "bg-card font-semibold text-primary shadow-sm ring-1 ring-primary/30 dark:bg-input/70"
                : "text-muted-foreground hover:text-foreground",
              disabled && "pointer-events-none opacity-50",
            )}
          >
            {option.label}
          </button>
        )
      })}
    </div>
  )
}
