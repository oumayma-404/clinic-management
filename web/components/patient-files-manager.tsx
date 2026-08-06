"use client"

import type React from "react"

import { useState, useEffect, useRef } from "react"
import { useParams } from "next/navigation"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Folder,
  File,
  Upload,
  Plus,
  ChevronRight,
  Home,
  FileText,
  ImageIcon,
  FileArchive,
  X,
  Download,
  Loader2,
} from "lucide-react"
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
import { cn } from "@/lib/utils"
import { patientFilesApi } from "@/lib/api/patient-files"
import { formatDate, formatFileSize } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { downloadBlob } from "@/lib/download"
import { PatientFilePdfPreview } from "@/components/patient-file-pdf-preview"
import { Label } from "@/components/ui/label"
import type { PatientFileDto, PatientFolderDto } from "@/lib/api/types"
import { toast } from "sonner"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"

export function PatientFilesManager({ patientName }: { patientName: string }) {
  const params = useParams()
  const patientId = params.id as string

  const [folders, setFolders] = useState<PatientFolderDto[]>([])
  const [files, setFiles] = useState<PatientFileDto[]>([])
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(null)
  const [isDragging, setIsDragging] = useState(false)
  const [isNewFolderOpen, setIsNewFolderOpen] = useState(false)
  const [newFolderName, setNewFolderName] = useState("")
  const [creatingFolder, setCreatingFolder] = useState(false)
  const [loading, setLoading] = useState(true)
  const [uploading, setUploading] = useState(false)
  const [deletingFileId, setDeletingFileId] = useState<string | null>(null)
  const [previewFile, setPreviewFile] = useState<PatientFileDto | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  // Destructive-delete confirmation (replaces native confirm() — AC-9).
  const [pendingDelete, setPendingDelete] = useState<
    { kind: "file"; fileId: string } | { kind: "folder"; folderId: string } | null
  >(null)
  const [deletePending, setDeletePending] = useState(false)

  const currentFolder = folders.find((f) => f.id === currentFolderId)
  const currentFiles = files.filter((f) => 
    currentFolderId ? f.folderId === currentFolderId : !f.folderId
  )

  // Load folders and files
  useEffect(() => {
    loadData()
  }, [patientId, currentFolderId])

  const loadData = async () => {
    try {
      setLoading(true)
      const [foldersData, filesData] = await Promise.all([
        patientFilesApi.getFolders(patientId, currentFolderId || undefined),
        patientFilesApi.getFiles(patientId, currentFolderId || undefined),
      ])
      setFolders(foldersData)
      setFiles(filesData)
    } catch (error) {
      console.error("Failed to load files:", error)
      toast.error("Échec du chargement des fichiers", {
        description: "Veuillez réessayer dans quelques instants",
        duration: 4000,
      })
    } finally {
      setLoading(false)
    }
  }

  // Real-time: reload folders/files when any client of this clinic uploads/deletes a file or folder.
  useClinicRealtime(RealtimeResource.Files, loadData)

  // Seed the default folder structure once per patient, AFTER the first load resolves. Keyed on the ref so
  // it fires exactly once: not on the initial render (when `loading` is still true — the old `!loading`
  // guard was always false then, so it never ran and no defaults were seeded), and not again if the user
  // later deletes every folder.
  const defaultsInitializedFor = useRef<string | null>(null)
  useEffect(() => {
    const initializeDefaults = async () => {
      try {
        await patientFilesApi.initializeDefaultFolders(patientId)
        await loadData()
      } catch {
        // Ignore errors - folders might already exist
        console.log("Default folders may already exist")
      }
    }
    if (patientId && !loading && defaultsInitializedFor.current !== patientId) {
      // Arm the ref on the FIRST resolved load for this patient regardless of whether seeding was needed,
      // so a later delete-all never re-creates the defaults (seed truly once per patient).
      defaultsInitializedFor.current = patientId
      if (folders.length === 0) {
        initializeDefaults()
      }
    }
  }, [patientId, loading, folders.length])

  /**
   * ⚠️ Takes a **`File[]`, not the live `FileList`** (AC-77). The picker below clears its own `value` the moment
   * it hands the selection over — which empties the very `FileList` the input exposes — so the array must be a
   * copy taken first, or a retry would upload nothing.
   */
  const handleFileUpload = async (filesToUpload: File[]) => {
    if (filesToUpload.length === 0) return

    setUploading(true)
    try {
      const uploadPromises = filesToUpload.map((file) =>
        patientFilesApi.uploadFile(patientId, file, currentFolderId || undefined)
      )
      await Promise.all(uploadPromises)
      const fileCount = filesToUpload.length
      toast.success(
        fileCount === 1 ? "Fichier téléchargé avec succès" : `${fileCount} fichiers téléchargés avec succès`,
        {
          description: fileCount === 1 
            ? `Le fichier a été ajouté${currentFolder ? ` au dossier "${currentFolder.name}"` : ""}`
            : `Les fichiers ont été ajoutés${currentFolder ? ` au dossier "${currentFolder.name}"` : ""}`,
          duration: 3000,
        }
      )
      await loadData()
    } catch (error) {
      console.error("Failed to upload files:", error)
      const fileCount = filesToUpload.length
      /*
       * The server's OWN reason, not a guess at one.
       *
       * This used to discard the response entirely and always say « vérifiez votre connexion » — so a file the
       * server refused on its allow-list (`FileContentValidation`: PDF, PNG, JPEG only) was reported as a network
       * failure. The user then retried the same DICOM or TIFF from the imaging centre, repeatedly, with the one
       * fact that would have explained it — « ce type de fichier n'est pas accepté » — sitting unread in the
       * response body. `getErrorMessage` reads the canonical `{ error }` and falls back to the connection wording
       * only when there genuinely is no message (an `ApiError(0)`).
       */
      toast.error(
        fileCount === 1 ? "Échec du téléchargement du fichier" : "Échec du téléchargement des fichiers",
        {
          description: getErrorMessage(
            error,
            "Une erreur s'est produite. Veuillez vérifier votre connexion et réessayer.",
          ),
          duration: 5000,
        }
      )
    } finally {
      setUploading(false)
    }
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    void handleFileUpload(Array.from(e.dataTransfer.files))
  }

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }

  const handleDragLeave = () => {
    setIsDragging(false)
  }

  // AC-P3.34 — one folder per double-click. `creatingFolder` guards the handler itself, not just the button,
  // because the Enter key in the name field calls the same function and would otherwise still double-fire.
  // AC-P3.45 — on failure the dialog stays open with the typed name intact, so the user retries rather than
  // retypes.
  const handleCreateFolder = async () => {
    const name = newFolderName.trim()
    if (!name || creatingFolder) return

    try {
      setCreatingFolder(true)
      await patientFilesApi.createFolder(patientId, name, currentFolderId || undefined)
      toast.success("Dossier créé avec succès", {
        description: `Le dossier « ${name} » a été créé${currentFolder ? ` dans « ${currentFolder.name} »` : ""}`,
        duration: 3000,
      })
      setNewFolderName("")
      setIsNewFolderOpen(false)
      await loadData()
    } catch (error) {
      toast.error("Échec de la création du dossier", {
        description: getErrorMessage(error),
        duration: 4000,
      })
    } finally {
      setCreatingFolder(false)
    }
  }

  const handleDeleteFile = (fileId: string) => {
    setPendingDelete({ kind: "file", fileId })
  }

  const performDeleteFile = async (fileId: string) => {
    setDeletingFileId(fileId)
    try {
      const file = files.find(f => f.id === fileId)
      await patientFilesApi.deleteFile(patientId, fileId)
      toast.success("Fichier supprimé avec succès", {
        description: file ? `"${file.fileName}" a été supprimé` : "Le fichier a été supprimé",
        duration: 3000,
      })
      await loadData()
    } catch (error) {
      console.error("Failed to delete file:", error)
      toast.error("Échec de la suppression du fichier", {
        description: "Une erreur s'est produite lors de la suppression. Veuillez réessayer.",
        duration: 4000,
      })
    } finally {
      setDeletingFileId(null)
    }
  }

  const handleDownloadFile = async (file: PatientFileDto) => {
    try {
      const blob = await patientFilesApi.downloadFile(patientId, file.id)
      // One way to deliver a file (AC-4). The hand-rolled `<a download>` this replaced is **ignored** by iOS
      // Safari for a `blob:` URL, so on an iPhone this button silently delivered nothing at all.
      await downloadBlob(blob, file.fileName)
      toast.success("Téléchargement démarré", {
        description: `Le fichier "${file.fileName}" est en cours de téléchargement`,
        duration: 2000,
      })
    } catch (error) {
      console.error("Failed to download file:", error)
      toast.error("Échec du téléchargement", {
        description: `Impossible de télécharger "${file.fileName}". Veuillez réessayer.`,
        duration: 4000,
      })
    }
  }

  const isImageFile = (file: PatientFileDto) => {
    return file.contentType.startsWith("image/")
  }

  const isPdfFile = (file: PatientFileDto) => {
    return file.contentType === "application/pdf" || file.fileName.toLowerCase().endsWith(".pdf")
  }

  const isPreviewableFile = (file: PatientFileDto) => {
    return isImageFile(file) || isPdfFile(file)
  }

  const handlePreviewFile = async (file: PatientFileDto) => {
    try {
      setPreviewLoading(true)
      setPreviewFile(file)
      
      // For previewable files, load the blob for preview
      if (isPreviewableFile(file)) {
        const blob = await patientFilesApi.downloadFile(patientId, file.id)
        const url = window.URL.createObjectURL(blob)
        setPreviewUrl(url)
      } else {
        // For non-previewable files, just show the dialog without loading the file
        setPreviewUrl(null)
      }
    } catch (error) {
      console.error("Failed to preview file:", error)
      toast.error("Échec de l'aperçu du fichier", {
        description: "Impossible de charger l'aperçu. Veuillez réessayer ou télécharger le fichier.",
        duration: 4000,
      })
      setPreviewFile(null)
    } finally {
      setPreviewLoading(false)
    }
  }

  const handleClosePreview = () => {
    if (previewUrl) {
      window.URL.revokeObjectURL(previewUrl)
    }
    setPreviewFile(null)
    setPreviewUrl(null)
    setPreviewLoading(false)
  }

  const handleDeleteFolder = (folderId: string) => {
    setPendingDelete({ kind: "folder", folderId })
  }

  const performDeleteFolder = async (folderId: string) => {
    const folder = folders.find(f => f.id === folderId)
    const hasFiles = folder && folder.fileCount > 0

    try {
      const folderName = folder?.name || "le dossier"
      await patientFilesApi.deleteFolder(patientId, folderId)
      toast.success("Dossier supprimé avec succès", {
        description: hasFiles 
          ? `"${folderName}" et ${folder.fileCount} fichier(s) ont été supprimés`
          : `"${folderName}" a été supprimé`,
        duration: 3000,
      })
      if (currentFolderId === folderId) {
        setCurrentFolderId(null)
      }
      await loadData()
    } catch (error) {
      console.error("Failed to delete folder:", error)
      const errorMessage = error instanceof Error ? error.message : "Une erreur s'est produite"
      toast.error("Échec de la suppression du dossier", {
        description: errorMessage,
        duration: 4000,
      })
    }
  }

  const confirmPendingDelete = async () => {
    if (!pendingDelete) return
    setDeletePending(true)
    try {
      if (pendingDelete.kind === "file") {
        await performDeleteFile(pendingDelete.fileId)
      } else {
        await performDeleteFolder(pendingDelete.folderId)
      }
    } finally {
      setDeletePending(false)
      setPendingDelete(null)
    }
  }

  const getFileIcon = (type: string) => {
    if (type.startsWith("image/")) return <ImageIcon className="h-4 w-4" />
    if (type.includes("pdf")) return <FileText className="h-4 w-4" />
    if (type.includes("zip") || type.includes("rar")) return <FileArchive className="h-4 w-4" />
    return <File className="h-4 w-4" />
  }

  // AC-P3.39 — the file/folder cards are the click target, so they must also be a keyboard target: Enter and
  // Space, a visible focus ring, and an accessible name. Kept as one helper so a card added later cannot be
  // wired half-way.
  const activateOnKey = (action: () => void) => (event: React.KeyboardEvent) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault()
      action()
    }
  }

  const CARD_FOCUS_CLASSES =
    "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"

  if (loading) {
    return (
      <div className="flex items-center justify-center p-8">
        <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-2xl font-bold text-foreground">Fichiers de {patientName}</h2>
          <p className="text-sm text-muted-foreground">Gérez les documents et dossiers du patient</p>
        </div>
        {!currentFolderId && (
          <Button 
            onClick={() => setIsNewFolderOpen(true)} 
            variant="default" 
            size="sm"
            className="bg-primary hover:bg-primary/90 text-white"
          >
            <Plus className="h-4 w-4 mr-2" />
            Nouveau dossier
          </Button>
        )}
      </div>

      {/* Breadcrumb Navigation */}
      <div className="flex items-center gap-2 text-sm text-muted-foreground">
        <Button 
          variant="ghost" 
          size="sm" 
          onClick={() => setCurrentFolderId(null)} 
          className="h-8 px-2 hover:bg-accent hover:text-primary/20"
        >
          <Home className="h-4 w-4 mr-1" />
          Fichiers
        </Button>
        {currentFolder && (
          <>
            <ChevronRight className="h-4 w-4 text-primary" />
            <span className="font-medium text-primary">{currentFolder.name}</span>
          </>
        )}
      </div>

      {/* Upload Area */}
      <Card
        className={cn(
          "border-2 border-dashed p-6 transition-all duration-200",
          /*
             ⚠️ `bg-accent/20`, not `bg-accent/50/20`. Tailwind does not parse a DOUBLE opacity modifier, so it
             emitted no background rule at all — the drop zone's active state has never had the fill this line
             was written to give it, and "you may drop here" was carried by the border alone. A class that
             produces nothing looks exactly like a class that produces something subtle, which is why the typo
             survived; the same slip appears once more in this file (the folder card's gradient below).
          */
          isDragging
            ? "border-primary bg-accent/20 shadow-lg"
            : "border-primary/25 hover:border-primary/40 bg-gradient-to-br from-accent/30 to-transparent/10"
        )}
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
      >
        <div className="flex flex-col items-center justify-center gap-3">
          {uploading ? (
            <div className="p-3 rounded-full bg-accent/30">
              <Loader2 className="h-10 w-10 animate-spin text-primary" />
            </div>
          ) : (
            <div className={cn(
              "p-3 rounded-full transition-colors",
              isDragging ? "bg-accent/30" : "bg-accent/20"
            )}>
              <Upload className={cn("h-10 w-10", isDragging ? "text-primary" : "text-primary")} />
            </div>
          )}
          <div className="text-center">
            <p className={cn(
              "text-sm font-semibold",
              isDragging ? "text-primary" : "text-foreground"
            )}>
              {isDragging ? "Déposez les fichiers ici" : "Glissez-déposez des fichiers ici"}
            </p>
            <p className="text-xs text-muted-foreground mt-1">ou</p>
          </div>
          <label>
            <input
              type="file"
              multiple
              /*
               * Mirrors the server's own allow-list, `FileContentValidation.PatientFileTypes` (AC-56).
               *
               * ⚠️ This input had **no** `accept` at all — the only one of the app's six without one — so the
               * picker offered DICOMs and TIFFs the upload then refused, which is the confusion the error
               * handler above was written to explain. It is deliberately **not** `image/*`: a referral letter
               * or a lab report arrives as a PDF and the server takes it, so narrowing to images would remove
               * a working capability (§ 0). Naming the image types is also what gives the native shell's
               * `onShowFileChooser` something to key the camera intent on.
               */
              accept="application/pdf,image/png,image/jpeg"
              className="hidden"
              onChange={(e) => {
                /*
                 * ⚠️ Copy, then clear the input **before** the upload runs (AC-77).
                 *
                 * Without the clear, the element still holds the file it just handed over, so re-picking the
                 * *same* file fires no `change` event at all and « réessayer » silently does nothing. That is
                 * precisely the case this criterion exists for: an upload killed by the OS backgrounding the
                 * app fails, and the one thing the user then does — pick that photo again — is a no-op.
                 */
                const chosen = Array.from(e.target.files ?? [])
                e.target.value = ""
                void handleFileUpload(chosen)
              }}
              disabled={uploading}
            />
            <Button 
              variant="default" 
              size="sm" 
              asChild 
              disabled={uploading}
              className="bg-primary hover:bg-primary/90 text-white disabled:opacity-50"
            >
              <span>{uploading ? "Téléversement…" : "Parcourir les fichiers"}</span>
            </Button>
          </label>
        </div>
      </Card>

      {/* Folders Grid (only show when in root) */}
      {!currentFolderId && (
        <div>
          <h3 className="text-sm font-semibold mb-3 text-foreground">Dossiers</h3>
          {folders.length === 0 ? (
            <Card className="p-8 border-dashed border-primary/25">
              <div className="text-center text-muted-foreground">
                <div className="p-4 rounded-full bg-accent/20 inline-block mb-3">
                  <Folder className="h-12 w-12 text-primary opacity-70" />
                </div>
                <p className="text-sm">Aucun dossier. Créez un dossier pour organiser les fichiers.</p>
              </div>
            </Card>
          ) : (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              {folders.map((folder) => (
                <Card
                  key={folder.id}
                  role="button"
                  tabIndex={0}
                  aria-label={`Ouvrir le dossier ${folder.name}`}
                  className={cn(
                    // Movement hover gated behind `hover-hover:` (AC-11); the shadow and border tints stay
                    // ungated, per the policy — a lingering tint reads as "selected", a stuck transform reads
                    // as broken.
                    // `to-accent/10` — the second of the two double-modifier typos (`to-accent/30/10` emitted no
                    // gradient stop, so these cards were a flat `from-card` with a `bg-gradient-to-br` that had
                    // nothing to travel to).
                    "p-4 cursor-pointer hover:shadow-md transition-all duration-200 hover-hover:hover:scale-105 border-border hover:border-primary/40 bg-gradient-to-br from-card to-accent/10 relative group",
                    CARD_FOCUS_CLASSES
                  )}
                  onClick={() => setCurrentFolderId(folder.id)}
                  onKeyDown={activateOnKey(() => setCurrentFolderId(folder.id))}
                >
                  <div className="flex flex-col items-center gap-2 text-center">
                    <div className="p-2 rounded-lg bg-accent/30">
                      <Folder className="h-10 w-10 text-primary" />
                    </div>
                    <p className="text-sm font-semibold truncate w-full text-foreground">{folder.name}</p>
                    <p className="text-xs text-muted-foreground">
                      {folder.fileCount} {folder.fileCount === 1 ? "fichier" : "fichiers"}
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    /*
                     * ⚠️ This is the OTHER hover rule (AC-11), and the opposite treatment.
                     *
                     * `opacity-0 group-hover:opacity-100` is not decoration — it is the only way to delete a
                     * folder, and on a touch device there is no hover, so the control was invisible and
                     * un-tappable: the capability simply did not exist on a tablet. Gating it behind
                     * `hover-hover:` — the fix for a *movement* hover — would have made that permanent.
                     *
                     * So it inverts instead: hidden-until-hover only where a pointer can hover, always visible
                     * on a coarse pointer. `features/LEARNINGS.md`: never let a presentation heuristic be the
                     * only gate on a required affordance.
                     */
                    className="absolute top-2 right-2 h-7 w-7 p-0 opacity-100 hover-hover:opacity-0 hover-hover:group-hover:opacity-100 hover-hover:group-focus-within:opacity-100 transition-opacity hover:bg-red-100 dark:hover:bg-red-900/20 hover:text-red-600 dark:hover:text-red-400 z-10"
                    onClick={(e) => {
                      e.stopPropagation()
                      e.preventDefault()
                      handleDeleteFolder(folder.id)
                    }}
                    title="Supprimer le dossier"
                    aria-label={`Supprimer le dossier ${folder.name}`}
                  >
                    <X className="h-4 w-4" />
                  </Button>
                </Card>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Files List */}
      <div>
        <h3 className="text-sm font-semibold mb-3 text-foreground">
          {currentFolderId ? "Fichiers du dossier" : "Fichiers récents"}
        </h3>
        {currentFiles.length === 0 ? (
          <Card className="p-8 border-dashed border-primary/25">
            <div className="text-center text-muted-foreground">
              <div className="p-4 rounded-full bg-accent/20 inline-block mb-3">
                <File className="h-12 w-12 text-primary opacity-70" />
              </div>
              <p className="text-sm font-medium">Aucun fichier. Téléversez des fichiers pour commencer.</p>
            </div>
          </Card>
        ) : (
          <div className="space-y-2">
            {currentFiles.map((file) => (
              <Card
                key={file.id}
                role="button"
                tabIndex={0}
                aria-label={`Ouvrir ${file.fileName}`}
                className={cn(
                  "p-3 hover:shadow-sm transition-all duration-200 hover:border-primary/40 bg-card cursor-pointer",
                  CARD_FOCUS_CLASSES
                )}
                onClick={() => handlePreviewFile(file)}
                onKeyDown={activateOnKey(() => void handlePreviewFile(file))}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3 flex-1 min-w-0">
                    <div className="p-2 rounded-lg bg-accent/30 text-primary">
                      {getFileIcon(file.contentType)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-semibold truncate text-foreground">{file.fileName}</p>
                      <p className="text-xs text-muted-foreground">
                        {formatFileSize(file.fileSize)} • {formatDate(file.uploadedAt)}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-8 w-8 p-0 hover:bg-accent/30 hover:text-primary"
                      onClick={(e) => {
                        e.stopPropagation()
                        isPreviewableFile(file) ? handlePreviewFile(file) : handleDownloadFile(file)
                      }}
                      title={isPreviewableFile(file) ? "Aperçu du fichier" : "Télécharger le fichier"}
                      aria-label={
                        isPreviewableFile(file)
                          ? `Aperçu de ${file.fileName}`
                          : `Télécharger ${file.fileName}`
                      }
                    >
                      {isPreviewableFile(file) ? (
                        <FileText className="h-4 w-4" />
                      ) : (
                        <Download className="h-4 w-4" />
                      )}
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-8 w-8 p-0 text-destructive hover:text-destructive hover:bg-red-100 dark:hover:bg-red-900/20"
                      onClick={() => handleDeleteFile(file.id)}
                      disabled={deletingFileId === file.id}
                      // AC-P3.40 — icon-only, and previously with neither label nor title, while the
                      // download button beside it at least had a title. A screen reader read « button ».
                      title="Supprimer le fichier"
                      aria-label={`Supprimer ${file.fileName}`}
                    >
                      {deletingFileId === file.id ? (
                        <Loader2 className="h-4 w-4 animate-spin" />
                      ) : (
                        <X className="h-4 w-4" />
                      )}
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>

      {/* New Folder Dialog */}
      <Dialog open={isNewFolderOpen} onOpenChange={setIsNewFolderOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Créer un dossier</DialogTitle>
            <DialogDescription>Saisissez un nom pour le nouveau dossier</DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="new-folder-name">Nom du dossier</Label>
            <Input
              id="new-folder-name"
              placeholder="Radiographies"
              value={newFolderName}
              onChange={(e) => setNewFolderName(e.target.value)}
              disabled={creatingFolder}
              onKeyDown={(e) => {
                if (e.key === "Enter") {
                  e.preventDefault()
                  void handleCreateFolder()
                }
              }}
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNewFolderOpen(false)} disabled={creatingFolder}>
              Annuler
            </Button>
            <Button
              onClick={() => void handleCreateFolder()}
              disabled={creatingFolder || !newFolderName.trim()}
              className="bg-primary hover:bg-primary/90 text-white"
            >
              {creatingFolder ? "Création…" : "Créer le dossier"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* File Preview Dialog */}
      <Dialog open={!!previewFile} onOpenChange={handleClosePreview}>
        {/* A preview is the one dialog that wants the whole screen on a phone — it is showing a document.
            ⚠️ The width is a TEMPLATE LITERAL, which the AC-50 check tokenises through the braces, so the
            `md:` prefixes here are as load-bearing as the quoted ones. */}
        <DialogContent
          mobile="sheet"
          className={`${previewFile && isPdfFile(previewFile) ? 'md:max-w-[98vw] md:w-[98vw]' : 'md:max-w-6xl'} p-0 md:max-h-[98dvh]`}
        >
          {previewFile && (
            <>
              <DialogHeader className="px-6 pt-6 pb-4 flex-shrink-0 border-b bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <DialogTitle className="truncate text-lg font-semibold">{previewFile.fileName}</DialogTitle>
                <DialogDescription className="mt-1">
                  {formatFileSize(previewFile.fileSize)} • {formatDate(previewFile.uploadedAt)}
                </DialogDescription>
              </DialogHeader>
              <div className={`relative flex items-start justify-center flex-1 min-h-0 ${previewFile && isPdfFile(previewFile) ? 'bg-slate-100 dark:bg-slate-900 p-6 overflow-auto' : 'bg-black/5 p-6 overflow-auto'}`}>
                {previewLoading ? (
                  <div className="flex flex-col items-center justify-center gap-3 h-full">
                    <Loader2 className="h-8 w-8 animate-spin text-primary" />
                    <p className="text-sm text-muted-foreground">Chargement de l&apos;aperçu…</p>
                  </div>
                ) : previewUrl ? (
                  <>
                    {isImageFile(previewFile) ? (
                      <div className="flex items-center justify-center w-full h-full">
                        <img
                          src={previewUrl}
                          alt={previewFile.fileName}
                          className="max-w-full max-h-full w-auto h-auto object-contain rounded-lg shadow-lg"
                        />
                      </div>
                    ) : isPdfFile(previewFile) ? (
                      <PatientFilePdfPreview
                        previewUrl={previewUrl}
                        fileName={previewFile.fileName}
                        onDeliver={() => handleDownloadFile(previewFile)}
                      />
                    ) : (
                      <div className="flex flex-col items-center gap-3 p-8">
                        <File className="h-16 w-16 text-muted-foreground" />
                        <p className="text-sm text-muted-foreground">Aperçu non disponible pour ce type de fichier</p>
                        <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                          <Download className="h-4 w-4 mr-2" />
                          Télécharger pour consulter
                        </Button>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="flex flex-col items-center gap-3 p-8">
                    <File className="h-16 w-16 text-muted-foreground" />
                    <p className="text-sm text-muted-foreground">Aperçu non disponible pour ce type de fichier</p>
                    <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                      <Download className="h-4 w-4 mr-2" />
                      Télécharger pour consulter
                    </Button>
                  </div>
                )}
              </div>
              <DialogFooter className="px-6 py-4 flex-shrink-0 border-t bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                {/* `flex-wrap` — « Fermer » (min 100px) + « Télécharger » + « Supprimer » is ~364px of French
                    inside a 342px phone sheet, and `buttonVariants` is `whitespace-nowrap`, so nothing could
                    give: the destructive button was pushed past the right edge and simply could not be reached.
                    Wrapping is the fix rather than shrinking, since none of the three labels can be shortened
                    without losing what it does. */}
                <div className="flex w-full flex-wrap items-center justify-between gap-3">
                  <Button variant="outline" onClick={handleClosePreview} className="min-w-[100px]">
                    Fermer
                  </Button>
                  <div className="flex flex-wrap items-center gap-2">
                    <Button variant="outline" onClick={() => handleDownloadFile(previewFile!)} className="gap-2">
                      <Download className="h-4 w-4" />
                      Télécharger
                    </Button>
                    <Button
                      variant="destructive"
                      onClick={() => {
                        handleClosePreview()
                        handleDeleteFile(previewFile.id)
                      }}
                      className="gap-2"
                    >
                      <X className="h-4 w-4" />
                      Supprimer
                    </Button>
                  </div>
                </div>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>

      {/* Destructive-delete confirmation (replaces native confirm() — AC-9). */}
      <AlertDialog open={!!pendingDelete} onOpenChange={(open) => { if (!open) setPendingDelete(null) }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {pendingDelete?.kind === "folder" ? "Supprimer le dossier ?" : "Supprimer le fichier ?"}
            </AlertDialogTitle>
            <AlertDialogDescription>
              {pendingDelete?.kind === "folder"
                ? (() => {
                    const folder = folders.find((f) => f.id === pendingDelete.folderId)
                    const count = folder?.fileCount ?? 0
                    return count > 0
                      ? `Le dossier « ${folder?.name} » contient ${count} fichier(s). Tous les fichiers qu'il contient seront supprimés. Cette action est irréversible.`
                      : `Voulez-vous vraiment supprimer « ${folder?.name} » ? Cette action est irréversible.`
                  })()
                : "Voulez-vous vraiment supprimer ce fichier ? Cette action est irréversible."}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={deletePending}>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={(e) => {
                e.preventDefault()
                void confirmPendingDelete()
              }}
              disabled={deletePending}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              {deletePending ? "Suppression…" : "Supprimer"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
