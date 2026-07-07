"use client"

import { useState } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { ClinicGuard } from "@/components/clinic-guard"
import { ProcedureTypesTable } from "@/components/procedure-types-table"
import { ProcedureTypeFormModal } from "@/components/procedure-type-form-modal"
import type { ProcedureTypeDto } from "@/lib/api/types"

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

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          
          <main className="flex-1 overflow-auto p-4">
            <div className="mx-auto max-w-7xl">
              <ProcedureTypesTable 
                key={refreshKey}
                onEdit={handleEdit} 
                onAdd={handleAdd}
              />
            </div>
          </main>
        </div>

        <ProcedureTypeFormModal
          open={modalOpen}
          onOpenChange={setModalOpen}
          editingProcedure={editingProcedure}
          onSuccess={handleSuccess}
        />
      </div>
    </ClinicGuard>
  )
}







