"use client"

import { useState } from "react"
import { Card } from "@/components/ui/card"
import { FileText, Mail, FileBarChart, Shield } from "lucide-react"
import { useRouter } from "next/navigation"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { HonorairesLauncher } from "@/components/documents/honoraires-launcher"
import { PageHeader } from "@/components/ui/page-header"

const documentTemplates = [
  {
    type: "prescription",
    title: "Ordonnance",
    description: "Prescription médicale pour traitement dentaire et médicaments",
    icon: FileText,
    color: "text-chart-1",
    tile: "bg-chart-1/12 text-chart-1",
  },
  {
    type: "liaison",
    title: "Lettre de liaison",
    description: "Courrier médical de liaison vers un confrère ou spécialiste",
    icon: Mail,
    color: "text-chart-5",
    tile: "bg-chart-5/12 text-chart-5",
  },
  {
    type: "honoraires",
    title: "Note d'honoraires",
    description: "Facture détaillée des soins et traitements dentaires",
    icon: FileBarChart,
    color: "text-chart-4",
    tile: "bg-chart-4/12 text-chart-4",
  },
  {
    type: "certificat",
    title: "Certificat médical",
    description: "Certificat d'arrêt de travail ou justificatif médical",
    icon: Shield,
    color: "text-chart-3",
    tile: "bg-chart-3/12 text-chart-3",
  },
  {
    type: "bulletin-cnam",
    title: "Bulletin de soins CNAM",
    description: "Bulletin de remboursement des frais de soins (BS1) à déposer à la CNAM",
    icon: FileText,
    color: "text-chart-2",
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
          zone="Clinique"
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

