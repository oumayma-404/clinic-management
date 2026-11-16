"use client"

import type { DoctorDto } from "@/lib/api/clinics"
import { initialsOf } from "@/components/ui/initials-avatar"
import { personToneClass } from "@/lib/staff-chat/person-tone"
import { cn } from "@/lib/utils"

/**
 * Which dentist an agenda block is for, as two initials on a tint (cabinet-de-groupe D7).
 *
 * ⚠️ **A chip, never the block's colour**: the block's surface and left rail already mean the ACT and its right strip
 * the STATUT, so a third meaning has to be a third channel. Drawn only when the cabinet has two dentists or more —
 * in a one-chair cabinet it would say the same thing on every block.
 */

/**
 * Each practitioner's tint, by order of arrival on the roster — retired ones included, so a dentist leaving repaints
 * nobody and a new one only takes the next colour. The colleague palette of « Messages » (`personToneClass`).
 */
export function doctorTones(allDoctors: readonly DoctorDto[]): Map<string, number> {
  const ranked = allDoctors
    .filter((d): d is DoctorDto & { id: string } => Boolean(d.id))
    .slice()
    .sort((a, b) => (a.createdAt ?? "").localeCompare(b.createdAt ?? "") || a.id.localeCompare(b.id))
  return new Map(ranked.map((d, index) => [d.id, index]))
}

interface DoctorChipProps {
  name: string
  tone: number | undefined
  className?: string
}

/** The chip itself. Decorative: the block's accessible name already carries « avec Dr … ». */
export function DoctorChip({ name, tone, className }: DoctorChipProps) {
  return (
    <span
      aria-hidden="true"
      className={cn(
        "inline-flex shrink-0 items-center rounded-sm px-1 text-2xs font-semibold leading-4 text-foreground",
        personToneClass(tone),
        className,
      )}
    >
      {initialsOf(name)}
    </span>
  )
}
