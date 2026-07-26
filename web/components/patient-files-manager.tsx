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

  const handleFileUpload = async (filesToUpload: FileList | null) => {
    if (!filesToUpload || filesToUpload.length === 0) return

    setUploading(true)
    try {
      const uploadPromises = Array.from(filesToUpload).map((file) =>
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
      toast.error(
        fileCount === 1 ? "Échec du téléchargement du fichier" : "Échec du téléchargement des fichiers",
        {
          description: "Une erreur s'est produite. Veuillez vérifier votre connexion et réessayer.",
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
    handleFileUpload(e.dataTransfer.files)
  }

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }

  const handleDragLeave = () => {
    setIsDragging(false)
  }

  const handleCreateFolder = async () => {
    if (!newFolderName.trim()) return

    try {
      await patientFilesApi.createFolder(patientId, newFolderName.trim(), currentFolderId || undefined)
      toast.success("Dossier créé avec succès", {
        description: `Le dossier "${newFolderName.trim()}" a été créé${currentFolder ? ` dans "${currentFolder.name}"` : ""}`,
        duration: 3000,
      })
      setNewFolderName("")
      setIsNewFolderOpen(false)
      await loadData()
    } catch (error) {
      console.error("Failed to create folder:", error)
      const errorMessage = error instanceof Error ? error.message : "Une erreur s'est produite"
      toast.error("Échec de la création du dossier", {
        description: errorMessage,
        duration: 4000,
      })
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
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      a.download = file.fileName
      document.body.appendChild(a)
      a.click()
      window.URL.revokeObjectURL(url)
      document.body.removeChild(a)
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

  const formatFileSize = (bytes: number) => {
    if (bytes < 1024) return bytes + " B"
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB"
    return (bytes / (1024 * 1024)).toFixed(1) + " MB"
  }

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
            className="bg-blue-600 hover:bg-blue-700 text-white"
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
          className="h-8 px-2 hover:bg-blue-50 hover:text-blue-700 dark:hover:bg-blue-950/20 dark:hover:text-blue-400"
        >
          <Home className="h-4 w-4 mr-1" />
          Fichiers
        </Button>
        {currentFolder && (
          <>
            <ChevronRight className="h-4 w-4 text-blue-500" />
            <span className="font-medium text-blue-700 dark:text-blue-400">{currentFolder.name}</span>
          </>
        )}
      </div>

      {/* Upload Area */}
      <Card
        className={cn(
          "border-2 border-dashed p-6 transition-all duration-200",
          isDragging 
            ? "border-blue-500 bg-blue-50/50 dark:bg-blue-950/20 shadow-lg" 
            : "border-blue-200 dark:border-blue-800 hover:border-blue-300 dark:hover:border-blue-700 bg-gradient-to-br from-blue-50/30 to-transparent dark:from-blue-950/10"
        )}
        onDrop={handleDrop}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
      >
        <div className="flex flex-col items-center justify-center gap-3">
          {uploading ? (
            <div className="p-3 rounded-full bg-blue-100 dark:bg-blue-900/30">
              <Loader2 className="h-10 w-10 animate-spin text-blue-600 dark:text-blue-400" />
            </div>
          ) : (
            <div className={cn(
              "p-3 rounded-full transition-colors",
              isDragging ? "bg-blue-100 dark:bg-blue-900/30" : "bg-blue-50 dark:bg-blue-950/20"
            )}>
              <Upload className={cn("h-10 w-10", isDragging ? "text-blue-600 dark:text-blue-400" : "text-blue-500 dark:text-blue-400")} />
            </div>
          )}
          <div className="text-center">
            <p className={cn(
              "text-sm font-semibold",
              isDragging ? "text-blue-700 dark:text-blue-300" : "text-foreground"
            )}>
              {isDragging ? "Déposez les fichiers ici" : "Glissez-déposez des fichiers ici"}
            </p>
            <p className="text-xs text-muted-foreground mt-1">ou</p>
          </div>
          <label>
            <input
              type="file"
              multiple
              className="hidden"
              onChange={(e) => handleFileUpload(e.target.files)}
              disabled={uploading}
            />
            <Button 
              variant="default" 
              size="sm" 
              asChild 
              disabled={uploading}
              className="bg-blue-600 hover:bg-blue-700 text-white disabled:opacity-50"
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
            <Card className="p-8 border-dashed border-blue-200 dark:border-blue-800">
              <div className="text-center text-muted-foreground">
                <div className="p-4 rounded-full bg-blue-50 dark:bg-blue-950/20 inline-block mb-3">
                  <Folder className="h-12 w-12 text-blue-500 dark:text-blue-400 opacity-70" />
                </div>
                <p className="text-sm">Aucun dossier. Créez un dossier pour organiser les fichiers.</p>
              </div>
            </Card>
          ) : (
            <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
              {folders.map((folder) => (
                <Card
                  key={folder.id}
                  className="p-4 cursor-pointer hover:shadow-md transition-all duration-200 hover:scale-105 border-border hover:border-blue-300 dark:hover:border-blue-700 bg-gradient-to-br from-card to-blue-50/30 dark:to-blue-950/10 relative group"
                  onClick={() => setCurrentFolderId(folder.id)}
                >
                  <div className="flex flex-col items-center gap-2 text-center">
                    <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-900/30">
                      <Folder className="h-10 w-10 text-blue-600 dark:text-blue-400" />
                    </div>
                    <p className="text-sm font-semibold truncate w-full text-foreground">{folder.name}</p>
                    <p className="text-xs text-muted-foreground">
                      {folder.fileCount} {folder.fileCount === 1 ? "file" : "files"}
                    </p>
                  </div>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="absolute top-2 right-2 h-7 w-7 p-0 opacity-0 group-hover:opacity-100 transition-opacity hover:bg-red-100 dark:hover:bg-red-900/20 hover:text-red-600 dark:hover:text-red-400 z-10"
                    onClick={(e) => {
                      e.stopPropagation()
                      e.preventDefault()
                      handleDeleteFolder(folder.id)
                    }}
                    title="Supprimer le dossier"
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
          <Card className="p-8 border-dashed border-blue-200 dark:border-blue-800">
            <div className="text-center text-muted-foreground">
              <div className="p-4 rounded-full bg-blue-50 dark:bg-blue-950/20 inline-block mb-3">
                <File className="h-12 w-12 text-blue-500 dark:text-blue-400 opacity-70" />
              </div>
              <p className="text-sm font-medium">Aucun fichier. Téléversez des fichiers pour commencer.</p>
            </div>
          </Card>
        ) : (
          <div className="space-y-2">
            {currentFiles.map((file) => (
              <Card
                key={file.id}
                className={cn(
                  "p-3 hover:shadow-sm transition-all duration-200 hover:border-blue-300 dark:hover:border-blue-700 bg-card cursor-pointer"
                )}
                onClick={() => handlePreviewFile(file)}
              >
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-3 flex-1 min-w-0">
                    <div className="p-2 rounded-lg bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400">
                      {getFileIcon(file.contentType)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-semibold truncate text-foreground">{file.fileName}</p>
                      <p className="text-xs text-muted-foreground">
                        {formatFileSize(file.fileSize)} • {new Date(file.uploadedAt).toLocaleDateString("fr-FR")}
                      </p>
                    </div>
                  </div>
                  <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                    <Button
                      variant="ghost"
                      size="sm"
                      className="h-8 w-8 p-0 hover:bg-blue-100 dark:hover:bg-blue-900/30 hover:text-blue-700 dark:hover:text-blue-400"
                      onClick={(e) => {
                        e.stopPropagation()
                        isPreviewableFile(file) ? handlePreviewFile(file) : handleDownloadFile(file)
                      }}
                      title={isPreviewableFile(file) ? "Aperçu du fichier" : "Télécharger le fichier"}
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
          <Input
            placeholder="Nom du dossier"
            value={newFolderName}
            onChange={(e) => setNewFolderName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") handleCreateFolder()
            }}
          />
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNewFolderOpen(false)}>
              Annuler
            </Button>
            <Button 
              onClick={handleCreateFolder}
              className="bg-blue-600 hover:bg-blue-700 text-white"
            >
              Créer le dossier
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      {/* File Preview Dialog */}
      <Dialog open={!!previewFile} onOpenChange={handleClosePreview}>
        <DialogContent className={`${previewFile && isPdfFile(previewFile) ? 'max-w-[98vw] w-[98vw]' : 'max-w-6xl'} max-h-[98vh] p-0 flex flex-col`}>
          {previewFile && (
            <>
              <DialogHeader className="px-6 pt-6 pb-4 flex-shrink-0 border-b bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <DialogTitle className="truncate text-lg font-semibold">{previewFile.fileName}</DialogTitle>
                <DialogDescription className="mt-1">
                  {formatFileSize(previewFile.fileSize)} • {new Date(previewFile.uploadedAt).toLocaleDateString("fr-FR")}
                </DialogDescription>
              </DialogHeader>
              <div className={`relative flex items-start justify-center flex-1 min-h-0 ${previewFile && isPdfFile(previewFile) ? 'bg-slate-100 dark:bg-slate-900 p-6 overflow-auto' : 'bg-black/5 p-6 overflow-auto'}`}>
                {previewLoading ? (
                  <div className="flex flex-col items-center justify-center gap-3 h-full">
                    <Loader2 className="h-8 w-8 animate-spin text-blue-600" />
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
                      <div className="w-full flex items-start justify-center min-h-full">
                        <div className="bg-white dark:bg-slate-800 shadow-2xl rounded-lg overflow-hidden" style={{ 
                          width: '100%', 
                          maxWidth: 'calc(100vw - 8rem)',
                          aspectRatio: '210 / 297'
                        }}>
                          <iframe
                            src={`${previewUrl}#toolbar=0&navpanes=0&scrollbar=1`}
                            className="w-full h-full"
                            style={{ 
                              border: 'none',
                              display: 'block',
                              aspectRatio: '210 / 297'
                            }}
                            title={previewFile.fileName}
                          />
                        </div>
                      </div>
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
                <div className="flex items-center gap-3 w-full justify-between">
                  <Button variant="outline" onClick={handleClosePreview} className="min-w-[100px]">
                    Fermer
                  </Button>
                  <div className="flex items-center gap-2">
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
