import { HeartPulse, Pencil } from "lucide-react"

import { cn } from "@/lib/utils"

/**
 * The ceiling the card's body scrolls past — the same technique and the same figure as `PatientNotesStrip`'s
 * `PANEL_BODY_MAX_PX`, one band below.
 *
 * <p>⚠️ <b>Without it this block has no growth control, and it is the first thing on the page.</b> Allergies,
 * maladies and médicaments are free text: a patient on six drugs with three conditions would push the
 * odontogramme — the chart the whole consultation is read off — below the fold, which is the exact defect the
 * notes strip was capped for and the reason the treatment band was moved *under* the chart.</p>
 *
 * <p>⚠️ Bounded and <b>scrolling</b>, never collapsible: a « voir plus » on the one block a practitioner checks
 * before injecting is a click between them and an allergy.</p>
 *
 * <p>⚠️ <b>192, not the notes strip's 120</b>, and every step of that figure was measured rather than chosen.
 * The values became bulleted <i>lists</i> rather than comma runs, so a cell is now as tall as the list inside it:
 * a real patient (1 allergy · 2 maladies · 3 médicaments · tabac) lays out 2 × 2 at 820 px and 1180 px and comes
 * to <b>183 px</b> of content. At 120 and again at 160 that ordinary tablet case scrolled by a couple of dozen
 * pixels — which reads as a clipped card rather than as a deliberate bound, on the device this product is used
 * on most. 192 clears it.</p>
 *
 * <p>⚠️ <b>It still bites where it matters.</b> The same patient at 390 px is one column and 301 px of content,
 * so a phone scrolls — which is the whole point: the odontogramme must stay reachable on the first screen.</p>
 */
const BODY_MAX_PX = 192

/**
 * « Santé » — the four facts a practitioner checks before touching the patient, as one full-width card.
 *
 * <p>⚠️ <b>This block has been rebuilt several times and every earlier shape is the reason for this one.</b> It
 * began as a single line, « Aucune allergie signalée », which was true and incomplete — maladies, médicaments and
 * tabac were equally unrecorded and it said nothing about any of them, so a blank read as « rien à signaler »
 * when it meant « on n'a rien demandé ». Naming all four turned it into a run of loose lines under an identity
 * strip already carrying five more (« it looks scattered »). Framing those overspent on decoration — uppercase
 * tracked labels at one size, values at another, four ink colours, a border *and* a fill (« too many fonts,
 * colours … extremely unprofessional »). Splitting it into two half-width panels then gave « Motif » a section of
 * its own it did not need, wasted the right half on one line, and tinted the left half a destructive wash that
 * read as alarming rather than as informative.</p>
 *
 * <p>So: <b>one card across the whole line</b>, and the width is spent on the four facts rather than on a second
 * panel. « Motif de consultation » went back under the patient's name, where it belongs — it is the reason this
 * person is on the books, not a health fact.</p>
 *
 * <p>⚠️ <b>The body is the page's own `dl` grammar</b> — `dt` small and muted above `dd`, in a responsive grid —
 * the same shape as « Informations personnelles » and « Informations médicales » at the foot of this page. A
 * fifth way of drawing a label and a value on the one screen that already has two is what made the earlier
 * versions feel foreign; this one is the app's.</p>
 *
 * <p>⚠️ <b>No wash, no tint, no coloured frame.</b> The card is an ordinary `bg-card`. The single accent is the
 * allergy <i>value</i>, in destructive ink, because it is the one of the four that changes what may be injected
 * in the next five minutes — three coloured things side by side and nothing alerts, and a whole panel tinted for
 * a patient who is merely allergic to penicillin reads as an emergency.</p>
 *
 * <p>⚠️ <b>« Tabac » belongs here, not in the identity strip.</b> It sat beside the telephone number for
 * historical reasons (it took the retired assureur's slot), and that was half of why the header read as mixed: a
 * risk factor rendered exactly like a contact detail.</p>
 *
 * <p>⚠️ <b>A cell with no value is not rendered</b> (`frontend-web.md` § 6: an absent field is omitted, never
 * printed as « — »), so a patient with nothing on file costs one line rather than four. Only when all four are
 * empty does the card say so, once, naming all four — the original defect was a sentence that spoke about
 * allergies alone, and every rebuild has kept that fix.</p>
 *
 * <p>⚠️ Deliberately <b>not</b> told whether the read failed. A caller that could not load the patient must not
 * render this at all, or an empty card becomes a claim of « aucune allergie » made on the strength of a network
 * error. On the patient page the patient object is the page's own gate, so reaching here means the read
 * succeeded.</p>
 */
interface HealthFact {
  key: string
  label: string
  /** Already split and de-bulleted by `splitHealthList` — one entry per line the practitioner typed. */
  items: string[]
  /** The one fact allowed to carry ink. */
  alert?: boolean
}

