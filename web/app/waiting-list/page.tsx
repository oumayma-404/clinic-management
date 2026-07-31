"use client"

import type React from "react"

import { useCallback, useEffect, useState } from "react"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"

import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { toast } from "sonner"
import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { PageHeader } from "@/components/ui/page-header"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Badge } from "@/components/ui/badge"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Textarea } from "@/components/ui/textarea"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { ClipboardList, Plus, Pencil, Trash2, UserPlus, Loader2, MoreHorizontal } from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import { cn } from "@/lib/utils"
import { waitingListApi, type WaitingListPayload } from "@/lib/api/waiting-list"
import { patientsApi } from "@/lib/api/patients"
import { ApiError } from "@/lib/api/client"
import type { WaitingListEntryDto, PatientDto } from "@/lib/api/types"

// ── Priority helpers (backend values Low|Normal|High ↔ French labels + Badge styling) ──────────────
type Priority = "Low" | "Normal" | "High"

const PRIORITY_OPTIONS: { value: Priority; label: string }[] = [
  { value: "Low", label: "Basse" },
  { value: "Normal", label: "Normale" },
  { value: "High", label: "Haute" },
]

const PRIORITY_LABELS: Record<string, string> = {
  Low: "Basse",
  Normal: "Normale",
  High: "Haute",
}

function priorityLabel(priority: string): string {
  return PRIORITY_LABELS[priority] ?? priority
}

function priorityBadgeVariant(priority: string): "destructive" | "secondary" | "outline" {
  switch (priority) {
    case "High":
      return "destructive"
    case "Low":
      return "outline"
    default:
      return "secondary"
  }
}

// French short date (e.g. "17 juil. 2026"); tolerant of an unparseable value.
function formatAddedDate(iso: string): string {
  try {
    return format(new Date(iso), "d MMM yyyy", { locale: fr })
  } catch {
    return "—"
  }
}

