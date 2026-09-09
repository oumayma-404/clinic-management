import { CalendarX, FileBarChart, FileText, FlaskConical, Mail, Shield, type LucideIcon } from "lucide-react"

/**
 * The document templates — **one registry, because there were three and they disagreed.**
 *
 * <p>Before this file the set lived in `app/documents/page.tsx`; the patient file's Documents panel offered
 * exactly one of the six (« Nouvelle ordonnance »), and the patient file *also* kept its own four-entry
 * `DOCUMENT_TYPE_LABELS` for rendering saved documents. That third copy is the one that showed: it had no
 * `honoraires` and no `arret-travail`, and `documentTypeLabel` falls back to the raw key, so a saved arrêt de
 * travail was labelled **`arret-travail`** in the patient's own Documents tab. Two of the six could not be
 * created from the patient panel and two of the six could not be *named* by it — the same drift, twice, from
 * the same missing shared list.</p>
 *
 * <p>Consequence worth keeping in mind: a seventh template is added <b>here</b> and appears in the gallery, in
 * the patient panel and in every saved-document label at once. Adding it anywhere else re-creates the bug.</p>
 *
 * <p>`tile` is written as a complete class string per entry rather than composed from `bg-chart-${n}/12`:
 * Tailwind scans source for literal class names, so an interpolated one is never generated and the tile would
 * render with no colour at all — the quiet failure mode of every themed system. (There was also a second
 * field, `color: "text-chart-N"`, that nothing read — the tile carries both the wash and the ink. A duplicated
 * hue nobody renders is the thing that drifts from the one that is rendered.)</p>
 *
 * <p>A module constant, not a fetch: there is no loading state and no empty state to render, because the
 * gallery cannot be empty and cannot fail.</p>
 */
export interface DocumentTemplate {
  /** The route segment — `/documents/{type}` — and the `MedicalDocument.type` key the API stores. */
  type: string
  title: string
  description: string
  icon: LucideIcon
  tile: string
  /**
   * Whether the gallery offers this template. Defaults to true; only `examens` sets it false.
   *
   * <p>⚠️ <b>It is not a soft delete.</b> The type still needs a row here — `documentTypeLabel` is derived from
   * this array, so a type missing from it renders its raw key (« examens ») in the patient's Documents tab.
   * What the flag says is « the fiche de soins is the only writer », which is why the standalone editor has no
   * form for it: a demande d'examens is prescribed at a séance. Flipping this to true is the whole of « let a
   * dentist write one without recording a fiche », and it needs the editor's form and preview first.</p>
   */
  creatable?: boolean
}

export const DOCUMENT_TEMPLATES: readonly DocumentTemplate[] = [
  {
    type: "prescription",
    title: "Ordonnance",
    description: "Prescription médicale pour traitement dentaire et médicaments",
    icon: FileText,
    tile: "bg-chart-1/12 text-chart-1",
  },
  {
    // ⚠️ A SECOND ordonnance, not a variant of the first. A médicament and an examen may not share a sheet —
    // « le médecin formule sur des ordonnances distinctes les prescriptions de médicaments … et les examens de
    // laboratoire » — because the two go to different places (la pharmacie, le laboratoire, le centre
    // d'imagerie), an examen prescription is single-use so one sheet cannot serve two of them, and CNAM claims
    // per line against the matching prescription. The printed title is « ORDONNANCE » like its sibling's; this
    // title is what tells them apart in the app. See DocumentTypes.Examens.
    type: "examens",
    title: "Demande d'examens",
    description: "Prescription d'examens : radiographie, bilan biologique, avis d'un confrère",
    icon: FlaskConical,
    tile: "bg-chart-2/12 text-chart-2",
    creatable: false,
  },
  {
    type: "liaison",
    title: "Lettre de liaison",
    description: "Courrier médical de liaison vers un confrère ou spécialiste",
    icon: Mail,
    tile: "bg-chart-5/12 text-chart-5",
  },
  {
    type: "honoraires",
    title: "Note d'honoraires",
    description: "Facture détaillée des soins et traitements dentaires",
    icon: FileBarChart,
    tile: "bg-chart-4/12 text-chart-4",
  },
  {
    type: "certificat",
    // ⚠️ It no longer claims to cover an arrêt de travail (L11). It never could: a free-text certificat is not
    // the CNAM P 061 form and the caisse refuses it, so the description was pointing dentists at the one
    // template guaranteed not to work for that.
    title: "Certificat médical",
    description: "Certificat de soins, aptitude ou justificatif médical libre",
    icon: Shield,
    tile: "bg-chart-3/12 text-chart-3",
  },
  {
    type: "arret-travail",
    title: "Arrêt de travail",
    description: "Certificat médical d'arrêt de travail sur le formulaire officiel CNAM P 061",
    icon: CalendarX,
    tile: "bg-chart-3/12 text-chart-3",
  },
  {
    type: "bulletin-cnam",
    title: "Bulletin de soins CNAM",
    description: "Bulletin de remboursement des frais de soins (BS1) à déposer à la CNAM",
    icon: FileText,
    tile: "bg-chart-2/12 text-chart-2",
  },
]