interface PatientHealthCardProps {
  /** Comma-split « Allergies ». */
  allergies: string[]
  /** Comma-split « Maladies » — the column the wire still calls `medicalHistory`. */
  diseases: string[]
  /** Comma-split « Médicaments ». */
  medications: string[]
  /**
   * The tobacco summary sentence, or null when nobody has asked.
   *
   * ⚠️ Every answered status shows, not only a current smoker's: this is the *record*, and « Non-fumeur » is
   * worth reading. The amber in `PatientAlertPanel` is the *warning*, a different job on a different surface.
   */
  tobacco?: string | null
  /**
   * « Antécédents médicaux » — the descriptions alone, already read and ordered by the caller.
   *
   * ⚠️ Descriptions only, not the date or the notes each entry may carry: this card is the glance before the
   * work, and the record card at the foot of the page is where an antécédent is read in full. Cramming a date
   * into a 220 px column turns a list into a paragraph.
   */
  medicalHistory: string[]
  /** « Antécédents familiaux », already composed by the caller as « Père : maladie cardiaque ». */
  familyHistory: string[]
  /** Opens the patient form scrolled to « Informations médicales ». */
  onEdit: () => void
  className?: string
}

export function PatientHealthCard({
  allergies,
  diseases,
  medications,
  tobacco,
  medicalHistory,
  familyHistory,
  onEdit,
  className,
}: PatientHealthCardProps) {
  const facts: HealthFact[] = []
  if (allergies.length > 0) facts.push({ key: "allergies", label: "Allergies", items: allergies, alert: true })
  if (diseases.length > 0) facts.push({ key: "diseases", label: "Maladies", items: diseases })
  if (medications.length > 0) facts.push({ key: "medications", label: "Médicaments", items: medications })
  // ⚠️ Tabac is ONE sentence the app composes (`tobaccoSummary`), never a list the user typed — so it is a
  // single item here rather than being split, or « Fumeur · 20 cigarettes/j » would render as two bullets.
  if (tobacco) facts.push({ key: "tobacco", label: "Tabac", items: [tobacco] })
  // ⚠️ Last, and after tabac: the four above are what the patient IS today, these two are history. A dentist
  // scanning this card left-to-right should meet the live facts before the background ones.
  if (medicalHistory.length > 0) {
    facts.push({ key: "medicalHistory", label: "Antécédents médicaux", items: medicalHistory })
  }
  if (familyHistory.length > 0) {
    facts.push({ key: "familyHistory", label: "Antécédents familiaux", items: familyHistory })
  }

  /*
    ⚠️ **`gap-px` over a `bg-border` container with each cell painting its own `bg-card`** — the
    `dashboard/kpi-grid.tsx` technique, so the gaps show through as hairline dividers. Its documented trap is
    that an *unfilled* cell shows the container's `bg-border` as a grey slab.

    ⚠️ **This used to dodge that by choosing column counts that DIVIDE the fact count, and that stopped working
    at six.** With four facts the steps 1 → 2 → 4 all divide; add « Antécédents médicaux » and « Antécédents
    familiaux » and five is prime — no useful step divides it — so the trick has to give way to `kpi-grid`'s own
    answer: pad the last row with `aria-hidden` fillers.

    ⚠️ **A filler is computed per breakpoint and only rendered at the one that needs it**, which is what stops a
    filler becoming a blank row somewhere else: at one column every cell already fills its row, so a filler there
    would paint an empty white band under the list. `twoUp` shows only between `sm:` and `xl:`, `threeUp` only
    from `xl:`. Checked at every count from 1 to 6, both grids come out exactly full.

    ⚠️ Never `divide-x`: it draws from DOM order, so it mis-aligns the moment a row wraps — the same reason
    `kpi-grid` refuses it.
  */
  const twoUpFillers = facts.length % 2 === 0 ? 0 : 1
  const threeUpFillers = (3 - (facts.length % 3)) % 3

  /*
    ⚠️ **With nothing recorded at all, this is ONE dashed line — the shape `patient-notes-strip.tsx` already
    settled one band below, for the reason it wrote down there.** Framed, the empty state cost 86 px: a header
    row carrying « SANTÉ » and a « Modifier », a border, and the body's own padding, all to deliver a single
    sentence — directly under the patient's name and above the odontogramme, i.e. the most expensive band on
    the page.

    ⚠️ **The sentence itself is unchanged and names all four facts.** That is the whole point of this card and
    the reason it was rebuilt five times: « Aucune allergie » alone reads as « rien à signaler » when what is
    true is « personne n'a posé la question ». Shrinking the frame must not shrink the claim.

    ⚠️ **Only the EMPTY state collapses.** A card with anything in it keeps its frame and its `BODY_MAX_PX`
    scroller — a « voir plus » on the one block a practitioner reads before injecting is a click between them
    and an allergy, and that is still forbidden.

    ⚠️ The caller is responsible for never rendering an empty card off a FAILED read: the page passes
    `HISTORY_UNREADABLE` for that, which is a non-empty fact, so this branch cannot be reached by a network
    error asserting « aucune allergie ».
  */
  if (facts.length === 0) {
    return (
      <section
        /*
          ⚠️ **One row, and `flex-wrap` with a basis was tried and reverted.** Measured: plain, this is 46 px
          from 1180 px up and 58 px at 390 px. Giving the sentence `basis-[14rem]` so it could take a row of its
          own pushed the control onto a second line at 390 px too and made it **90 px** — worse at the width
          that matters most. At 320 px it is 98 px either way, because the sentence genuinely needs three or
          four lines there; that is roughly what the framed card cost, so 320 px is the one width this change
          does not improve. It does not regress it either, which is the bar.
        */
        className={cn(
          "flex min-w-0 items-center gap-2 rounded-lg border border-dashed px-3 py-2",
          className,
        )}
      >
        <HeartPulse className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <h2 className="sr-only">Santé</h2>
        <p className="min-w-0 text-sm text-muted-foreground">
          Aucune allergie, maladie, médicament ni tabac renseigné.
        </p>
        <button
          type="button"
          onClick={onEdit}
          aria-label="Renseigner les informations médicales du patient"
          className="ms-auto inline-flex min-h-7 shrink-0 items-center gap-1.5 rounded-md px-2 text-xs text-muted-foreground underline-offset-2 hover-hover:hover:underline coarse:min-h-11"
        >
          <Pencil className="h-3 w-3" aria-hidden="true" />
          Renseigner
        </button>
      </section>
    )
  }

  return (
    <section className={cn("min-w-0 overflow-hidden rounded-lg border bg-card", className)}>
      {/* `min-h-8` floors the header so the body starts at the same y whether or not anything is recorded. */}
      <div className="flex min-h-8 items-center gap-2 px-3 py-2">
        <HeartPulse className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        <h2 className="text-2xs font-semibold uppercase tracking-wide text-muted-foreground">Santé</h2>
        <button
          type="button"
          onClick={onEdit}
          aria-label="Modifier les informations médicales du patient"
          className="ms-auto inline-flex min-h-7 items-center gap-1.5 rounded-md px-2 text-xs text-muted-foreground underline-offset-2 hover-hover:hover:underline coarse:min-h-11"
        >
          <Pencil className="h-3 w-3" aria-hidden="true" />
          Modifier
        </button>
      </div>

      <div className="min-h-0 overflow-y-auto border-t" style={{ maxHeight: BODY_MAX_PX }}>
        {facts.length === 0 ? (
          <p className="px-3 py-2.5 text-sm text-muted-foreground">
            Aucune allergie, maladie, médicament ni tabac renseigné.
          </p>
        ) : (
          <dl
            /*
              ⚠️ Values sit UNDER their labels rather than beside them, which is this page's own `dl` grammar
              (« Informations personnelles » and « Informations médicales » at its foot). Beside them, a
              free-text médicaments value wraps under the label track and stops aligning with anything.
            */
            className="grid gap-px bg-border sm:grid-cols-2 xl:grid-cols-3"
          >
            {facts.map((fact) => (
              <div key={fact.key} className="min-w-0 bg-card px-3 py-2.5">
                <dt className="text-xs font-medium text-muted-foreground">{fact.label}</dt>
                <dd
                  className={cn(
                    "mt-0.5 text-sm [overflow-wrap:anywhere]",
                    fact.alert ? "font-medium text-destructive" : "text-foreground",
                  )}
                >
                  {/*
                    ⚠️ A real `<ul>` past one item, and a bare line at exactly one. A single-item list still
                    announces « liste, 1 élément » to a screen reader and pays a marker's indent for nothing —
                    and « Tabac » is always one item, so the common card would otherwise carry a lone bullet
                    beside three real lists.

                    ⚠️ `list-none` with a typographic « • » in a `::marker`-free span rather than
                    `list-disc list-inside`: the built-in marker sits outside the text box, so a wrapped second
                    line hangs under the bullet instead of aligning with the first character. The explicit span
                    plus `ps-3 -indent-3` is the hanging indent this needs at 200 px of column width.
                  */}
                  {fact.items.length === 1 ? (
                    fact.items[0]
                  ) : (
                    <ul className="list-none space-y-0.5">
                      {fact.items.map((item, index) => (
                        <li key={index} className="-indent-3 ps-3">
                          <span aria-hidden="true" className="me-1.5 text-muted-foreground">
                            •
                          </span>
                          {item}
                        </li>
                      ))}
                    </ul>
                  )}
                </dd>
              </div>
            ))}

            {/* See the note above: visible only at the width whose grid they complete. */}
            {Array.from({ length: twoUpFillers }, (_, i) => (
              <div key={`f2-${i}`} aria-hidden="true" className="hidden bg-card sm:block xl:hidden" />
            ))}
            {Array.from({ length: threeUpFillers }, (_, i) => (
              <div key={`f3-${i}`} aria-hidden="true" className="hidden bg-card xl:block" />
            ))}
          </dl>
        )}
      </div>
    </section>
  )
}
