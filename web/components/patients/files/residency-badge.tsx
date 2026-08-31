import { HardDrive } from "lucide-react"

import type { PatientFileDto } from "@/lib/api/types"
import { cn } from "@/lib/utils"

/**
 * Says that a file's original is kept at the cabinet rather than on the server (`clinic-file-vault`).
 *
 * ⚠️ **Nothing is rendered for a hosted file**, which is almost all of them. A badge on every row would be noise
 * carrying no decision; this one exists because « conservé au cabinet » changes what the reader can expect from
 * the machine they are sitting at.
 *
 * ⚠️ **It states residency, not availability.** Whether *this* machine can open the original is a question about
 * a folder handle and a size check, and answering it per row would mean a disk read for every card on every
 * render. The open action answers it, at the moment somebody asks.
 */
export function FileResidencyBadge({ file, className }: { file: PatientFileDto; className?: string }) {
  if (file.residency !== "Vault") return null

  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center gap-1 rounded-full border border-border bg-muted/60 px-1.5 py-0.5",
        "text-2xs font-medium text-muted-foreground",
        className,
      )}
      // The visible text is already the whole message; the title carries the consequence a badge cannot hold.
      title="L'original est conservé au cabinet et n'a jamais été transmis au serveur."
    >
      <HardDrive className="h-3 w-3" aria-hidden="true" />
      Au cabinet
    </span>
  )
}
