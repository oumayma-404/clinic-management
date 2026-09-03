"use client"

import type React from "react"

import { useCallback, useEffect, useRef, useState } from "react"
import { useParams } from "next/navigation"
import {
  ChevronRight,
  Download,
  File,
  Folder,
  FolderInput,
  Home,
  LayoutGrid,
  List,
  MoreVertical,
  Pencil,
  Plus,
  ShieldCheck,
  Trash2,
  Upload,
} from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
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
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { cn } from "@/lib/utils"
import { patientFilesApi } from "@/lib/api/patient-files"
import { formatDate, formatFileSize, quoteFr } from "@/lib/format"
import { showErrorToast } from "@/lib/errors"
import { downloadBlob } from "@/lib/download"
import { useUploadPolicy } from "@/lib/hooks/use-upload-policy"
import { useVault } from "@/lib/hooks/use-vault"
import { findVerifiedInVault, vaultDisplayPath, verifyVaultIntegrity } from "@/lib/vault/path"
import { DEFAULT_PAGE_SIZE, emptyPage, type PagedResponse } from "@/lib/api/paging"
import type { PatientFileDto, PatientFolderDto } from "@/lib/api/types"
import { toast } from "sonner"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

import { FilePreviewDialog } from "@/components/patients/files/file-preview-dialog"
import { FileThumbnail } from "@/components/patients/files/file-thumbnail"
import { FileResidencyBadge } from "@/components/patients/files/residency-badge"
import { RenameFileDialog } from "@/components/patients/files/rename-file-dialog"
import { UploadQueue, useUploadQueue } from "@/components/patients/files/upload-queue"
import { ResumeUploadsNotice } from "@/components/patients/files/resume-uploads-notice"
import { StorageUsageLine } from "@/components/patients/files/storage-usage-line"
import { useFilePreview } from "@/components/patients/files/use-file-preview"

type FilesView = "grid" | "list"

const VIEW_STORAGE_KEY = "clinic:patient-files-view"

