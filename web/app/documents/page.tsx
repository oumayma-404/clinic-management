"use client"

import { useState } from "react"
import { Card } from "@/components/ui/card"
import { CalendarX, FileText, Mail, FileBarChart, Shield } from "lucide-react"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { HonorairesLauncher } from "@/components/documents/honoraires-launcher"
import { PageHeader } from "@/components/ui/page-header"

/**
 * The five templates, each with its own categorical hue.
 *
 * <p>`tile` is written as a complete class string per entry rather than composed from `bg-chart-${n}/12`:
 * Tailwind scans source for literal class names, so an interpolated one is never generated and the tile would
 * render with no colour at all — the quiet failure mode of every themed system.</p>
 *
 * <p>There was also a second field, `color: "text-chart-N"`, that nothing read — the tile carries both the wash
 * and the ink. A duplicated hue nobody renders is the thing that drifts from the one that is rendered.</p>
 *
 * <p>This is a module constant, not a fetch: there is no loading state and no empty state to render, because
 * the gallery cannot be empty and cannot fail.</p>
 */
const documentTemplates = [
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
    title: "Certificat médical",
    // ⚠️ It no longer claims to cover an arrêt de travail (L11). It never could: a free-text certificat is not
    // the CNAM P 061 form and the caisse refuses it, so the description was pointing dentists at the one
    // template guaranteed not to work for that.
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

export default function DocumentsPage() {
  const router = useRouter()
  const [honorairesOpen, setHonorairesOpen] = useState(false)

  // Any caller may deep-link here with ?appointmentId=… to have the chosen template's editor associate the
  // document with that visit; it is forwarded verbatim. (The post-visit review prompt used to be that caller,
  // but « Ajouter le dossier médical » means the patient's record modal, not a document template — it now
  // deep-links to /patients/{id}?addRecord=1 like the notification panel does.)
  // Read from the URL at click time (avoids useSearchParams,
  // which would force this page out of static prerendering — see the notification-center deep-link
  // learning — and avoids a mount-effect race that could drop the id on a very early click).
  const openTemplate = (type: string) => {
    const appointmentId = new URLSearchParams(window.location.search).get("appointmentId")
    const suffix = appointmentId ? `?appointmentId=${encodeURIComponent(appointmentId)}` : ""
    router.push(`/documents/${type}${suffix}`)
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        <PageHeader
          title="Documents médicaux"
          subtitle="Cinq modèles — ordonnance, liaison, honoraires, certificat, bulletin CNAM."
        />

        {/* Template Grid. AC-P3.38 — every clickable Card is keyboard-operable: it is the click target, so
            it has to be a tab stop with Enter/Space and a visible focus ring, not a mouse-only div. */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 max-w-6xl mx-auto">
          {documentTemplates.map((template) => {
            const Icon = template.icon
            const open = () =>
              template.type === "honoraires" ? setHonorairesOpen(true) : openTemplate(template.type)
            return (
              <Card
                key={template.type}
                role="button"
                tabIndex={0}
                aria-label={`Créer : ${template.title}`}
                /*
                 * Hover effects are gated behind `hover-hover:` (pointer: fine). A touch tap fires `:hover`
                 * and *leaves it applied* until the next tap elsewhere, so on a tablet — which is what a
                 * dentist actually holds at the chair — these cards stayed scaled-up and ringed after being
                 * tapped, which reads as a stuck selection. Named properties instead of `transition-all`, and
                 * 200 ms `ease-snap` instead of 300 ms linear-ish: a hover is the fastest feedback loop in
                 * the UI and 300 ms is above the ceiling for one. `active:scale-[0.99]` gives the press the
                 * same acknowledgement the Button now has, since this Card *is* a button.
                 */
                className="group cursor-pointer overflow-hidden transition-[transform,box-shadow] duration-200 ease-snap hover-hover:hover:scale-[1.03] hover-hover:hover:shadow-xl hover-hover:hover:ring-2 hover-hover:hover:ring-primary/25 active:scale-[0.99] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
                onClick={open}
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault()
                    open()
                  }
                }}
              >
                <div className="p-6 space-y-4 flex flex-col h-full">
                  {/* Icon */}
                  {/* A tinted tile in the type's own categorical hue, replacing a gradient with a white glyph:
                      the tile identifies the document, it is not a piece of branding. */}
                  <div
                    className={`${template.tile} flex size-12 items-center justify-center rounded-lg transition-transform duration-200 ease-snap hover-hover:group-hover:scale-110`}
                  >
                    <Icon className="size-6" />
                  </div>

                  {/* Content */}
                  <div className="space-y-2 flex-grow">
                    <h3 className="text-base font-semibold text-foreground transition-colors group-hover:text-primary">
                      {template.title}
                    </h3>
                    <p className="text-sm text-muted-foreground leading-snug">{template.description}</p>
                  </div>

                  {/* Arrow indicator */}
                  <div className="flex items-center pt-2 text-xs font-medium text-primary transition-transform duration-200 ease-snap hover-hover:group-hover:translate-x-1">
                    <span>Créer</span>
                    <svg className="w-4 h-4 ml-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                    </svg>
                  </div>
                </div>
              </Card>
            )
          })}
        </div>
      </AppShell>
      {/* FR-1: honoraires → patient picker → compliant invoice draft (no document editor) */}
      <HonorairesLauncher open={honorairesOpen} onOpenChange={setHonorairesOpen} />
    </ClinicGuard>
  )
}

