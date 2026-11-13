"use client"

import { useRef, useState, type DragEvent } from "react"
import { Camera, FolderOpen, Images } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useDemo } from "@/lib/demo/demo-context"
import { refuseInDemo } from "@/lib/demo/refuse-in-demo"
import { showErrorToast } from "@/lib/errors"
import { startImport } from "@/lib/photo-import/session"
import { batchOfDrop, batchOfFiles, canPickFolder, pickFolder, type SourceBatch } from "@/lib/photo-import/sources"
import { cn } from "@/lib/utils"

/**
 * Where the photos come from: the card's folder, photos picked one by one (a phone's gallery included), or a drop.
 *
 * ⚠️ **The folder button is the first choice wherever it exists** — a card holds a `DCIM` tree, and picking its root
 * once is the whole gesture. Where the browser cannot open a folder for reading (Firefox, Safari), the same button
 * falls back to the `webkitdirectory` picker; on a phone, « Choisir des photos » opens the gallery.
 */
export function SourceStep() {
  const { isDemo } = useDemo()
  const filesInput = useRef<HTMLInputElement>(null)
  const folderInput = useRef<HTMLInputElement>(null)
  const [dragging, setDragging] = useState(false)

  const begin = (batch: SourceBatch | null) => {
    if (batch) void startImport(batch)
  }

  const chooseFolder = async () => {
    if (refuseInDemo(isDemo)) return
    if (!canPickFolder()) {
      folderInput.current?.click()
      return
    }
    try {
      begin(await pickFolder())
    } catch (error) {
      showErrorToast(error)
    }
  }

  const chooseFiles = () => {
    if (refuseInDemo(isDemo)) return
    filesInput.current?.click()
  }

  const fromInput = (input: HTMLInputElement) => {
    if (refuseInDemo(isDemo)) return
    // Copied before the value is cleared — clearing empties the live FileList — and cleared so the same card can
    // be chosen again after a failure.
    const files = Array.from(input.files ?? [])
    input.value = ""
    if (files.length > 0) begin(batchOfFiles(files))
  }

  const onDrop = async (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault()
    setDragging(false)
    if (refuseInDemo(isDemo)) return
    try {
      // `dataTransfer.files` alone cannot see into a dropped folder; the entries can, and must be taken now.
      begin(event.dataTransfer.items?.length ? await batchOfDrop(event.dataTransfer.items) : batchOfFiles(Array.from(event.dataTransfer.files)))
    } catch (error) {
      showErrorToast(error)
    }
  }

  return (
    <div
      onDragOver={(event) => {
        event.preventDefault()
        setDragging(true)
      }}
      onDragLeave={() => setDragging(false)}
      onDrop={onDrop}
      className={cn(
        "flex flex-col items-center gap-5 rounded-xl border-2 border-dashed border-border bg-card px-4 py-10 text-center sm:px-8",
        dragging && "border-primary bg-primary/5",
      )}
    >
      <span aria-hidden="true" className="flex size-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
        <Camera className="size-7" strokeWidth={1.75} />
      </span>
      <div className="max-w-md space-y-1.5">
        <h2 className="text-lg font-semibold">Mettez la carte de l&apos;appareil photo dans le lecteur</h2>
        <p className="text-sm text-muted-foreground">
          Les photos sont triées par rendez-vous. Rien n&apos;est rangé avant « Ranger », et rien n&apos;est effacé de
          la carte.
        </p>
      </div>

      <div className="flex w-full max-w-md flex-col gap-2 sm:flex-row sm:justify-center">
        <Button type="button" className="gap-2 coarse:min-h-11" onClick={() => void chooseFolder()}>
          <FolderOpen className="size-4" aria-hidden="true" />
          Choisir le dossier de la carte
        </Button>
        <Button type="button" variant="outline" className="gap-2 coarse:min-h-11" onClick={chooseFiles}>
          <Images className="size-4" aria-hidden="true" />
          Choisir des photos
        </Button>
      </div>
      <p className="hidden text-xs text-muted-foreground md:block">ou glissez le dossier ici</p>

      <input
        ref={filesInput}
        type="file"
        multiple
        accept="image/jpeg,.jpg,.jpeg"
        className="sr-only"
        tabIndex={-1}
        aria-hidden="true"
        onChange={(event) => fromInput(event.currentTarget)}
      />
      <input
        ref={folderInput}
        type="file"
        multiple
        className="sr-only"
        tabIndex={-1}
        aria-hidden="true"
        // A folder picker where `showDirectoryPicker` is missing; React passes the attribute through untouched.
        {...{ webkitdirectory: "" }}
        onChange={(event) => fromInput(event.currentTarget)}
      />
    </div>
  )
}