export default function WaitingListPage() {
  const [entryPage, setEntryPage] = useState<PagedResponse<WaitingListEntryDto>>(
    () => emptyPage<WaitingListEntryDto>(),
  )
  const entries = entryPage.items
  const [search, setSearch] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Patients for the picker (loaded once on mount).
  const [patients, setPatients] = useState<PatientDto[]>([])

  // Add/edit dialog state.
  const [modalOpen, setModalOpen] = useState(false)
  const [editingEntry, setEditingEntry] = useState<WaitingListEntryDto | null>(null)
  const [patientId, setPatientId] = useState("")
  const [priority, setPriority] = useState<Priority>("Normal")
  const [desiredTimeframe, setDesiredTimeframe] = useState("")
  const [note, setNote] = useState("")
  const [formError, setFormError] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  // Per-row promote state (disable the button while in flight).
  const [promotingId, setPromotingId] = useState<string | null>(null)

  // Promote-and-book (P1-B): the entry whose appointment we're booking. Opening the dialog is the
  // whole "Promouvoir" gesture now — booking the RDV from the entry and promoting are one flow.
  const [promoteBookEntry, setPromoteBookEntry] = useState<WaitingListEntryDto | null>(null)

  // Remove (delete) confirmation state.
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false)
  const [entryToDelete, setEntryToDelete] = useState<WaitingListEntryDto | null>(null)
  const [deleting, setDeleting] = useState(false)

  const loadEntries = useCallback(async () => {
    try {
      setLoading(true)
      setError(null)
      const data = await waitingListApi.listPaged({
        page,
        pageSize,
        search: debouncedSearch || undefined,
        activeOnly: true,
      })
      setEntryPage(data)
    } catch (err) {
      const message = err instanceof ApiError ? err.message : "Échec du chargement de la liste d'attente"
      setError(message)
      toast.error(message)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, debouncedSearch])

  // Debounced so a search does not fire a request per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search.trim()), 300)
    return () => clearTimeout(timer)
  }, [search])

  // A new term (or filter) must not leave the table on a page the narrowed result set no longer has.
  useEffect(() => {
    setPage(1)
  }, [debouncedSearch])

  const loadPatients = useCallback(async () => {
    try {
      const data = await patientsApi.list()
      setPatients(data)
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du chargement des patients")
    }
  }, [])

  useEffect(() => {
    loadEntries()
    loadPatients()
  }, [loadEntries, loadPatients])

  // AC-P4.21/4.26 — the salle d'attente is the canonical two-user screen: one person adds a walk-in at the
  // desk while another works from the same queue. It had no subscription, so both saw a stale list until
  // somebody reloaded. `waitinglist` covers the queue itself; `appointments` because promoting an entry
  // creates a rendez-vous and removes the row, which the other viewer must see too.
  useClinicRealtime([RealtimeResource.WaitingList, RealtimeResource.Appointments], loadEntries)

  // Reset the form whenever the dialog opens (create) or the edited entry changes.
  useEffect(() => {
    if (!modalOpen) return
    if (editingEntry) {
      setPatientId(editingEntry.patientId)
      setPriority((["Low", "Normal", "High"].includes(editingEntry.priority) ? editingEntry.priority : "Normal") as Priority)
      setDesiredTimeframe(editingEntry.desiredTimeframe ?? "")
      setNote(editingEntry.note ?? "")
    } else {
      setPatientId("")
      setPriority("Normal")
      setDesiredTimeframe("")
      setNote("")
    }
    setFormError(null)
  }, [modalOpen, editingEntry])

  const handleAddNew = () => {
    setEditingEntry(null)
    setModalOpen(true)
  }

  const handleEdit = (entry: WaitingListEntryDto) => {
    setEditingEntry(entry)
    setModalOpen(true)
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!editingEntry && !patientId) {
      setFormError("Veuillez sélectionner un patient")
      return
    }

    const timeframe = desiredTimeframe.trim() || null
    const trimmedNote = note.trim() || null

    try {
      setSaving(true)
      if (editingEntry) {
        // Update leaves the patient unchanged (payload without patientId).
        await waitingListApi.update(editingEntry.id, {
          priority,
          preferredDoctorId: null,
          desiredTimeframe: timeframe,
          note: trimmedNote,
        })
        toast.success("Entrée mise à jour")
      } else {
        const payload: WaitingListPayload = {
          patientId,
          priority,
          preferredDoctorId: null,
          desiredTimeframe: timeframe,
          note: trimmedNote,
        }
        await waitingListApi.create(payload)
        toast.success("Ajouté à la liste d'attente")
      }
      setModalOpen(false)
      await loadEntries()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'enregistrement")
    } finally {
      setSaving(false)
    }
  }

  // "Promouvoir" now books the appointment from the entry (patient pre-selected) in one gesture; the
  // entry is promoted with the new appointment's id once the dialog reports success.
  const handlePromote = (entry: WaitingListEntryDto) => {
    setPromoteBookEntry(entry)
  }

  const handlePromoteBooked = async (appointmentId: string) => {
    const entry = promoteBookEntry
    if (!entry) return
    try {
      setPromotingId(entry.id)
      await waitingListApi.promote(entry.id, appointmentId)
      toast.success("Rendez-vous créé et patient promu")
      await loadEntries()
    } catch (err) {
      // The appointment was already created; only the promotion link failed.
      toast.error(err instanceof ApiError ? err.message : "Rendez-vous créé, mais la promotion a échoué")
    } finally {
      setPromotingId(null)
    }
  }

  const handleDelete = (entry: WaitingListEntryDto) => {
    setEntryToDelete(entry)
    setDeleteDialogOpen(true)
  }

  const confirmDelete = async () => {
    if (!entryToDelete) return
    try {
      setDeleting(true)
      await waitingListApi.delete(entryToDelete.id)
      toast.success("Retiré de la liste d'attente")
      setDeleteDialogOpen(false)
      setEntryToDelete(null)
      await loadEntries()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec du retrait")
    } finally {
      setDeleting(false)
    }
  }

  return (
    <ClinicGuard>
      <AppShell contentClassName="space-y-6">
        {/* Page Header */}
        <div className="flex items-center justify-between">
          <PageHeader
            zone="Clinique"
            title="Salle d&apos;attente"
            subtitle="Patients en attente d&apos;un créneau de rendez-vous."
          />

          <Button onClick={handleAddNew} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter à la liste
          </Button>
        </div>

        {/* Waiting list table */}
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ClipboardList className="h-5 w-5" />
              Liste d&apos;attente
              <Badge variant="secondary" className="ml-2">
                {entryPage.totalCount}
              </Badge>
            </CardTitle>
          </CardHeader>
          <CardContent>
            {loading ? (
              <div className="flex items-center justify-center py-12 text-muted-foreground">
                <Loader2 className="h-5 w-5 animate-spin" />
              </div>
            ) : error ? (
              <p className="py-12 text-center text-sm text-destructive">{error}</p>
            ) : (
              <div className="overflow-x-auto">
                <div className="mb-4">
                  <Label htmlFor="waiting-list-search" className="sr-only">
                    Rechercher un patient
                  </Label>
                  <Input
                    id="waiting-list-search"
                    value={search}
                    onChange={(e) => setSearch(e.target.value)}
                    placeholder="Rechercher un patient, une note, un créneau…"
                  />
                </div>
                {/* « Promouvoir » stays a visible button rather than sinking into the menu: it is the whole
                    point of the salle d'attente and the one action taken dozens of times a day. */}
                <CardList
                  className={CARDS_ONLY}
                  ariaLabel="Salle d'attente"
                  items={entries}
                  getKey={(e) => e.id}
                  title={(e) => e.patientName ?? "Patient inconnu"}
                  status={(e) => (
                    <Badge variant={priorityBadgeVariant(e.priority)}>{priorityLabel(e.priority)}</Badge>
                  )}
                  fields={(e) => [
                    { label: "Créneau souhaité", value: e.desiredTimeframe?.trim() },
                    { label: "Note", value: e.note?.trim() },
                    { label: "Ajouté le", value: formatAddedDate(e.createdAt) },
                  ]}
                  actions={(e) => (
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon" aria-label={`Actions pour ${e.patientName ?? "ce patient"}`}>
                          <MoreHorizontal className="h-4 w-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end">
                        <DropdownMenuItem onSelect={() => handleEdit(e)}>Modifier</DropdownMenuItem>
                        <DropdownMenuItem
                          className="text-destructive focus:text-destructive"
                          onSelect={() => setEntryToDelete(e)}
                        >
                          Retirer
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  )}
                  /* Promoting is what this page is FOR, so it gets its own full-width row rather than sharing
                     the header with the ⋯ trigger — together they took ~150px of a 288px card and left the
                     patient's name three characters wide. */
                  primaryAction={(e) => (
                    <Button
                      variant="outline"
                      onClick={() => handlePromote(e)}
                      disabled={promotingId === e.id}
                      className="w-full gap-1"
                    >
                      {promotingId === e.id ? (
                        <Loader2 className="h-4 w-4 animate-spin" />
                      ) : (
                        <UserPlus className="h-4 w-4" />
                      )}
                      Promouvoir en rendez-vous
                    </Button>
                  )}
                  empty="La liste d'attente est vide"
                />
                <Table containerClassName={TABLE_ONLY}>
                  <TableHeader>
                    <TableRow>
                      <TableHead>Patient</TableHead>
                      <TableHead>Priorité</TableHead>
                      <TableHead>Créneau souhaité</TableHead>
                      <TableHead>Note</TableHead>
                      <TableHead>Date d&apos;ajout</TableHead>
                      <TableHead className="text-right">Actions</TableHead>
                    </TableRow>
                  </TableHeader>
                  <TableBody>
                    {entries.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={6} className="h-24 text-center">
                          <p className="text-muted-foreground">La liste d&apos;attente est vide</p>
                        </TableCell>
                      </TableRow>
                    ) : (
                      entries.map((entry) => (
                        <TableRow key={entry.id}>
                          <TableCell className="font-medium text-foreground">
                            {entry.patientName ?? "—"}
                          </TableCell>
                          <TableCell>
                            <Badge variant={priorityBadgeVariant(entry.priority)}>
                              {priorityLabel(entry.priority)}
                            </Badge>
                          </TableCell>
                          <TableCell className="text-muted-foreground">
                            {entry.desiredTimeframe?.trim() ? entry.desiredTimeframe : "—"}
                          </TableCell>
                          <TableCell className="max-w-xs truncate text-muted-foreground">
                            {entry.note?.trim() ? entry.note : "—"}
                          </TableCell>
                          <TableCell className="text-muted-foreground">{formatAddedDate(entry.createdAt)}</TableCell>
                          <TableCell className="text-right">
                            <div className="flex justify-end gap-2">
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => handlePromote(entry)}
                                disabled={promotingId === entry.id}
                                className="h-8 gap-1"
                              >
                                {promotingId === entry.id ? (
                                  <Loader2 className="h-3 w-3 animate-spin" />
                                ) : (
                                  <UserPlus className="h-3 w-3" />
                                )}
                                Promouvoir
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => handleEdit(entry)}
                                className="h-8 gap-1"
                              >
                                <Pencil className="h-3 w-3" />
                                Modifier
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => handleDelete(entry)}
                                className="h-8 gap-1 text-destructive hover:text-destructive"
                              >
                                <Trash2 className="h-3 w-3" />
                                Retirer
                              </Button>
                            </div>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
                <DataTablePagination
                  page={entryPage}
                  onPageChange={setPage}
                  onPageSizeChange={setPageSize}
                  loading={loading}
                  label={["patient", "patients"]}
                />
              </div>
            )}
          </CardContent>
        </Card>

        {/* Add / edit dialog */}
        <Dialog open={modalOpen} onOpenChange={setModalOpen}>
        <DialogContent className="md:max-w-md">
          <DialogHeader>
            <DialogTitle>{editingEntry ? "Modifier l'entrée" : "Ajouter à la liste d'attente"}</DialogTitle>
            <DialogDescription>
              {editingEntry
                ? "Mettez à jour les détails de l'entrée en liste d'attente"
                : "Ajoutez un patient à la liste d'attente"}
            </DialogDescription>
          </DialogHeader>
          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="space-y-2">
              <Label htmlFor="patient">
                Patient <span className="text-destructive">*</span>
              </Label>
              <select
                id="patient"
                value={patientId}
                onChange={(e) => setPatientId(e.target.value)}
                disabled={!!editingEntry}
                className={cn(
                  "border-input h-9 w-full min-w-0 rounded-md border bg-transparent px-3 py-1 text-base shadow-xs outline-none transition-[color,box-shadow] md:text-sm",
                  "focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px]",
                  "disabled:pointer-events-none disabled:cursor-not-allowed disabled:opacity-50",
                )}
              >
                <option value="" disabled>
                  Sélectionner un patient
                </option>
                {patients.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.firstName + " " + p.lastName}
                  </option>
                ))}
              </select>
              {formError && <p className="text-xs text-destructive">{formError}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="priority">Priorité</Label>
              <Select value={priority} onValueChange={(v) => setPriority(v as Priority)}>
                <SelectTrigger id="priority">
                  <SelectValue placeholder="Priorité" />
                </SelectTrigger>
                <SelectContent>
                  {PRIORITY_OPTIONS.map((opt) => (
                    <SelectItem key={opt.value} value={opt.value}>
                      {opt.label}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="desiredTimeframe">Créneau souhaité</Label>
              <Input
                id="desiredTimeframe"
                placeholder="ex : matin, cette semaine"
                value={desiredTimeframe}
                onChange={(e) => setDesiredTimeframe(e.target.value)}
              />
            </div>
            <div className="space-y-2">
              <Label htmlFor="note">Note</Label>
              <Textarea
                id="note"
                placeholder="Optionnel"
                value={note}
                onChange={(e) => setNote(e.target.value)}
                rows={2}
              />
            </div>
            <DialogFooter className="gap-2">
              <Button type="button" variant="outline" onClick={() => setModalOpen(false)} disabled={saving}>
                Annuler
              </Button>
              <Button type="submit" disabled={saving}>
                {saving ? "Enregistrement..." : editingEntry ? "Mettre à jour" : "Ajouter"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
        </Dialog>
        {/* Promote-and-book: create the RDV from the entry (patient pre-selected), then promote (P1-B). */}
        <CreateAppointmentDialog
        open={promoteBookEntry !== null}
        onOpenChange={(open) => {
          if (!open) setPromoteBookEntry(null)
        }}
        defaultPatientId={promoteBookEntry?.patientId}
        onCreated={handlePromoteBooked}
        />
        {/* Remove confirmation */}
        <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Êtes-vous sûr ?</AlertDialogTitle>
            <AlertDialogDescription>
              Cette action retirera{" "}
              <span className="font-semibold">{entryToDelete?.patientName ?? "ce patient"}</span> de la liste
              d&apos;attente. Cette action est irréversible.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deleting}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                confirmDelete()
              }}
              disabled={deleting}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deleting ? "Retrait..." : "Retirer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
        </AlertDialog>
      </AppShell>
    </ClinicGuard>
  )
}
