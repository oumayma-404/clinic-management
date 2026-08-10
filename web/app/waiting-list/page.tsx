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
import {
  AlertTriangle,
  Check,
  ChevronsUpDown,
  ClipboardList,
  Loader2,
  MoreHorizontal,
  Pencil,
  Plus,
  Trash2,
  UserMinus,
  UserPlus,
} from "lucide-react"
import { CardList, CARDS_ONLY, TABLE_ONLY } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command"
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
import { showErrorToast } from "@/lib/errors"
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

/** Column widths the loading skeleton mirrors, in the table's own order — Patient, Priorité, Créneau, Note,
 *  Date d'ajout, Actions. Kept beside the table so the two cannot drift into different shapes. */
const WAITING_LIST_COLUMN_WIDTHS = ["w-[24%]", "w-[12%]", "w-[18%]", "w-[22%]", "w-[14%]", "w-[10%]"] as const

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
  // The patient picker is a searchable Popover+Command, not a `<select>` — see the note at its call site.
  const [patientPickerOpen, setPatientPickerOpen] = useState(false)
  /**
   * The picked patient's name for the trigger. Falls back to the entry's own snapshot: on edit the picker is
   * disabled and the patient list may not contain that patient (it is a `list()` of the clinic, and an archived
   * patient is excluded), so resolving from `patients` alone would show « Sélectionner un patient » over a row
   * that plainly has one.
   */
  const selectedPatientName = (() => {
    const match = patients.find((p) => p.id === patientId)
    if (match) return `${match.firstName} ${match.lastName}`.trim()
    return editingEntry?.patientId === patientId ? (editingEntry?.patientName ?? "") : ""
  })()

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

  /**
   * Whether the list is currently narrowed. « la liste d'attente est vide » and « aucun patient ne correspond à
   * votre recherche » are different facts and must not share copy — and only the first may offer « Ajouter »,
   * since on the second the entry probably exists and the term was simply mistyped.
   */
  const isSearching = debouncedSearch !== ""

  const renderEmpty = (size: "default" | "compact") =>
    isSearching ? (
      <div className="flex flex-col items-center gap-2 py-2">
        <p className="text-sm text-muted-foreground">Aucun patient ne correspond à votre recherche</p>
        <Button variant="outline" size="sm" onClick={() => setSearch("")}>
          Effacer la recherche
        </Button>
      </div>
    ) : (
      <EmptyState
        icon={ClipboardList}
        size={size}
        title="La liste d'attente est vide"
        description="Inscrivez ici les patients qui attendent un créneau : dès qu'une place se libère, « Promouvoir » ouvre le rendez-vous avec le patient déjà sélectionné."
        action={
          <Button onClick={handleAddNew} className="gap-2">
            <Plus className="h-4 w-4" />
            Ajouter à la liste
          </Button>
        }
      />
    )

  /**
   * « Retirer de la liste » — AC-25, the first caller `WaitingListEntry.Cancel()` has ever had.
   *
   * ⚠️ Not the same action as « Supprimer » beside it, and the distinction is the point: cancelling keeps the row
   * and records that the patient stopped waiting, while deleting destroys the evidence they ever did. Until now
   * only the destructive one existed, so « elle a trouvé un rendez-vous ailleurs » and « je me suis trompé de
   * patient » were the same button. No confirm dialog: it is reversible in substance (the row survives) and the
   * default view is « en attente » only, which is what makes the entry disappear.
   */
  const handleCancel = async (entry: WaitingListEntryDto) => {
    try {
      await waitingListApi.cancel(entry.id)
      toast.success(`${entry.patientName ?? "Le patient"} retiré de la liste d'attente`)
      await loadEntries()
    } catch (err) {
      showErrorToast(err)
    }
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
        {/*
          The row could not wrap: `flex items-center justify-between` against a ~190px button leaves the title
          nothing to give on a 390px screen. `/caisse` already had the working shape.

          No `zone`: `PageHeader` derives it from the route (`/waiting-list` is « Quotidien »), and the
          hardcoded « Clinique » here contradicted the rail.
        */}
        <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
          <PageHeader
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
            {/*
              A failed read used to be a bare red <p> with no way out but a browser reload (finding #2) — and the
              loading state was a lone spinner in a ~100px box that the full table then replaced, jumping the
              page, while the skeletons `CardList` already draws sat unreachable behind it (finding #3). The
              retry banner is the `dashboard/dashboard-section.tsx` shape; `loading` now flows into the list.
            */}
            {error ? (
              <div
                role="status"
                className="flex flex-wrap items-center gap-3 rounded-lg border border-destructive/40 bg-destructive-wash p-3 text-sm"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 text-destructive" aria-hidden="true" />
                <span className="min-w-0 flex-1">{error}</span>
                <Button size="sm" variant="outline" onClick={loadEntries}>
                  Réessayer
                </Button>
              </div>
            ) : (
              <div>
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
                  loading={loading}
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
                        {/* AC-25: the outcome, not the deletion. Ordinary weight — the row survives. */}
                        <DropdownMenuItem onSelect={() => void handleCancel(e)}>
                          Retirer de la liste
                        </DropdownMenuItem>
                        <DropdownMenuItem
                          className="text-destructive focus:text-destructive"
                          /* `handleDelete`, not `setEntryToDelete`: the confirm dialog is gated on
                             `deleteDialogOpen`, which only `handleDelete` sets — so on a phone « Supprimer »
                             silently did nothing. */
                          onSelect={() => handleDelete(e)}
                        >
                          Supprimer
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
                  empty={renderEmpty("compact")}
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
                    {/* The desktop half of the skeleton: a shape like the rows it replaces, so the arriving
                        list does not shift the page. */}
                    {loading ? (
                      Array.from({ length: 5 }).map((_, row) => (
                        <TableRow key={`skeleton-${row}`}>
                          {WAITING_LIST_COLUMN_WIDTHS.map((width, col) => (
                            <TableCell key={col}>
                              <div
                                className={`h-5 animate-pulse rounded bg-muted ${width}`}
                                role={row === 0 && col === 0 ? "status" : undefined}
                                aria-label={row === 0 && col === 0 ? "Chargement de la liste d'attente" : undefined}
                              />
                            </TableCell>
                          ))}
                        </TableRow>
                      ))
                    ) : entries.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={6}>{renderEmpty("default")}</TableCell>
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
                              {/* AC-25 — « Retirer » records the outcome and keeps the row; « Supprimer » is for
                                  an entry made by mistake. They used to be one button, doing the second. */}
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => void handleCancel(entry)}
                                className="h-8 gap-1"
                              >
                                <UserMinus className="h-3 w-3" />
                                Retirer
                              </Button>
                              <Button
                                variant="ghost"
                                size="sm"
                                onClick={() => handleDelete(entry)}
                                className="h-8 gap-1 text-destructive hover:text-destructive"
                              >
                                <Trash2 className="h-3 w-3" />
                                Supprimer
                              </Button>
                            </div>
                          </TableCell>
                        </TableRow>
                      ))
                    )}
                  </TableBody>
                </Table>
                {/* Hidden while the skeletons are up: the pager reads its counts from an empty page, so it
                    would print « Aucun … » under rows that are still loading. */}
                {!loading && (
                  <DataTablePagination
                    page={entryPage}
                    onPageChange={setPage}
                    onPageSizeChange={setPageSize}
                    loading={loading}
                    label={["patient", "patients"]}
                  />
                )}
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
              {/*
                A searchable Popover+Command, not a native `<select>` (the pattern
                `create-appointment-dialog.tsx` already established). Two defects it closes: a clinic's whole
                patient list in an unfilterable dropdown is unusable past a few dozen names, and the native
                control rendered at `h-9` — 36px — which is under the 44px touch floor, because `globals.css`
                only raises `input`, `textarea` and `[data-slot="select-trigger"]`, never a bare `<select>`.

                `modal` is required: the parent Dialog disables pointer events outside its content, so a
                non-modal Popover portalled to <body> inherits `pointer-events: none` and its options can only
                be reached by keyboard.
              */}
              <Popover open={patientPickerOpen} onOpenChange={setPatientPickerOpen} modal>
                <PopoverTrigger asChild>
                  <Button
                    id="patient"
                    type="button"
                    variant="outline"
                    role="combobox"
                    aria-expanded={patientPickerOpen}
                    disabled={!!editingEntry}
                    className="h-10 w-full justify-between font-normal"
                  >
                    <span className={cn("truncate", !patientId && "text-muted-foreground")}>
                      {selectedPatientName || "Sélectionner un patient"}
                    </span>
                    <ChevronsUpDown className="ms-2 h-4 w-4 shrink-0 opacity-50" aria-hidden="true" />
                  </Button>
                </PopoverTrigger>
                <PopoverContent className="p-0" align="start" style={{ width: "var(--radix-popover-trigger-width)" }}>
                  <Command>
                    <CommandInput placeholder="Rechercher un patient…" />
                    <CommandList>
                      <CommandEmpty>Aucun patient trouvé.</CommandEmpty>
                      <CommandGroup>
                        {patients.map((p) => {
                          const fullName = `${p.firstName} ${p.lastName}`.trim()
                          return (
                            <CommandItem
                              key={p.id}
                              value={fullName}
                              onSelect={() => {
                                setPatientId(p.id)
                                setPatientPickerOpen(false)
                              }}
                            >
                              <Check
                                className={cn("me-2 h-4 w-4", patientId === p.id ? "opacity-100" : "opacity-0")}
                              />
                              {fullName}
                            </CommandItem>
                          )
                        })}
                      </CommandGroup>
                    </CommandList>
                  </Command>
                </PopoverContent>
              </Popover>
              {formError && <p className="text-xs text-destructive">{formError}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="priority">Priorité</Label>
              {/* `w-full`: `ui/select.tsx`'s trigger ships `w-fit`, so it rendered narrower than every other
                  field in this form. */}
              <Select value={priority} onValueChange={(v) => setPriority(v as Priority)}>
                <SelectTrigger id="priority" className="w-full">
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
            {/* The title names the object. « Êtes-vous sûr ? » was the same sentence used to delete a patient. */}
            <AlertDialogTitle>Retirer ce patient de la liste d&apos;attente ?</AlertDialogTitle>
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
