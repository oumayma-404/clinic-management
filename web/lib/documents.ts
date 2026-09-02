import { CalendarX, FileBarChart, FileText, Mail, Shield, type LucideIcon } from "lucide-react"

/**
 * The six document templates — **one registry, because there were three and they disagreed.**
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