/**
 * The French label for a saved document's stored type key.
 *
 * <p>Derived from {@link DOCUMENT_TEMPLATES} rather than a second map, which is the whole point of this file.
 * The raw-key fallback is kept deliberately: a document saved under a type this build does not know about
 * still renders *something* rather than an empty cell.</p>
 */
export const documentTypeLabel = (type: string): string =>
  DOCUMENT_TEMPLATES.find((template) => template.type === type)?.title ?? type

/**
 * The templates a user may start from scratch — every surface that offers « nouveau document » reads this,
 * never {@link DOCUMENT_TEMPLATES} directly.
 *
 * <p>⚠️ <b>`honoraires` is deliberately still here.</b> The type is retired and both server paths reject it,
 * but its tile is a <i>signpost</i>: `app/documents/[type]/page.tsx` intercepts it and says the honoraires
 * live in the Factures module. Dropping the tile would leave whoever is looking for one with nothing to click
 * and nowhere to be told. What this list removes is `examens`, and for a different reason — it is a live
 * document that the <b>fiche de soins alone</b> writes, so there is no blank form to start from.</p>
 */
export const CREATABLE_DOCUMENT_TEMPLATES: readonly DocumentTemplate[] = DOCUMENT_TEMPLATES.filter(
  (template) => template.creatable !== false,
)

/**
 * The demande d'examens' opening formula — the browser's half of `ExamenContent.IntroSingular` /
 * `IntroPlural`.
 *
 * <p>⚠️ <b>Byte-identical to the server's by contract</b>, and `check:responsive`'s
 * `examens-intro-has-one-owner` compares the two in both directions. A sheet whose printed sentence differs
 * from the one the app showed is the same drift the médicament line already has three guards against — and
 * here it would be worse, because this sentence is the only thing on the page that says what is being
 * prescribed.</p>
 */
export const EXAMENS_INTRO_SINGULAR = "Prière de bien vouloir faire pratiquer l'examen suivant :"
export const EXAMENS_INTRO_PLURAL = "Prière de bien vouloir faire pratiquer les examens suivants :"

/** Singular below two, exactly as the renderer decides it. */
export const examensIntro = (count: number): string =>
  count === 1 ? EXAMENS_INTRO_SINGULAR : EXAMENS_INTRO_PLURAL

/*
 * ── The ordonnance's lines ────────────────────────────────────────────────────────────────────────────────
 *
 * Hoisted out of `document-editor-content.tsx`, where the type and the formatter were module-private inside a
 * ~4 000-line component. Two surfaces now write the same document — that editor and the fiche de soins'
 * « Prescription » section — and the formatting docstring below already warned that this rendering had once
 * existed in three copies. A fourth, in the fiche, is exactly what this move prevents.
 *
 * ⚠️ The server halves are `PrescriptionLineKinds` (the kind vocabulary), `PrescriptionLines` (the JSON, and
 * the short row label) and `PrescriptionContent.FormatLine` (the printed sentence). The property names here
 * are the wire contract: the C# reader is case-insensitive but this file's readers are not, so renaming one
 * silently empties a posology rather than failing.
 */

/**
 * What a line of an ordonnance IS. `examen` is a bilan, a radio, or anything else the practitioner sends the
 * patient for — one free-text field, printed verbatim as its own line.
 *
 * ⚠️ An absent kind is a **médicament**: every line written before this key existed is a drug. Read it through
 * {@link prescriptionKind}, never with a bare `=== "examen"` on a possibly-undefined value.
 */
export const PRESCRIPTION_KINDS = { medicament: "medicament", examen: "examen" } as const

export type PrescriptionKind = (typeof PRESCRIPTION_KINDS)[keyof typeof PRESCRIPTION_KINDS]