export function PatientFilesManager({ patientName }: { patientName: string }) {
  const params = useParams()
  const patientId = params.id as string

  const policy = useUploadPolicy()

  const [folders, setFolders] = useState<PatientFolderDto[]>([])
  /**
   * Every folder this patient has, independently of where we are standing.
   *
   * ⚠️ `folders` holds the **children** of the current folder, so inside a folder it is empty — and three separate
   * symptoms came out of reading it as though it were the folder list: the breadcrumb showed no folder name, a
   * file's « Dossier » field was blank, and the move dialog offered only « Aucun dossier », so a file in a folder
   * could be moved back to the root and never to a sibling. All three want *this* set, not the children of the
   * place they are asked from.
   */
  const [allFolders, setAllFolders] = useState<PatientFolderDto[]>([])
  const [filePage, setFilePage] = useState<PagedResponse<PatientFileDto>>(emptyPage<PatientFileDto>())
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(null)
  const [isDragging, setIsDragging] = useState(false)
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [view, setView] = useState<FilesView>("grid")

  const [folderDialog, setFolderDialog] = useState<{ mode: "create" } | { mode: "rename"; folder: PatientFolderDto } | null>(null)
  const [folderName, setFolderName] = useState("")
  const [savingFolder, setSavingFolder] = useState(false)

  const [fileToEdit, setFileToEdit] = useState<PatientFileDto | null>(null)
  const [deletingFileId, setDeletingFileId] = useState<string | null>(null)
  const [verifyingFileId, setVerifyingFileId] = useState<string | null>(null)
  const [pendingDelete, setPendingDelete] = useState<
    { kind: "file"; file: PatientFileDto } | { kind: "folder"; folder: PatientFolderDto } | null
  >(null)
  const [deletePending, setDeletePending] = useState(false)

  const fileInput = useRef<HTMLInputElement | null>(null)
  const currentFolder = allFolders.find((f) => f.id === currentFolderId)
  const files = filePage.items

  // Read after mount, never during render: the server has no `localStorage`, and seeding state from it would
  // hydrate one view and paint the other.
  useEffect(() => {
    const stored = window.localStorage.getItem(VIEW_STORAGE_KEY)
    if (stored === "grid" || stored === "list") setView(stored)
  }, [])

  const chooseView = (next: FilesView) => {
    setView(next)
    window.localStorage.setItem(VIEW_STORAGE_KEY, next)
  }

  /**
   * Which end of the page the viewer should reopen on once a page turn lands.
   *
   * The arrows walk the loaded page; stepping past either end turns the page and continues, so a folder of 200
   * files reads as one sequence rather than eight that each dead-end at 25.
   */
  const resumeAt = useRef<"first" | "last" | null>(null)

  // ⚠️ Resolved here rather than beside the upload queue below, because `useFilePreview` needs it: a coffre
  // original is read from this folder, and asking the server for one can only 404.
  const { vault, status: vaultStatus, pair: pairVault, reconnect: reconnectVault } = useVault()

  const preview = useFilePreview(
    patientId,
    policy,
    {
      files,
      offset: (filePage.page - 1) * filePage.pageSize,
      total: filePage.totalCount,
      hasMoreBefore: filePage.hasPreviousPage,
      hasMoreAfter: filePage.hasNextPage,
      onPastStart: () => { resumeAt.current = "last"; setPage((p) => Math.max(1, p - 1)) },
      onPastEnd: () => { resumeAt.current = "first"; setPage((p) => p + 1) },
    },
    vault,
  )
  const openPreview = preview.open

  const loadData = useCallback(async () => {
    try {
      setLoading(true)
      const [foldersData, filesData, rootFolders] = await Promise.all([
        patientFilesApi.getFolders(patientId, currentFolderId || undefined),
        patientFilesApi.getFilesPaged(patientId, currentFolderId || undefined, { page, pageSize }),
        // The patient's folder list, for the breadcrumb, the « Dossier » field and the move destinations. Skipped
        // at the root, where the first call already IS that list — one round trip, not two, on the common path.
        currentFolderId ? patientFilesApi.getFolders(patientId) : Promise.resolve(null),
      ])
      setFolders(foldersData)
      // The current folder is a sibling of the roots, so the two sets together always contain it.
      setAllFolders(rootFolders ? [...rootFolders, ...foldersData] : foldersData)
      setFilePage(filesData)
      setLoadFailed(false)
    } catch (error) {
      // A failed read is not an empty drawer (§ 13) — the tab renders a retry banner, never « aucun fichier ».
      setLoadFailed(true)
      showErrorToast(error, {
        title: "Échec du chargement des fichiers",
        onRetry: () => void loadData(),
      })
    } finally {
      setLoading(false)
    }
  }, [patientId, currentFolderId, page, pageSize])

  useEffect(() => {
    void loadData()
  }, [loadData])

  useEffect(() => {
    const end = resumeAt.current
    if (!end || loading || files.length === 0) return
    resumeAt.current = null
    openPreview(end === "first" ? files[0] : files[files.length - 1])
  }, [files, loading, openPreview])

  // Real-time: reload when any client of this clinic uploads/deletes a file or folder.
  useClinicRealtime(RealtimeResource.Files, loadData)

  // Seed the default folder structure once per patient, AFTER the first load resolves. Keyed on the ref so it
  // fires exactly once: not on the initial render (when `loading` is still true), and not again if the user
  // later deletes every folder.
  const defaultsInitializedFor = useRef<string | null>(null)
  useEffect(() => {
    const initializeDefaults = async () => {
      try {
        await patientFilesApi.initializeDefaultFolders(patientId)
        await loadData()
      } catch {
        // Folders may already exist; nothing here is worth a toast.
      }
    }
    if (patientId && !loading && !loadFailed && defaultsInitializedFor.current !== patientId) {
      defaultsInitializedFor.current = patientId
      if (folders.length === 0) {
        void initializeDefaults()
      }
    }
  }, [patientId, loading, loadFailed, folders.length, loadData])

  const uploads = useUploadQueue({
    patientId,
    folderId: currentFolderId || undefined,
    policy,
    vault,
    onFileUploaded: () => void loadData(),
  })

  /**
   * ⚠️ Takes a **`File[]`, not the live `FileList`**. The picker below clears its own `value` the moment it
   * hands the selection over — which empties the very `FileList` the input exposes — so the array must be a
   * copy taken first, or a retry would upload nothing.
   */
  const handleFileUpload = (filesToUpload: File[]) => {
    void uploads.enqueue(filesToUpload)
  }

  /*
   * Dragging over a child fires `dragleave` on the parent, so a boolean flickers the overlay off the instant the
   * pointer crosses a tile. Counting enter/leave pairs is what makes a whole-surface drop zone hold.
   */
  const dragDepth = useRef(0)

  const carriesFiles = (e: React.DragEvent) => Array.from(e.dataTransfer.types).includes("Files")

  const handleDragEnter = (e: React.DragEvent) => {
    if (!carriesFiles(e)) return
    dragDepth.current += 1
    setIsDragging(true)
  }

  const handleDragLeave = () => {
    dragDepth.current = Math.max(0, dragDepth.current - 1)
    if (dragDepth.current === 0) setIsDragging(false)
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    dragDepth.current = 0
    setIsDragging(false)
    handleFileUpload(Array.from(e.dataTransfer.files))
  }

  const saveFolder = async () => {
    const name = folderName.trim()
    if (!name || !folderDialog || savingFolder) return

    try {
      setSavingFolder(true)
      if (folderDialog.mode === "create") {
        await patientFilesApi.createFolder(patientId, name, currentFolderId || undefined)
        toast.success("Dossier créé", { description: `Le dossier ${quoteFr(name)} a été créé.` })
      } else {
        await patientFilesApi.renameFolder(patientId, folderDialog.folder.id, name)
        toast.success("Dossier renommé", { description: `Le dossier s'appelle maintenant ${quoteFr(name)}.` })
      }
      setFolderName("")
      setFolderDialog(null)
      await loadData()
    } catch (error) {
      // The dialog stays open with the typed name intact, so the user retries rather than retypes.
      showErrorToast(error, "Le dossier n'a pas pu être enregistré.")
    } finally {
      setSavingFolder(false)
    }
  }

  const performDeleteFile = async (file: PatientFileDto) => {
    setDeletingFileId(file.id)
    try {
      await patientFilesApi.deleteFile(patientId, file.id)
      toast.success("Fichier supprimé", { description: `${quoteFr(file.fileName)} a été supprimé.` })
      await loadData()
    } catch (error) {
      showErrorToast(error, `${quoteFr(file.fileName)} n'a pas pu être supprimé.`)
    } finally {
      setDeletingFileId(null)
    }
  }

  const performDeleteFolder = async (folder: PatientFolderDto) => {
    try {
      await patientFilesApi.deleteFolder(patientId, folder.id)
      toast.success("Dossier supprimé", { description: `${quoteFr(folder.name)} a été supprimé.` })
      if (currentFolderId === folder.id) setCurrentFolderId(null)
      await loadData()
    } catch (error) {
      showErrorToast(error, `${quoteFr(folder.name)} n'a pas pu être supprimé.`)
    }
  }

  const confirmPendingDelete = async () => {
    if (!pendingDelete) return
    setDeletePending(true)
    try {
      if (pendingDelete.kind === "file") {
        await performDeleteFile(pendingDelete.file)
      } else {
        await performDeleteFolder(pendingDelete.folder)
      }
    } finally {
      setDeletePending(false)
      setPendingDelete(null)
    }
  }

  /**
   * « Copier le chemin » — the honest form of « open the folder ».
   *
   * ⚠️ A page served over `https:` cannot link to `file:`: every modern browser ignores such a click and reports
   * nothing, so a hyperlink here would be a control that looks like it works and does not. Copying the path is
   * what a person can actually use — paste it into the Explorer address bar. A real « ouvrir le dossier » needs
   * a reveal method on the desktop shell's bridge, which does not exist yet.
   */
  const copyPathAction = (path: string) => ({
    label: "Copier le chemin",
    onClick: () => {
      void navigator.clipboard
        ?.writeText(path)
        .then(() => toast.success("Chemin copié"))
        .catch(() => toast.error("Le chemin n'a pas pu être copié", { description: path }))
    },
  })

  /**
   * ⚠️ **The only reader of `contentHash` in the product.** The registration computes it, sends it and the server
   * stores it — and until this existed nothing ever compared it, so the open path's size check was the whole of
   * a coffre file's integrity story and a replacement of the same length read as genuine.
   */
  const handleVerifyFile = async (file: PatientFileDto) => {
    if (!vault) return

    // ⚠️ **The menu is allowed to close, and the progress lives in a toast.** Holding it open with
    // `event.preventDefault()` to show « Vérification… » in place looked tidier and trapped the whole page:
    // Radix keeps `pointer-events: none` on the document while a menu is open, so for the minute a 25 Go study
    // takes to hash, nothing else on the screen could be clicked — and the menu was still sitting there over the
    // result afterwards. A `toast.loading` sharing one id with its outcome says the same thing and blocks nothing.
    const toastId = `vault-integrity-${file.id}`
    setVerifyingFileId(file.id)
    toast.loading(`Vérification de ${quoteFr(file.fileName)}…`, {
      id: toastId,
      description: "L'empreinte est recalculée depuis le coffre. Cela peut prendre un moment sur un gros fichier.",
    })

    try {
      const verdict = await verifyVaultIntegrity(
        vault,
        patientId,
        file.id,
        file.fileName,
        file.fileSize,
        file.contentHash,
      )
      const where = vaultDisplayPath(vault.name, patientId, file.id, file.fileName)

      switch (verdict.kind) {
        case "intact":
          toast.success("Original intact", {
            id: toastId,
            description: "L'empreinte du fichier correspond à celle enregistrée lors de son dépôt.",
          })
          break
        case "missing":
          // Not a failure: the study is on the machine that recorded it (§ AC-9's own rule).
          toast.info("Original conservé au cabinet", {
            id: toastId,
            description: `Ce fichier n'est pas dans le coffre de ce poste. Il se trouve dans ${where}.`,
            action: copyPathAction(where),
          })
          break
        case "size-mismatch":
          toast.error("L'original ne correspond pas", {
            id: toastId,
            description: `Le fichier du coffre fait ${formatFileSize(verdict.found)} au lieu de ${formatFileSize(verdict.expected)}. Rien n'a été modifié ; vérifiez ${where}.`,
            action: copyPathAction(where),
          })
          break
        case "hash-mismatch":
          toast.error("L'original a changé depuis son dépôt", {
            id: toastId,
            description: `La taille est la bonne mais l'empreinte ne l'est pas : le fichier a été remplacé ou abîmé. Rien n'a été modifié ; vérifiez ${where}.`,
            action: copyPathAction(where),
          })
          break
        case "unknown-hash":
          toast.info("Aucune empreinte enregistrée", {
            id: toastId,
            description: "Ce fichier a été déposé avant l'enregistrement des empreintes ; son intégrité ne peut pas être vérifiée.",
          })
          break
      }
    } catch (error) {
      // Dismiss the loading toast first: `showErrorToast` mints its own, and leaving this one spinning beside it
      // says the verification is still running when it has already failed.
      toast.dismiss(toastId)
      showErrorToast(error, `L'intégrité de ${quoteFr(file.fileName)} n'a pas pu être vérifiée.`)
    } finally {
      setVerifyingFileId(null)
    }
  }

  const handleDownloadFile = async (file: PatientFileDto) => {
    try {
      // AC-9 — a coffre original is opened from the disk it never left. No request, no transfer: at Tunisia's
      // median uplink a 400 Mo study would take six minutes to come back down a wire it never went up.
      if (file.residency === "Vault") {
        const local = vault
          ? await findVerifiedInVault(vault, patientId, file.id, file.fileName, file.fileSize)
          : null

        // Where the original sits inside the coffre. Shown in every branch below that cannot simply hand the
        // file over, because « il est au cabinet » without a path leaves somebody hunting through folders named
        // after ids they have never seen.
        const where = vaultDisplayPath(vault?.name, patientId, file.id, file.fileName)

        if (!local) {
          // ⚠️ Not an error. The study is on the machine that recorded it, and a colleague's laptop legitimately
          // has no copy — so this names where it is rather than reporting a failure.
          toast.info("Original conservé au cabinet", {
            description:
              `Ce fichier n'est pas disponible sur ce poste. Sur le poste du cabinet il se trouve dans ${where}.`,
            action: copyPathAction(where),
          })
          return
        }

        await downloadBlob(local, file.fileName, { savedAt: where })
        return
      }

      const blob = await patientFilesApi.downloadFile(patientId, file.id)
      // One way to deliver a file. The hand-rolled `<a download>` this replaced is **ignored** by iOS Safari for
      // a `blob:` URL, so on an iPhone the button silently delivered nothing at all.
      await downloadBlob(blob, file.fileName)
    } catch (error) {
      showErrorToast(error, `${quoteFr(file.fileName)} n'a pas pu être téléchargé.`)
    }
  }

  const openFolderDialog = () => { setFolderName(""); setFolderDialog({ mode: "create" }) }

  const fileMenu = (file: PatientFileDto) => (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="sm"
          className="size-8 p-0 coarse:size-11"
          aria-label={`Actions pour ${file.fileName}`}
          disabled={deletingFileId === file.id}
          onKeyDown={(e) => e.stopPropagation()}
        >
          <MoreVertical className="h-4 w-4" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end">
        <DropdownMenuItem className="coarse:py-3" onSelect={() => preview.open(file)}>
          <File className="mr-2 h-4 w-4" />
          Aperçu
        </DropdownMenuItem>
        <DropdownMenuItem className="coarse:py-3" onSelect={() => void handleDownloadFile(file)}>
          <Download className="mr-2 h-4 w-4" />
          Télécharger
        </DropdownMenuItem>
        <DropdownMenuItem className="coarse:py-3" onSelect={() => setFileToEdit(file)}>
          <FolderInput className="mr-2 h-4 w-4" />
          Renommer, décrire, déplacer
        </DropdownMenuItem>
        {/* Only where the bytes could be here, and only for a coffre file: the server holds no copy of one, so its
            recorded empreinte is the only integrity evidence that exists. Deliberately an action rather than a
            check on open — a 25 Go study takes about a minute to re-read. */}
        {file.residency === "Vault" && vault && (
          <DropdownMenuItem
            className="coarse:py-3"
            disabled={verifyingFileId === file.id}
            onSelect={() => void handleVerifyFile(file)}
          >
            <ShieldCheck className="mr-2 h-4 w-4" />
            Vérifier l'intégrité
          </DropdownMenuItem>
        )}
        <DropdownMenuItem
          className="coarse:py-3 text-destructive focus:text-destructive"
          onSelect={() => setPendingDelete({ kind: "file", file })}
        >
          <Trash2 className="mr-2 h-4 w-4" />
          Supprimer
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )

  return (
    <div
      className="relative space-y-4"
      onDragEnter={handleDragEnter}
      onDragOver={(e) => { if (carriesFiles(e)) e.preventDefault() }}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div className="min-w-0">
          <h2 className="truncate text-xl font-bold text-foreground sm:text-2xl">Fichiers de {patientName}</h2>
          <p className="text-sm text-muted-foreground">
            {filePage.totalCount} {filePage.totalCount === 1 ? "fichier" : "fichiers"}
            {!currentFolderId && folders.length > 0 && (
              <> · {folders.length} {folders.length === 1 ? "dossier" : "dossiers"}</>
            )}
          </p>
        </div>
        {/* Full-width halves below `sm:` — « Nouveau dossier » + « Téléverser » is ~270 px of French, so at 320 px
            a shrink-to-fit row leaves two buttons pinched against the gutters. */}
        <div className="flex shrink-0 items-center gap-2">
          {!currentFolderId && (
            <Button variant="outline" onClick={openFolderDialog} size="sm" className="flex-1 coarse:h-11 sm:flex-none">
              <Plus className="mr-2 h-4 w-4" />
              Nouveau dossier
            </Button>
          )}
          {/* The 180 px drop zone this replaced pushed the drawer's own contents below the fold on a tablet. */}
          <Button size="sm" onClick={() => fileInput.current?.click()} className="flex-1 coarse:h-11 sm:flex-none">
            <Upload className="mr-2 h-4 w-4" />
            Téléverser
          </Button>
        </div>
      </div>

      <input
        ref={fileInput}
        type="file"
        multiple
        /*
         * Served by `GET /api/meta/upload-policy`, never mirrored here (AC-5.1). The literal it replaced
         * (`application/pdf,image/png,image/jpeg`) was accurate when written and hid every DICOM, STL and
         * HEIC the moment the catalog widened. An unread policy leaves the picker fully open — the server
         * still checks.
         */
        accept={policy?.accept}
        className="hidden"
        onChange={(e) => {
          /*
           * ⚠️ Copy, then clear the input **before** the upload runs. Without the clear, the element still
           * holds the file it just handed over, so re-picking the *same* file fires no `change` event at
           * all and « réessayer » silently does nothing.
           */
          const chosen = Array.from(e.target.files ?? [])
          e.target.value = ""
          handleFileUpload(chosen)
        }}
      />

      <div className="flex flex-wrap items-center gap-3 border-b pb-3">
        <nav aria-label="Fil d'Ariane" className="flex min-w-0 flex-1 items-center gap-1 text-sm text-muted-foreground">
          <Button variant="ghost" size="sm" onClick={() => setCurrentFolderId(null)} className="h-8 px-2 coarse:h-11">
            <Home className="mr-1 h-4 w-4" />
            Fichiers
          </Button>
          {currentFolder && (
            <>
              <ChevronRight className="h-4 w-4 shrink-0 text-primary" />
              <span className="truncate font-medium text-primary">{currentFolder.name}</span>
            </>
          )}
        </nav>

        <div className="flex items-center rounded-md border" role="group" aria-label="Affichage des fichiers">
          <ViewButton current={view} value="grid" icon={LayoutGrid} label="Grille" onSelect={chooseView} />
          <ViewButton current={view} value="list" icon={List} label="Liste" onSelect={chooseView} />
        </div>
      </div>

      {/* AC-6 / § 0 — the coffre's state is stated where files are added, and it never hides a control. It renders
          only where the deployment actually keeps large files at the cabinet: elsewhere there is nothing to say. */}
      {policy?.vaultAvailable && vaultStatus !== "checking" && vaultStatus !== "ready" && (
        <div
          className="flex flex-col gap-2 rounded-lg border border-dashed bg-muted/40 p-3 sm:flex-row sm:items-center sm:justify-between"
          role="status"
        >
          <p className="text-xs text-muted-foreground">
            {vaultStatus === "lapsed"
              ? "Le coffre du cabinet est enregistré sur ce poste, mais ce navigateur en a oublié l'autorisation. Reconnectez-le pour ajouter des fichiers volumineux."
              : vaultStatus === "unpaired"
                ? "Les fichiers volumineux (scanners 3D, empreintes) sont conservés au cabinet. Indiquez le dossier du coffre pour pouvoir en ajouter depuis ce poste."
                : "Les fichiers volumineux sont conservés au cabinet. Ce navigateur ne peut pas y accéder ; ouvrez APEXA sur le poste du cabinet pour en ajouter. Les autres fichiers s'envoient normalement."}
          </p>
          {/* ⚠️ « lapsed » is one click and « unpaired » is the whole picker. They were one branch, and the
              browser drops the grant when its last tab closes — so every morning re-asked for the folder. */}
          {vaultStatus === "lapsed" && (
            <Button
              variant="outline"
              size="sm"
              className="w-full shrink-0 coarse:h-11 sm:w-auto"
              onClick={() => void reconnectVault()}
            >
              Reconnecter le coffre
            </Button>
          )}
          {vaultStatus === "unpaired" && (
            <Button
              variant="outline"
              size="sm"
              className="w-full shrink-0 coarse:h-11 sm:w-auto"
              onClick={() => void pairVault()}
            >
              Choisir le dossier du coffre
            </Button>
          )}
        </div>
      )}

      {/* The cabinet's ceiling — a fact about the practice rather than about any one envoi, so it sits above
          both. Renders nothing where no quota is enforced. */}
      <StorageUsageLine policy={policy} />

      {/* Above the queue, because it is about an envoi that is not in the queue yet — and below the coffre
          notice, which is about what this machine can do at all. */}
      <ResumeUploadsNotice
        patientId={patientId}
        reloadToken={uploads.settledCount}
        inFlight={uploads.activeUploads}
        onResume={(record, file) => void uploads.resume(record, file)}
      />

      <UploadQueue
        items={uploads.items}
        running={uploads.running}
        onCancel={uploads.cancel}
        onClear={uploads.clear}
      />

      {/* Folders, as chips: four cards in a grid pushed the files themselves off the first screen. */}
      {!currentFolderId && folders.length > 0 && (
        <div className="flex flex-wrap gap-2">
          {folders.map((folder) => (
            <div
              key={folder.id}
              role="button"
              tabIndex={0}
              aria-label={`Ouvrir le dossier ${folder.name}`}
              className={cn(
                "flex cursor-pointer items-center gap-2 rounded-full border bg-card py-1 ps-3 pe-1 text-sm transition-colors hover:border-primary hover:bg-accent/30",
                FOCUS_CLASSES,
              )}
              onClick={() => setCurrentFolderId(folder.id)}
              onKeyDown={activateOnKey(() => setCurrentFolderId(folder.id))}
            >
              <Folder className="h-4 w-4 shrink-0 text-primary" />
              <span className="max-w-[9rem] truncate font-medium sm:max-w-[12rem] text-foreground">{folder.name}</span>
              <span className="tabular-nums text-xs text-muted-foreground">{folder.fileCount}</span>
              <div onClick={(e) => e.stopPropagation()}>
                <DropdownMenu>
                  <DropdownMenuTrigger asChild>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="size-8 rounded-full p-0 coarse:size-11"
                      aria-label={`Actions pour le dossier ${folder.name}`}
                      onKeyDown={(e) => e.stopPropagation()}
                    >
                      <MoreVertical className="h-4 w-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem
                      className="coarse:py-3"
                      onSelect={() => { setFolderName(folder.name); setFolderDialog({ mode: "rename", folder }) }}
                    >
                      <Pencil className="mr-2 h-4 w-4" />
                      Renommer
                    </DropdownMenuItem>
                    <DropdownMenuItem
                      className="coarse:py-3 text-destructive focus:text-destructive"
                      onSelect={() => setPendingDelete({ kind: "folder", folder })}
                    >
                      <Trash2 className="mr-2 h-4 w-4" />
                      Supprimer
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
            </div>
          ))}
          <Button variant="ghost" size="sm" onClick={openFolderDialog} className="rounded-full border border-dashed coarse:h-11">
            <Plus className="mr-1 h-4 w-4" />
            Nouveau dossier
          </Button>
        </div>
      )}

      {loading ? (
        <FilesSkeleton view={view} />
      ) : loadFailed ? (
        <LoadFailureNotice
          message="Les fichiers de ce patient n'ont pas pu être chargés."
          onRetry={() => void loadData()}
        />
      ) : files.length === 0 ? (
        <EmptyState
          icon={File}
          size="compact"
          title="Aucun fichier"
          description="Glissez une radiographie, un compte rendu ou une photo n'importe où sur cette page, ou utilisez « Téléverser »."
          action={
            <Button onClick={() => fileInput.current?.click()} className="coarse:h-11">
              <Upload className="mr-2 h-4 w-4" />
              Téléverser
            </Button>
          }
        />
      ) : (
        <>
          {view === "grid" ? (
            <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 sm:gap-3 lg:grid-cols-4 xl:grid-cols-5">
              {files.map((file) => (
                <li key={file.id}>
                  <Card
                    role="button"
                    tabIndex={0}
                    aria-label={`Ouvrir ${file.fileName}`}
                    className={cn(
                      "relative h-full cursor-pointer gap-0 overflow-hidden bg-card p-0 hover:border-primary",
                      FOCUS_CLASSES,
                    )}
                    onClick={() => preview.open(file)}
                    onKeyDown={activateOnKey(() => preview.open(file))}
                  >
                    {/* ⚠️ `absolute inset-0`, not `size-full`: a percentage height against a box whose own height
                        comes from `aspect-ratio` is not definite, so the image drove the tile and every row took
                        the height of its tallest picture. Absolute positioning makes the 4:3 box the authority. */}
                    <div className="relative aspect-[4/3] w-full border-b bg-muted/40">
                      <FileThumbnail
                        patientId={patientId}
                        file={file}
                        className="absolute inset-0 size-full rounded-none bg-transparent"
                        iconClassName="h-8 w-8"
                        imgClassName="object-contain"
                      />
                    </div>
                    <div className="min-w-0 p-2">
                      <p className="truncate text-sm font-semibold text-foreground" title={file.fileName}>
                        {file.fileName}
                      </p>
                      <p className="truncate text-xs tabular-nums text-muted-foreground">
                        {formatFileSize(file.fileSize)} · {formatDate(file.uploadedAt)}
                      </p>
                      <FileResidencyBadge file={file} className="mt-1" />
                    </div>
                    <div className="absolute end-1 top-1" onClick={(e) => e.stopPropagation()}>
                      {fileMenu(file)}
                    </div>
                  </Card>
                </li>
              ))}
            </ul>
          ) : (
            <ul className="space-y-2">
              {files.map((file) => (
                <li key={file.id}>
                  <Card
                    role="button"
                    tabIndex={0}
                    aria-label={`Ouvrir ${file.fileName}`}
                    className={cn(
                      "cursor-pointer bg-card p-3 transition-all duration-200 hover:border-primary/40 hover:shadow-sm",
                      FOCUS_CLASSES,
                    )}
                    onClick={() => preview.open(file)}
                    onKeyDown={activateOnKey(() => preview.open(file))}
                  >
                    <div className="flex items-center justify-between gap-3">
                      <div className="flex min-w-0 flex-1 items-center gap-3">
                        <FileThumbnail patientId={patientId} file={file} />
                        <div className="min-w-0 flex-1">
                          <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
                            <p className="truncate text-sm font-semibold text-foreground">{file.fileName}</p>
                            <FileResidencyBadge file={file} />
                          </div>
                          <p className="text-xs tabular-nums text-muted-foreground">
                            {formatFileSize(file.fileSize)} • {formatDate(file.uploadedAt)}
                          </p>
                          {file.description && (
                            <p className="truncate text-xs text-muted-foreground">{file.description}</p>
                          )}
                        </div>
                      </div>

                      {/* One menu, not two adjacent 32 px buttons: at that spacing an overlaid hit area has the
                          later sibling steal the earlier one's taps. */}
                      <div onClick={(e) => e.stopPropagation()}>{fileMenu(file)}</div>
                    </div>
                  </Card>
                </li>
              ))}
            </ul>
          )}

          <DataTablePagination
            page={filePage}
            loading={loading}
            label={["fichier", "fichiers"]}
            onPageChange={setPage}
            onPageSizeChange={(size) => { setPageSize(size); setPage(1) }}
          />
        </>
      )}

      {isDragging && (
        <div
          className="pointer-events-none absolute inset-0 z-20 flex flex-col items-center justify-center gap-2 rounded-xl border-2 border-dashed border-primary bg-accent/80 text-center backdrop-blur-sm"
          role="status"
        >
          <Upload className="h-10 w-10 text-primary" />
          <p className="text-sm font-semibold text-primary">Déposez les fichiers ici</p>
          <p className="text-xs text-muted-foreground">
            {currentFolder ? `Ils iront dans ${quoteFr(currentFolder.name)}` : "Ils iront à la racine"}
            {policy ? ` · jusqu'à ${formatFileSize(policy.maxBytes)} par fichier` : ""}
          </p>
        </div>
      )}

      {/* Folder create / rename */}
      <Dialog open={!!folderDialog} onOpenChange={(open) => { if (!open) setFolderDialog(null) }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              {folderDialog?.mode === "rename" ? "Renommer le dossier" : "Créer un dossier"}
            </DialogTitle>
            <DialogDescription>Saisissez un nom pour le dossier</DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="folder-name">Nom du dossier</Label>
            <Input
              id="folder-name"
              placeholder="Radiographies"
              value={folderName}
              onChange={(e) => setFolderName(e.target.value)}
              disabled={savingFolder}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault()
                  void saveFolder()
                }
              }}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setFolderDialog(null)} disabled={savingFolder}>
              Annuler
            </Button>
            <Button onClick={() => void saveFolder()} disabled={savingFolder || !folderName.trim()}>
              {savingFolder ? "Enregistrement…" : folderDialog?.mode === "rename" ? "Renommer" : "Créer le dossier"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <RenameFileDialog
        patientId={patientId}
        file={fileToEdit}
        // Every folder, not the children of where we are standing — see `allFolders`.
        folders={allFolders}
        onOpenChange={(open) => { if (!open) setFileToEdit(null) }}
        onSaved={() => void loadData()}
      />

      <FilePreviewDialog
        preview={preview}
        patientId={patientId}
        onDownload={(file) => void handleDownloadFile(file)}
        onDelete={(file) => setPendingDelete({ kind: "file", file })}
      />

      <AlertDialog open={!!pendingDelete} onOpenChange={(open) => { if (!open) setPendingDelete(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pendingDelete?.kind === "folder" ? "Supprimer le dossier ?" : "Supprimer le fichier ?"}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {pendingDelete?.kind === "folder"
                ? pendingDelete.folder.fileCount > 0
                  ? `Le dossier ${quoteFr(pendingDelete.folder.name)} contient ${pendingDelete.folder.fileCount} fichier(s). Tous seront supprimés. Cette action est irréversible.`
                  : `Voulez-vous vraiment supprimer ${quoteFr(pendingDelete.folder.name)} ? Cette action est irréversible.`
                : pendingDelete
                  ? `${quoteFr(pendingDelete.file.fileName)} sera définitivement supprimé. Cette action est irréversible.`
                  : ""}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deletePending}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              variant="destructive"
              onClick={(e) => {
                e.preventDefault()
                void confirmPendingDelete()
              }}
              disabled={deletePending}
            >
              {deletePending ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}

const FOCUS_CLASSES =
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"

// The file/folder cards are the click target, so they must also be a keyboard target: Enter and Space, a
// visible focus ring, and an accessible name.
const activateOnKey = (action: () => void) => (event: React.KeyboardEvent) => {
  if (event.key === "Enter" || event.key === " ") {
    event.preventDefault()
    action()
  }
}

function ViewButton({
  current,
  value,
  icon: Icon,
  label,
  onSelect,
}: {
  current: FilesView
  value: FilesView
  icon: typeof LayoutGrid
  label: string
  onSelect: (view: FilesView) => void
}) {
  const active = current === value

  return (
    <Button
      variant="ghost"
      size="sm"
      aria-pressed={active}
      onClick={() => onSelect(value)}
      className={cn(
        // ⚠️ `coarse:min-w-11` as well as the height. Below `sm:` the label is `sr-only`, so the button is a
        // 16 px icon in `px-3` — 36 px WIDE — and the height fix alone left it under the floor on the axis that
        // was actually short. `min-w`, not `w`, so the labelled form above `sm:` keeps its natural width.
        "h-9 rounded-none px-3 first:rounded-s-md last:rounded-e-md coarse:h-11 coarse:min-w-11",
        active && "bg-accent text-accent-foreground",
      )}
    >
      <Icon className="h-4 w-4 sm:mr-2" />
      <span className="sr-only sm:not-sr-only">{label}</span>
    </Button>
  )
}

/** Distinct from empty (§ 13): neither a card list nor a tile grid has a header row, so a blank region is
 *  otherwise ambiguous. */
function FilesSkeleton({ view }: { view: FilesView }) {
  if (view === "grid") {
    return (
      <ul className="grid grid-cols-2 gap-2 sm:grid-cols-3 sm:gap-3 lg:grid-cols-4 xl:grid-cols-5" aria-hidden="true">
        {[0, 1, 2, 3, 4].map((tile) => (
          <li key={tile}>
            <Card className="gap-0 overflow-hidden p-0">
              <div className="aspect-[4/3] animate-pulse bg-muted" />
              <div className="space-y-2 p-2">
                <div className="h-3 w-2/3 animate-pulse rounded bg-muted" />
                <div className="h-3 w-1/2 animate-pulse rounded bg-muted" />
              </div>
            </Card>
          </li>
        ))}
      </ul>
    )
  }

  return (
    <ul className="space-y-2" aria-hidden="true">
      {[0, 1, 2].map((row) => (
        <li key={row}>
          <Card className="flex items-center gap-3 p-3">
            <div className="size-10 shrink-0 animate-pulse rounded-lg bg-muted" />
            <div className="min-w-0 flex-1 space-y-2">
              <div className="h-3 w-1/3 animate-pulse rounded bg-muted" />
              <div className="h-3 w-1/5 animate-pulse rounded bg-muted" />
            </div>
          </Card>
        </li>
      ))}
    </ul>
  )
}
