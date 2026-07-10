"use client"

import type React from "react"
import { useState, useEffect } from "react"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
import { ClinicGuard } from "@/components/clinic-guard"
import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Folder, File, ChevronRight, Home, Loader2, Search, X, Download, FileText, ImageIcon, FileArchive, Upload, Plus } from "lucide-react"
import { patientsApi } from "@/lib/api/patients"
import { patientFilesApi } from "@/lib/api/patient-files"
import type { PatientDto, PatientFileDto, PatientFolderDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"
import { toast } from "sonner"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"

export default function FilesPage() {
  const [patients, setPatients] = useState<PatientDto[]>([])
  const [selectedPatientId, setSelectedPatientId] = useState<string | null>(null)
  const [selectedPatient, setSelectedPatient] = useState<PatientDto | null>(null)
  const [folders, setFolders] = useState<PatientFolderDto[]>([])
  const [files, setFiles] = useState<PatientFileDto[]>([])
  const [currentFolderId, setCurrentFolderId] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingFiles, setLoadingFiles] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [previewFile, setPreviewFile] = useState<PatientFileDto | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [previewLoading, setPreviewLoading] = useState(false)
  const [isDragging, setIsDragging] = useState(false)
  const [isNewFolderOpen, setIsNewFolderOpen] = useState(false)
  const [newFolderName, setNewFolderName] = useState("")
  const [uploading, setUploading] = useState(false)
  const [deletingFileId, setDeletingFileId] = useState<string | null>(null)
  const [patientsRefreshKey, setPatientsRefreshKey] = useState(0)

  // Load all patients
  useEffect(() => {
    const loadPatients = async () => {
      try {
        setLoading(true)
        const patientsData = await patientsApi.list()
        setPatients(patientsData)
      } catch (error) {
        console.error("Failed to load patients:", error)
        toast.error("Failed to load patients", {
          description: "Please try again later",
        })
      } finally {
        setLoading(false)
      }
    }
    loadPatients()
  }, [patientsRefreshKey])

  const loadPatientFiles = async () => {
    if (!selectedPatientId) {
      setFolders([])
      setFiles([])
      setCurrentFolderId(null)
      return
    }

    try {
      setLoadingFiles(true)
      const [foldersData, filesData] = await Promise.all([
        patientFilesApi.getFolders(selectedPatientId, currentFolderId || undefined).catch(() => []),
        patientFilesApi.getFiles(selectedPatientId, currentFolderId || undefined).catch(() => []),
      ])
      setFolders(foldersData)
      setFiles(filesData)
    } catch (error) {
      console.error("Failed to load files:", error)
      toast.error("Failed to load files", {
        description: "Please try again later",
      })
    } finally {
      setLoadingFiles(false)
    }
  }

  // Load files when patient is selected
  useEffect(() => {
    loadPatientFiles()
  }, [selectedPatientId, currentFolderId])

  // Real-time: a file change (upload/delete/folder) reloads the open patient's files; a patient change
  // reloads the patient list. One connection routes both by resource. Undefined (reconnect) → refresh both.
  useClinicRealtime([RealtimeResource.Files, RealtimeResource.Patients], (resource) => {
    if (resource === RealtimeResource.Patients || resource === undefined) {
      setPatientsRefreshKey((k) => k + 1)
    }
    if (resource === RealtimeResource.Files || resource === undefined) {
      loadPatientFiles()
    }
  })

  // Initialize default folders when patient is first selected
  useEffect(() => {
    if (selectedPatientId && folders.length === 0 && !loadingFiles) {
      const initializeDefaults = async () => {
        try {
          await patientFilesApi.initializeDefaultFolders(selectedPatientId)
          await loadPatientFiles()
        } catch (error) {
          // Ignore errors - folders might already exist
          console.log("Default folders may already exist")
        }
      }
      initializeDefaults()
    }
  }, [selectedPatientId])

  const handlePatientClick = (patient: PatientDto) => {
    setSelectedPatientId(patient.id)
    setSelectedPatient(patient)
    setCurrentFolderId(null)
  }

  const handleBackToPatients = () => {
    setSelectedPatientId(null)
    setSelectedPatient(null)
    setCurrentFolderId(null)
  }

  const handleFolderClick = (folderId: string) => {
    setCurrentFolderId(folderId)
  }

  const handleBackToRoot = () => {
    setCurrentFolderId(null)
  }

  const filteredPatients = patients.filter((patient) => {
    if (!searchQuery) return true
    const query = searchQuery.toLowerCase()
    
    // Search by name
    const nameMatch =
      patient.firstName.toLowerCase().includes(query) ||
      patient.lastName.toLowerCase().includes(query) ||
      `${patient.firstName} ${patient.lastName}`.toLowerCase().includes(query)
    
    // Search by date of birth - format in multiple ways for flexibility
    const dob = new Date(patient.dateOfBirth)
    const dobFormats = [
      dob.toLocaleDateString('en-US'), // MM/DD/YYYY
      dob.toLocaleDateString('en-GB'), // DD/MM/YYYY
      dob.toISOString().split('T')[0], // YYYY-MM-DD
      dob.toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' }), // MM/DD/YYYY
      dob.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }), // MMM DD, YYYY
      dob.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' }), // Month DD, YYYY
    ]
    
    const dobMatch = dobFormats.some(format => 
      format.toLowerCase().includes(query)
    )
    
    return nameMatch || dobMatch
  })

  const currentFolder = folders.find((f) => f.id === currentFolderId)
  const currentFiles = files.filter((f) =>
    currentFolderId ? f.folderId === currentFolderId : !f.folderId
  )

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
    if (!selectedPatientId) return

    try {
      setPreviewLoading(true)
      setPreviewFile(file)

      if (isPreviewableFile(file)) {
        const blob = await patientFilesApi.downloadFile(selectedPatientId, file.id)
        const url = window.URL.createObjectURL(blob)
        setPreviewUrl(url)
      } else {
        setPreviewUrl(null)
      }
    } catch (error) {
      console.error("Failed to preview file:", error)
      toast.error("Failed to preview file", {
        description: "Please try again or download the file",
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

  const handleDownloadFile = async (file: PatientFileDto) => {
    if (!selectedPatientId) return

    try {
      const blob = await patientFilesApi.downloadFile(selectedPatientId, file.id)
      const url = window.URL.createObjectURL(blob)
      const a = document.createElement("a")
      a.href = url
      a.download = file.fileName
      document.body.appendChild(a)
      a.click()
      window.URL.revokeObjectURL(url)
      document.body.removeChild(a)
      toast.success("Download started", {
        description: `Downloading "${file.fileName}"`,
      })
    } catch (error) {
      console.error("Failed to download file:", error)
      toast.error("Failed to download", {
        description: `Unable to download "${file.fileName}". Please try again.`,
      })
    }
  }

  const handleFileUpload = async (filesToUpload: FileList | null) => {
    if (!filesToUpload || filesToUpload.length === 0 || !selectedPatientId) return

    setUploading(true)
    try {
      const uploadPromises = Array.from(filesToUpload).map((file) =>
        patientFilesApi.uploadFile(selectedPatientId, file, currentFolderId || undefined)
      )
      await Promise.all(uploadPromises)
      const fileCount = filesToUpload.length
      toast.success(
        fileCount === 1 ? "File uploaded successfully" : `${fileCount} files uploaded successfully`,
        {
          description: fileCount === 1
            ? `The file has been added${currentFolder ? ` to folder "${currentFolder.name}"` : ""}`
            : `The files have been added${currentFolder ? ` to folder "${currentFolder.name}"` : ""}`,
          duration: 3000,
        }
      )
      await loadPatientFiles()
    } catch (error) {
      console.error("Failed to upload files:", error)
      const fileCount = filesToUpload.length
      toast.error(
        fileCount === 1 ? "Failed to upload file" : "Failed to upload files",
        {
          description: "An error occurred. Please check your connection and try again.",
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
    if (!newFolderName.trim() || !selectedPatientId) return

    try {
      await patientFilesApi.createFolder(selectedPatientId, newFolderName.trim(), currentFolderId || undefined)
      toast.success("Folder created successfully", {
        description: `The folder "${newFolderName.trim()}" has been created${currentFolder ? ` in "${currentFolder.name}"` : ""}`,
        duration: 3000,
      })
      setNewFolderName("")
      setIsNewFolderOpen(false)
      await loadPatientFiles()
    } catch (error) {
      console.error("Failed to create folder:", error)
      const errorMessage = error instanceof Error ? error.message : "An error occurred"
      toast.error("Failed to create folder", {
        description: errorMessage,
        duration: 4000,
      })
    }
  }

  const handleDeleteFile = async (fileId: string) => {
    if (!confirm("Are you sure you want to delete this file?") || !selectedPatientId) return

    setDeletingFileId(fileId)
    try {
      const file = files.find(f => f.id === fileId)
      await patientFilesApi.deleteFile(selectedPatientId, fileId)
      toast.success("File deleted successfully", {
        description: file ? `"${file.fileName}" has been deleted` : "The file has been deleted",
        duration: 3000,
      })
      await loadPatientFiles()
    } catch (error) {
      console.error("Failed to delete file:", error)
      toast.error("Failed to delete file", {
        description: "An error occurred during deletion. Please try again.",
        duration: 4000,
      })
    } finally {
      setDeletingFileId(null)
    }
  }

  const handleDeleteFolder = async (folderId: string) => {
    if (!selectedPatientId) return

    const folder = folders.find(f => f.id === folderId)
    const hasFiles = folder && folder.fileCount > 0

    const message = hasFiles
      ? `Are you sure you want to delete "${folder?.name}"? This folder contains ${folder.fileCount} file(s). All files inside will be deleted. This action cannot be undone.`
      : `Are you sure you want to delete "${folder?.name}"? This action cannot be undone.`

    if (!confirm(message)) return

    try {
      const folderName = folder?.name || "the folder"
      await patientFilesApi.deleteFolder(selectedPatientId, folderId)
      toast.success("Folder deleted successfully", {
        description: hasFiles
          ? `"${folderName}" and ${folder.fileCount} file(s) have been deleted`
          : `"${folderName}" has been deleted`,
        duration: 3000,
      })
      if (currentFolderId === folderId) {
        setCurrentFolderId(null)
      }
      await loadPatientFiles()
    } catch (error) {
      console.error("Failed to delete folder:", error)
      const errorMessage = error instanceof Error ? error.message : "An error occurred"
      toast.error("Failed to delete folder", {
        description: errorMessage,
        duration: 4000,
      })
    }
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />

        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />

          <main className="flex-1 overflow-auto p-4">
            <div className="mx-auto max-w-[1400px]">
              {!selectedPatientId ? (
                // Patients List View
                <div className="space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h1 className="text-3xl font-bold text-foreground">Patient Files</h1>
                      <p className="text-sm text-muted-foreground mt-1">
                        Select a patient to view their files
                      </p>
                    </div>
                  </div>

                  {/* Search */}
                  <div className="relative">
                    <Search className="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                    <Input
                      placeholder="Search patients by name or date of birth..."
                      value={searchQuery}
                      onChange={(e) => setSearchQuery(e.target.value)}
                      className="pl-10"
                    />
                    {searchQuery && (
                      <Button
                        variant="ghost"
                        size="sm"
                        className="absolute right-2 top-1/2 transform -translate-y-1/2 h-6 w-6 p-0"
                        onClick={() => setSearchQuery("")}
                      >
                        <X className="h-4 w-4" />
                      </Button>
                    )}
                  </div>

                  {/* Patients Grid */}
                  {loading ? (
                    <div className="flex items-center justify-center p-12">
                      <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
                    </div>
                  ) : filteredPatients.length === 0 ? (
                    <Card className="p-12 border-dashed">
                      <div className="text-center text-muted-foreground">
                        <Folder className="h-16 w-16 mx-auto mb-4 opacity-50" />
                        <p className="text-lg font-medium">
                          {searchQuery ? "No patients found" : "No patients yet"}
                        </p>
                        <p className="text-sm mt-2">
                          {searchQuery
                            ? "Try a different search term"
                            : "Patients will appear here once they are added to the system"}
                        </p>
                      </div>
                    </Card>
                  ) : (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
                      {filteredPatients.map((patient) => (
                        <Card
                          key={patient.id}
                          className="p-4 cursor-pointer hover:shadow-md transition-all duration-200 hover:scale-105 border-border hover:border-primary bg-gradient-to-br from-card to-primary/5 relative group"
                          onClick={() => handlePatientClick(patient)}
                        >
                          <div className="flex flex-col items-center gap-3 text-center">
                            <div className="p-3 rounded-lg bg-primary/10">
                              <Folder className="h-12 w-12 text-primary" />
                            </div>
                            <div className="flex-1 min-w-0 w-full">
                              <p className="text-base font-semibold truncate text-foreground">
                                {patient.firstName} {patient.lastName}
                              </p>
                              <p className="text-xs text-muted-foreground mt-1">
                                {new Date(patient.dateOfBirth).toLocaleDateString()}
                              </p>
                            </div>
                            <ChevronRight className="h-5 w-5 text-muted-foreground group-hover:text-primary transition-colors" />
                          </div>
                        </Card>
                      ))}
                    </div>
                  )}
                </div>
              ) : (
                // Patient Files View
                <div className="space-y-4">
                  {/* Header with Breadcrumb */}
                  <div className="flex items-center justify-between">
                    <div>
                      <div className="flex items-center gap-2 text-sm text-muted-foreground mb-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={handleBackToPatients}
                          className="h-8 px-2 hover:bg-primary/10 hover:text-primary"
                        >
                          <Home className="h-4 w-4 mr-1" />
                          All Patients
                        </Button>
                        <ChevronRight className="h-4 w-4" />
                        <span className="font-medium text-foreground">
                          {selectedPatient?.firstName} {selectedPatient?.lastName}
                        </span>
                        {currentFolder && (
                          <>
                            <ChevronRight className="h-4 w-4" />
                            <span className="font-medium text-primary">{currentFolder.name}</span>
                          </>
                        )}
                      </div>
                    </div>
                    {!currentFolderId && (
                      <Button
                        onClick={() => setIsNewFolderOpen(true)}
                        variant="default"
                        size="sm"
                        className="bg-primary hover:bg-primary/90 text-white"
                      >
                        <Plus className="h-4 w-4 mr-2" />
                        New Folder
                      </Button>
                    )}
                  </div>

                  {loadingFiles ? (
                    <div className="flex items-center justify-center p-12">
                      <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
                    </div>
                  ) : (
                    <>

                      {/* Upload Area */}
                      <Card
                        className={cn(
                          "border-2 border-dashed p-6 transition-all duration-200",
                          isDragging
                            ? "border-primary bg-primary/10 shadow-lg"
                            : "border-primary/30 hover:border-primary/50 bg-gradient-to-br from-primary/5 to-transparent"
                        )}
                        onDrop={handleDrop}
                        onDragOver={handleDragOver}
                        onDragLeave={handleDragLeave}
                      >
                        <div className="flex flex-col items-center justify-center gap-3">
                          {uploading ? (
                            <div className="p-3 rounded-full bg-primary/10">
                              <Loader2 className="h-10 w-10 animate-spin text-primary" />
                            </div>
                          ) : (
                            <div className={cn(
                              "p-3 rounded-full transition-colors",
                              isDragging ? "bg-primary/10" : "bg-primary/5"
                            )}>
                              <Upload className={cn("h-10 w-10", isDragging ? "text-primary" : "text-primary/70")} />
                            </div>
                          )}
                          <div className="text-center">
                            <p className={cn(
                              "text-sm font-semibold",
                              isDragging ? "text-primary" : "text-foreground"
                            )}>
                              {isDragging ? "Drop files here" : "Drag and drop files here"}
                            </p>
                            <p className="text-xs text-muted-foreground mt-1">or</p>
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
                              className="bg-primary hover:bg-primary/90 text-white disabled:opacity-50"
                            >
                              <span>{uploading ? "Uploading..." : "Browse Files"}</span>
                            </Button>
                          </label>
                        </div>
                      </Card>

                      {/* Folders Grid (only show when in root) */}
                      {!currentFolderId && folders.length > 0 && (
                        <div>
                          <h3 className="text-sm font-semibold mb-3 text-foreground">Folders</h3>
                          <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                            {folders.map((folder) => (
                              <Card
                                key={folder.id}
                                className="p-4 cursor-pointer hover:shadow-md transition-all duration-200 hover:scale-105 border-border hover:border-primary bg-gradient-to-br from-card to-primary/5 relative group"
                                onClick={() => handleFolderClick(folder.id)}
                              >
                                <div className="flex flex-col items-center gap-2 text-center">
                                  <div className="p-2 rounded-lg bg-primary/10">
                                    <Folder className="h-10 w-10 text-primary" />
                                  </div>
                                  <p className="text-sm font-semibold truncate w-full text-foreground">
                                    {folder.name}
                                  </p>
                                  <p className="text-xs text-muted-foreground">
                                    {folder.fileCount} {folder.fileCount === 1 ? "file" : "files"}
                                  </p>
                                </div>
                                <Button
                                  variant="ghost"
                                  size="sm"
                                  className="absolute top-2 right-2 h-7 w-7 p-0 opacity-0 group-hover:opacity-100 transition-opacity hover:bg-destructive/10 hover:text-destructive z-10"
                                  onClick={(e) => {
                                    e.stopPropagation()
                                    e.preventDefault()
                                    handleDeleteFolder(folder.id)
                                  }}
                                  title="Delete folder"
                                >
                                  <X className="h-4 w-4" />
                                </Button>
                              </Card>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Files List */}
                      <div>
                        <div className="flex items-center justify-between mb-3">
                          <h3 className="text-sm font-semibold text-foreground">
                            {currentFolderId ? "Files in this folder" : "Files"}
                          </h3>
                          {currentFolderId && (
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={handleBackToRoot}
                              className="gap-2"
                            >
                              <Home className="h-4 w-4" />
                              Back to root
                            </Button>
                          )}
                        </div>
                        {currentFiles.length === 0 ? (
                          <Card className="p-8 border-dashed border-primary/20">
                            <div className="text-center text-muted-foreground">
                              <div className="p-4 rounded-full bg-primary/10 inline-block mb-3">
                                <File className="h-12 w-12 text-primary opacity-70" />
                              </div>
                              <p className="text-sm font-medium">No files yet</p>
                              <p className="text-xs mt-1">
                                Files will appear here once they are uploaded
                              </p>
                            </div>
                          </Card>
                        ) : (
                          <div className="space-y-2">
                            {currentFiles.map((file) => (
                              <Card
                                key={file.id}
                                className={cn(
                                  "p-3 hover:shadow-sm transition-all duration-200 hover:border-primary bg-card cursor-pointer"
                                )}
                                onClick={() => handlePreviewFile(file)}
                              >
                                <div className="flex items-center justify-between">
                                  <div className="flex items-center gap-3 flex-1 min-w-0">
                                    <div className="p-2 rounded-lg bg-primary/10 text-primary">
                                      {getFileIcon(file.contentType)}
                                    </div>
                                    <div className="flex-1 min-w-0">
                                      <p className="text-sm font-semibold truncate text-foreground">
                                        {file.fileName}
                                      </p>
                                      <p className="text-xs text-muted-foreground">
                                        {formatFileSize(file.fileSize)} •{" "}
                                        {new Date(file.uploadedAt).toLocaleDateString()}
                                      </p>
                                    </div>
                                  </div>
                                  <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                                    <Button
                                      variant="ghost"
                                      size="sm"
                                      className="h-8 w-8 p-0 hover:bg-primary/10 hover:text-primary"
                                      onClick={(e) => {
                                        e.stopPropagation()
                                        isPreviewableFile(file)
                                          ? handlePreviewFile(file)
                                          : handleDownloadFile(file)
                                      }}
                                      title={isPreviewableFile(file) ? "Preview file" : "Download file"}
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
                                      className="h-8 w-8 p-0 text-destructive hover:text-destructive hover:bg-destructive/10"
                                      onClick={(e) => {
                                        e.stopPropagation()
                                        handleDeleteFile(file.id)
                                      }}
                                      disabled={deletingFileId === file.id}
                                      title="Delete file"
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
                    </>
                  )}
                </div>
              )}
            </div>
          </main>
        </div>
      </div>

      {/* File Preview Dialog */}
      <Dialog open={!!previewFile} onOpenChange={handleClosePreview}>
        <DialogContent
          className={`${
            previewFile && isPdfFile(previewFile) ? "max-w-[98vw] w-[98vw]" : "max-w-6xl"
          } max-h-[98vh] p-0 flex flex-col`}
        >
          {previewFile && (
            <>
              <DialogHeader className="px-6 pt-6 pb-4 flex-shrink-0 border-b bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <DialogTitle className="truncate text-lg font-semibold">
                  {previewFile.fileName}
                </DialogTitle>
                <DialogDescription className="mt-1">
                  {formatFileSize(previewFile.fileSize)} •{" "}
                  {new Date(previewFile.uploadedAt).toLocaleDateString()}
                </DialogDescription>
              </DialogHeader>
              <div
                className={`relative flex items-start justify-center flex-1 min-h-0 ${
                  previewFile && isPdfFile(previewFile)
                    ? "bg-slate-100 dark:bg-slate-900 p-6 overflow-auto"
                    : "bg-black/5 p-6 overflow-auto"
                }`}
              >
                {previewLoading ? (
                  <div className="flex flex-col items-center justify-center gap-3 h-full">
                    <Loader2 className="h-8 w-8 animate-spin text-primary" />
                    <p className="text-sm text-muted-foreground">Loading preview...</p>
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
                        <div
                          className="bg-white dark:bg-slate-800 shadow-2xl rounded-lg overflow-hidden"
                          style={{
                            width: "100%",
                            maxWidth: "calc(100vw - 8rem)",
                            aspectRatio: "210 / 297",
                          }}
                        >
                          <iframe
                            src={`${previewUrl}#toolbar=0&navpanes=0&scrollbar=1`}
                            className="w-full h-full"
                            style={{
                              border: "none",
                              display: "block",
                              aspectRatio: "210 / 297",
                            }}
                            title={previewFile.fileName}
                          />
                        </div>
                      </div>
                    ) : (
                      <div className="flex flex-col items-center gap-3 p-8">
                        <File className="h-16 w-16 text-muted-foreground" />
                        <p className="text-sm text-muted-foreground">
                          Preview not available for this file type
                        </p>
                        <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                          <Download className="h-4 w-4 mr-2" />
                          Download to view
                        </Button>
                      </div>
                    )}
                  </>
                ) : (
                  <div className="flex flex-col items-center gap-3 p-8">
                    <File className="h-16 w-16 text-muted-foreground" />
                    <p className="text-sm text-muted-foreground">
                      Preview not available for this file type
                    </p>
                    <Button variant="outline" onClick={() => handleDownloadFile(previewFile)}>
                      <Download className="h-4 w-4 mr-2" />
                      Download to view
                    </Button>
                  </div>
                )}
              </div>
              <DialogFooter className="px-6 py-4 flex-shrink-0 border-t bg-gradient-to-r from-slate-50 to-slate-100 dark:from-slate-900 dark:to-slate-800">
                <div className="flex items-center gap-3 w-full justify-between">
                  <Button variant="outline" onClick={handleClosePreview} className="min-w-[100px]">
                    Close
                  </Button>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      onClick={() => handleDownloadFile(previewFile!)}
                      className="gap-2"
                    >
                      <Download className="h-4 w-4" />
                      Download
                    </Button>
                    <Button
                      variant="destructive"
                      onClick={() => {
                        handleClosePreview()
                        handleDeleteFile(previewFile!.id)
                      }}
                      className="gap-2"
                    >
                      <X className="h-4 w-4" />
                      Delete
                    </Button>
                  </div>
                </div>
              </DialogFooter>
            </>
          )}
        </DialogContent>
      </Dialog>

      {/* New Folder Dialog */}
      <Dialog open={isNewFolderOpen} onOpenChange={setIsNewFolderOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Create New Folder</DialogTitle>
            <DialogDescription>Enter a name for the new folder</DialogDescription>
          </DialogHeader>
          <Input
            placeholder="Folder name"
            value={newFolderName}
            onChange={(e) => setNewFolderName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") handleCreateFolder()
            }}
          />
          <DialogFooter>
            <Button variant="outline" onClick={() => setIsNewFolderOpen(false)}>
              Cancel
            </Button>
            <Button
              onClick={handleCreateFolder}
              className="bg-primary hover:bg-primary/90 text-white"
            >
              Create Folder
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </ClinicGuard>
  )
}