/** The kind a stored line actually has — mirrors `PrescriptionLineKinds.Normalize`. */
export const prescriptionKind = (kind: string | null | undefined): PrescriptionKind =>
  kind?.trim().toLowerCase() === PRESCRIPTION_KINDS.examen
    ? PRESCRIPTION_KINDS.examen
    : PRESCRIPTION_KINDS.medicament

export const isExamenLine = (kind: string | null | undefined): boolean =>
  prescriptionKind(kind) === PRESCRIPTION_KINDS.examen

/**
 * One prescribed line. `medicationId` + `dci` are set when the line is picked from the catalog (dci is a
 * snapshot of the drug's molecules at selection time); both are absent for a free-text entry, which is a
 * first-class case and not a fallback.
 */
export type PrescriptionLine = {
  /** `medicament` | `examen`. Absent reads as a médicament — see {@link prescriptionKind}. */
  kind?: string
  name: string
  dosage: string
  timesPerDay: string
  /** Voie d'administration — « par voie orale », « en application locale »… Free text: the norms name no closed list. */
  route?: string
  /** Quantité à délivrer (boîtes / unités) — what makes the line dispensable. */
  quantity?: string
  duration: string
  medicationId?: string
  dci?: string[]
}

/** @deprecated The old name, kept so the document editor's existing call sites read unchanged. */
export type MedicationLine = PrescriptionLine

/**
 * The one client-side rendering of a prescribed line, shared by the read-only preview and the Word export.
 *
 * ⚠️ Must stay identical to the server's `PrescriptionContent.FormatLine`, which renders the PDF — the two are
 * the same ordonnance seen twice. It exists because the preview and the Word export each carried their own copy
 * of this formatting, so adding the voie and the quantité would have made three implementations of what a
 * prescription line says.
 *
 * ⚠️ An **examen** needs no branch here and must not get one: its line carries only a `name`, so every clause
 * below is skipped and the text comes out verbatim — which is precisely why the fiche's examens print
 * correctly without touching any of the three formatters.
 */
export const formatPrescriptionLine = (med: PrescriptionLine): string => {
  let text = med.name?.trim() || "Médicament"
  if (med.dosage?.trim()) text += ` ${med.dosage.trim()}`
  if (med.timesPerDay?.trim()) text += `, ${med.timesPerDay.trim()}x par jour`
  if (med.route?.trim()) text += `, ${med.route.trim()}`
  if (med.duration?.trim()) {
    const days = Number.parseInt(med.duration, 10)
    text += ` pendant ${med.duration.trim()} jour${days > 1 ? "s" : ""}`
  }
  if (med.quantity?.trim()) text += ` — quantité : ${med.quantity.trim()}`
  const dci = (med.dci ?? []).map((d) => d?.trim()).filter(Boolean).join(", ")
  if (dci) text += ` (DCI : ${dci})`
  return text
}

/**
 * The renewal mention, mirroring the server's `PrescriptionContent.RenewalMention`. Blank ⇒ the ordonnance is
 * silent on renewal (the default); "0"/"non" ⇒ explicitly non-renewable; anything else ⇒ a count.
 */
export const formatRenewalMention = (renewals: string | null | undefined): string | null => {
  const value = renewals?.trim()
  if (!value) return null
  if (value === "0" || value.toLowerCase() === "non") return "Ordonnance non renouvelable."
  return `Ordonnance à renouveler ${value} fois.`
}

/**
 * The five-or-six-word label a list row shows — « Augmentin Comprimé 1 g », « Radiographie panoramique ».
 *
 * ⚠️ Deliberately NOT {@link formatPrescriptionLine}: that one is the full printed sentence, which would blow
 * out a table cell. The server's `PrescriptionLines.ShortLabel` is the twin, and it is what the séance history
 * actually reads — this copy exists only for the fiche's own section summary, which must label lines the user
 * has typed and NOT yet saved, and therefore cannot be served.
 */
export const shortPrescriptionLabel = (line: PrescriptionLine): string => {
  const name = line.name?.trim() ?? ""
  if (!name) return ""
  if (isExamenLine(line.kind)) return name
  const dosage = line.dosage?.trim()
  return dosage ? `${name} ${dosage}` : name
}

/** An empty médicament line, as both writing surfaces create one. */
export const emptyPrescriptionLine = (kind: PrescriptionKind): PrescriptionLine => ({
  kind,
  name: "",
  dosage: "",
  timesPerDay: "",
  duration: "",
})
