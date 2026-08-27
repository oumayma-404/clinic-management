import { cn } from "@/lib/utils"

/**
 * A person's initials in a tinted disc — the anchor a dense list of names is scanned by.
 *
 * <p>It exists for the patients table, which is the densest surface in the product: nine columns of grey text
 * with nothing for the eye to land on, so finding « Ben Amor » among twenty-five rows is a read rather than a
 * glance. A coloured disc per row turns the first pass into a shape-match.</p>
 *
 * <p><b>Deliberately not `ui/avatar.tsx`.</b> That is the shadcn/Radix primitive and its job is to show an
 * uploaded <i>image</i> with a fallback. This has no image and never will — patients have no photograph in this
 * product — so it is a different component with a different contract, not a variant of that one.</p>
 *
 * <h3>Two decisions worth knowing</h3>
 *
 * <p><b>The hue is decorative and deterministic, never a status.</b> It is derived from the name alone, so the
 * same patient is the same colour on every screen and across every machine, and it means nothing beyond
 * "different person". Status keeps its own family (`ui/status-tone.ts`) and zone keeps its own (`lib/zones.ts`) —
 * a reader who learned that amber meant something here would be learning a falsehood. It draws on
 * `--chart-1…5` because that is the palette this codebase already designates as *categorical* colour that
 * follows the theme, and it carries a tuned dark-mode step for free.</p>
 *
 * <p><b>The initials are `aria-hidden`, and the ink is `--foreground` rather than the hue.</b> Tinted initials
 * would have measured ~3.3:1 against their own wash, under the 4.5:1 floor this codebase holds — and the fix is
 * not a darker step per hue but the recognition that the disc carries the colour while the letters only need to
 * be legible. They are hidden from assistive tech because the full name is always rendered immediately beside
 * them; announcing « B A, Ben Amor Sonia » is noise, not information.</p>
 */

/**
 * The five tints, as complete literal class strings.
 *
 * ⚠️ Same rule as `lib/zones.ts`: Tailwind scans source for literal class names, so a `bg-chart-${n}/20`
 * composed at runtime is never generated and renders as **no colour at all** — a silent failure that looks like
 * a plain grey disc rather than like a bug.
 */
const TONES = [
  "bg-chart-1/20",
  "bg-chart-2/20",
  "bg-chart-3/20",
  "bg-chart-4/20",
  "bg-chart-5/20",
] as const

/**
 * A stable index for a name — `0…4`, the bucket both this disc and anything tinted to match it draw from.
 *
 * <p>A plain character-code sum, on purpose: it must give the same answer in every browser, on every machine and
 * across reloads, since a patient whose colour changed between two page loads would be worse than no colour at
 * all. Distribution does not need to be cryptographic — five buckets over real Tunisian names is even enough
 * that no group of rows reads as one block.</p>
 *
 * <p><b>Exported because a second surface tints more than a disc.</b> The « Fichiers » directory
 * (`components/files/patient-files-directory.tsx`) washes a whole card in the same hue as the disc inside it, and
 * two independent hash functions is how you end up with a violet disc on an amber card. Only the <i>index</i> is
 * shared: each surface still writes its own literal class strings, because Tailwind scans source text and a
 * `bg-chart-N/8` composed at runtime is never generated at all.</p>
 */
export function toneIndexFor(name: string): number {
  let sum = 0
  for (let i = 0; i < name.length; i++) sum += name.charCodeAt(i)
  return sum % TONES.length
}

function toneFor(name: string): string {
  return TONES[toneIndexFor(name)]
}

/**
 * Up to two initials.
 *
 * <p>Falls back to « ? » rather than to an empty disc: a nameless row is a data problem worth seeing, and a blank
 * circle reads as a rendering gap. Composed surnames (« Ben Amor ») yield the first two words' initials, which is
 * what a reader expects.</p>
 */
export function initialsOf(name: string): string {
  const initials = name
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .toUpperCase()
    .slice(0, 2)
  return initials || "?"
}

interface InitialsAvatarProps {
  /** The full name. Drives both the letters and the tint — never pass pre-computed initials. */
  name: string
  /** Size and any extra classes. Defaults to 32 px, which is the patients table's row height minus its padding. */
  className?: string
}

export function InitialsAvatar({ name, className }: InitialsAvatarProps) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "flex size-8 shrink-0 select-none items-center justify-center rounded-full text-2xs font-semibold text-foreground",
        toneFor(name),
        className,
      )}
    >
      {initialsOf(name)}
    </span>
  )
}
