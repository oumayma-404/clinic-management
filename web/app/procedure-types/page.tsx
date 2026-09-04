"use client"

import { useState } from "react"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { ProcedureTypesTable } from "@/components/procedure-types-table"
import { ProcedureTypeFormModal } from "@/components/procedure-type-form-modal"
import type { ProcedureTypeDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export default function ProcedureTypesPage() {
  const [modalOpen, setModalOpen] = useState(false)
  const [editingProcedure, setEditingProcedure] = useState<ProcedureTypeDto | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const handleAdd = () => {
    setEditingProcedure(null)
    setModalOpen(true)
  }

  const handleEdit = (procedure: ProcedureTypeDto) => {
    setEditingProcedure(procedure)
    setModalOpen(true)
  }

  const handleSuccess = () => {
    setRefreshKey(prev => prev + 1)
  }

  // Real-time: refetch when any client of this clinic creates/edits/deletes a procedure type.
  useClinicRealtime(RealtimeResource.ProcedureTypes, handleSuccess)

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        {/* The page had NO title: it rendered straight into the table, whose 16px `CardTitle` was then the
            largest text on the screen, and the route's zone eyebrow and icon never appeared. No `zone` prop —
            `PageHeader` derives it from the route. */}
        <PageHeader
          title="Types de procédures"
          subtitle="Le catalogue d'actes qui alimente l'agenda, les devis et les fiches de soins."
        />

        {/*
          ⚠️ **`reloadKey`, never `key`.** A changed `key` REMOUNTS the table, so every refresh threw away its
          search term, its catégorie filter and its page — measured: searching « B2-Test » (1 row), editing the
          act's protocol and saving landed back on « 1–25 sur 35 types d'actes » with the box empty, and the
          refetch went out with no `search=` at all. Setting up a catalogue means doing that fourteen times, so
          each save cost a re-typed search and a re-scan of a paged table to find where you were. The table
          already refetches its current page from `reloadKey` and keeps its own state.
        */}
        <ProcedureTypesTable reloadKey={refreshKey} onEdit={handleEdit} onAdd={handleAdd} />

        <ProcedureTypeFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingProcedure={editingProcedure}
          onSuccess={handleSuccess}
        />
      </AppShell>
    </ClinicGuard>
  )
}







