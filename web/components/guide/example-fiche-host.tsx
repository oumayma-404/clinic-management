"use client"

import { useEffect, useState } from "react"
import dynamic from "next/dynamic"

import { useGuide } from "@/components/guide/guide-provider"
import { EXAMPLE_FICHE_EVENT } from "@/lib/guides/dental-record"

// Loaded only on « Patients », and only once the fiche tour runs — the fiche is the app's heaviest window.
const PatientRecordModal = dynamic(
  () => import("@/components/patient-record-modal").then((module) => module.PatientRecordModal),
  { ssr: false },
)

/**
 * « Première fiche de soins » in a cabinet with no patient yet (owner's call, 2026-11-05): the tour opens this
 * example fiche and walks the same steps over it. It has no patient, so its « Enregistrer » saves nothing.
 *
 * <p>Mounted (closed) for as long as the tour runs, so the window is loaded before « Suivant » asks for it — the
 * tour ends a step whose window does not show up.</p>
 */
export function ExampleFicheHost() {
  const { running } = useGuide()
  const touring = running === "dentalRecord"
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const show = () => setOpen(true)
    window.addEventListener(EXAMPLE_FICHE_EVENT, show)
    return () => window.removeEventListener(EXAMPLE_FICHE_EVENT, show)
  }, [])

  // The tour over, its example goes with it.
  useEffect(() => {
    if (!touring) setOpen(false)
  }, [touring])

  if (!touring && !open) return null
  return <PatientRecordModal open={open} onOpenChange={setOpen} patientName="Patient exemple" />
}
