"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { FileText, Mail, FileBarChart, Shield, FolderOpen, Edit, Trash2 } from "lucide-react"
import { useRouter } from "next/navigation"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { medicalDocumentsApi } from "@/lib/api/medical-documents"
import type { MedicalDocumentDto } from "@/lib/api/types"
import { format } from "date-fns"
import { toast } from "sonner"

const documentTemplates = [
  {
    type: "prescription",
    title: "Ordonnance",
    description: "Prescription médicale pour traitement dentaire et médicaments",
    icon: FileText,
    color: "text-blue-600 dark:text-blue-400",
    bgColor: "bg-gradient-to-br from-blue-500 to-blue-600",
  },
  {
    type: "liaison",
    title: "Lettre de liaison",
    description: "Courrier médical de liaison vers un confrère ou spécialiste",
    icon: Mail,
    color: "text-green-600 dark:text-green-400",
    bgColor: "bg-gradient-to-br from-green-500 to-green-600",
  },
  {
    type: "honoraires",
    title: "Note d'honoraires",
    description: "Facture détaillée des soins et traitements dentaires",
    icon: FileBarChart,
    color: "text-amber-600 dark:text-amber-400",
    bgColor: "bg-gradient-to-br from-amber-500 to-amber-600",
  },
  {
    type: "certificat",
    title: "Certificat médical",
    description: "Certificat d'arrêt de travail ou justificatif médical",
    icon: Shield,
    color: "text-purple-600 dark:text-purple-400",
    bgColor: "bg-gradient-to-br from-purple-500 to-purple-600",
  },
]

const getDocumentTypeName = (type: string) => {
  const names: Record<string, string> = {
    prescription: "Ordonnance",
    liaison: "Lettre de liaison",
    honoraires: "Note d'honoraires",
    certificat: "Certificat médical",
  }
  return names[type] || type
}

export default function DocumentsPage() {
  const router = useRouter()

  // A post-visit review deep-links here with ?appointmentId=… so the chosen template's editor can
  // associate the new record with that visit. Read from the URL at click time (avoids useSearchParams,
  // which would force this page out of static prerendering — see the notification-center deep-link
  // learning — and avoids a mount-effect race that could drop the id on a very early click).
  const openTemplate = (type: string) => {
    const appointmentId = new URLSearchParams(window.location.search).get("appointmentId")
    const suffix = appointmentId ? `?appointmentId=${encodeURIComponent(appointmentId)}` : ""
    router.push(`/documents/${type}${suffix}`)
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex-1 overflow-y-auto p-6">
          <div className="mx-auto max-w-7xl space-y-6">
        {/* Header */}
        <div className="text-center space-y-4 mb-12">
          <div className="inline-block">
            <div className="w-16 h-16 mx-auto bg-gradient-to-br from-blue-500 to-blue-600 rounded-xl flex items-center justify-center shadow-lg mb-4">
              <FileText className="w-8 h-8 text-white" />
            </div>
          </div>
          <h1 className="text-4xl font-bold bg-gradient-to-r from-blue-600 to-blue-800 bg-clip-text text-transparent">
            Documents médicaux
          </h1>
          <p className="text-lg text-muted-foreground max-w-2xl mx-auto">
            Créez et gérez vos documents professionnels à partir de modèles prédéfinis
          </p>
        </div>

        {/* Template Grid */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6 max-w-6xl mx-auto">
          {documentTemplates.map((template) => {
            const Icon = template.icon
            return (
              <Card
                key={template.type}
                className="group cursor-pointer transition-all duration-300 hover:shadow-xl hover:scale-105 hover:ring-2 hover:ring-blue-200 dark:hover:ring-blue-800 overflow-hidden"
                onClick={() => openTemplate(template.type)}
              >
                <div className="p-6 space-y-4 flex flex-col h-full">
                  {/* Icon */}
                  <div
                    className={`${template.bgColor} w-12 h-12 rounded-lg flex items-center justify-center shadow-md transition-transform group-hover:scale-110`}
                  >
                    <Icon className="w-6 h-6 text-white" />
                  </div>

                  {/* Content */}
                  <div className="space-y-2 flex-grow">
                    <h3 className="text-lg font-bold text-foreground group-hover:text-blue-600 transition-colors">
                      {template.title}
                    </h3>
                    <p className="text-sm text-muted-foreground leading-snug">{template.description}</p>
                  </div>

                  {/* Arrow indicator */}
                  <div className="flex items-center text-xs font-medium text-blue-600 dark:text-blue-400 group-hover:translate-x-1 transition-transform pt-2">
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
          </div>
        </main>
      </div>
      </div>
    </ClinicGuard>
  )
}

