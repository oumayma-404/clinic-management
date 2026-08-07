"use client"

import { useEffect, useState } from "react"

import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Textarea } from "@/components/ui/textarea"
import { patientFilesApi } from "@/lib/api/patient-files"
import { showErrorToast } from "@/lib/errors"
import type { PatientFileDto, PatientFolderDto } from "@/lib/api/types"
import { toast } from "sonner"

/** Radix `Select` refuses an empty value, so the root folder needs a token of its own. */
const ROOT_VALUE = "__root__"

function splitName(fileName: string): { base: string; extension: string } {
  const dot = fileName.lastIndexOf(".")
  if (dot <= 0 || dot === fileName.length - 1) return { base: fileName, extension: "" }
  return { base: fileName.slice(0, dot), extension: fileName.slice(dot) }
}

/**
 * Rename a file, describe it, move it (AC-4.2/AC-4.6) — the app's first rename affordance for anything.
 *
 * <p>The extension is a **fixed suffix beside the field**, not part of it: the server recomposes the name from
 * the stored extension, so a typed « .jpg » on a PDF cannot change what the file is, and showing it as editable
 * would promise otherwise.</p>
 */
export function RenameFileDialog({
  patientId,
  file,
  folders,
  onOpenChange,
  onSaved,
}: {
  patientId: string
  file: PatientFileDto | null
  folders: PatientFolderDto[]
  onOpenChange: (open: boolean) => void
  onSaved: () => void
}) {
  const [base, setBase] = useState("")
  const [description, setDescription] = useState("")
  const [folderId, setFolderId] = useState<string>(ROOT_VALUE)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    if (!file) return
    setBase(splitName(file.fileName).base)
    setDescription(file.description ?? "")
    setFolderId(file.folderId ?? ROOT_VALUE)
  }, [file])

  const extension = file ? splitName(file.fileName).extension : ""

  const save = async () => {
    if (!file || saving) return
    const trimmed = base.trim()
    if (!trimmed) return

    try {
      setSaving(true)
      await patientFilesApi.updateFile(patientId, file.id, {
        fileName: trimmed,
        description,
        folderId: folderId === ROOT_VALUE ? null : folderId,
      })
      toast.success("Fichier modifié", { description: `« ${trimmed}${extension} » a été enregistré.` })
      onOpenChange(false)
      onSaved()
    } catch (error) {
      // The dialog stays open with the typed name intact — a refusal must be correctable, not retyped.
      showErrorToast(error, "Le fichier n'a pas pu être modifié.")
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={!!file} onOpenChange={onOpenChange}>
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Modifier le fichier</DialogTitle>
          <DialogDescription>
            Le format ne peut pas être changé : l&apos;extension reste celle du fichier envoyé.
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          <div className="space-y-2">
            <Label htmlFor="file-base-name">Nom du fichier</Label>
            <div className="flex items-center gap-2">
              <Input
                id="file-base-name"
                value={base}
                onChange={(event) => setBase(event.target.value)}
                disabled={saving}
                onKeyDown={(event) => {
                  if (event.key === "Enter") {
                    event.preventDefault()
                    void save()
                  }
                }}
              />
              {extension && (
                <span className="shrink-0 text-sm text-muted-foreground" aria-label="Extension du fichier">
                  {extension}
                </span>
              )}
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="file-description">Description</Label>
            <Textarea
              id="file-description"
              value={description}
              onChange={(event) => setDescription(event.target.value)}
              disabled={saving}
              placeholder="Panoramique du 12/03, contrôle post-opératoire…"
              rows={2}
            />
          </div>

          <div className="space-y-2">
            <Label htmlFor="file-folder">Dossier</Label>
            <Select value={folderId} onValueChange={setFolderId} disabled={saving}>
              <SelectTrigger id="file-folder">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value={ROOT_VALUE}>Aucun dossier</SelectItem>
                {folders.map((folder) => (
                  <SelectItem key={folder.id} value={folder.id}>
                    {folder.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={saving}>
            Annuler
          </Button>
          <Button onClick={() => void save()} disabled={saving || !base.trim()}>
            {saving ? "Enregistrement…" : "Enregistrer"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
