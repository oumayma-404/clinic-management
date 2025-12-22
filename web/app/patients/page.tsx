"use client"

import { useState } from "react"
import { DashboardHeader } from "@/components/dashboard-header"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { PatientsTable } from "@/components/patients-table"
import { Input } from "@/components/ui/input"
import { Button } from "@/components/ui/button"
import { Search, Filter } from "lucide-react"

export default function PatientsPage() {
  const [searchQuery, setSearchQuery] = useState("")
  const [showFlaggedOnly, setShowFlaggedOnly] = useState(false)

  return (
    <div className="flex h-screen bg-background">
      <DashboardSidebar />

      <div className="flex flex-1 flex-col overflow-hidden">
        <DashboardHeader />

        <main className="flex-1 overflow-y-auto p-6">
          <div className="mx-auto max-w-7xl space-y-6">
            {/* Page Header */}
            <div>
              <h1 className="text-3xl font-semibold text-foreground">Patients</h1>
              <p className="mt-1 text-sm text-muted-foreground">Manage and view all patient records</p>
            </div>

            {/* Search and Filters */}
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
              <div className="relative flex-1 max-w-md">
                <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  type="text"
                  placeholder="Search by name or phone..."
                  value={searchQuery}
                  onChange={(e) => setSearchQuery(e.target.value)}
                  className="pl-9"
                />
              </div>

              <Button
                variant={showFlaggedOnly ? "default" : "outline"}
                onClick={() => setShowFlaggedOnly(!showFlaggedOnly)}
                className="gap-2"
              >
                <Filter className="h-4 w-4" />
                {showFlaggedOnly ? "Showing Flagged" : "Show Flagged Only"}
              </Button>
            </div>

            {/* Patients Table */}
            <PatientsTable searchQuery={searchQuery} showFlaggedOnly={showFlaggedOnly} />
          </div>
        </main>
      </div>
    </div>
  )
}
