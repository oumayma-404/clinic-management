"use client"

import { useEffect, useState } from "react"
import { CalendarClock, FileText, Scissors, Undo2, User } from "lucide-react"
import { Button } from "@/components/ui/button"
import { PatientNameLink } from "@/components/patient-name-link"
import { PatientSearchList } from "@/components/patients/patient-search-list"
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone"
import { orientationTransform } from "@/lib/photo-import/exif"
import type { MatchCandidate, MatchReason } from "@/lib/photo-import/match"
import {
  choosePatient,
  correctedTime,
  splitGroup,
  toggleExcluded,
  toggleIgnored,
  type ImportPhoto,
  type PatientRef,
  type ReviewGroup,
} from "@/lib/photo-import/session"
import { formatWallClockDay, formatWallClockTime, type WallClock } from "@/lib/photo-import/wall-clock"
import { cn } from "@/lib/utils"

/** Why a group asks — one plain sentence, shown above its candidates. */
const ASK_REASON: Partial<Record<MatchReason, string>> = {
  overlap: "Deux patients étaient au fauteuil à cette heure-là.",
  straddle: "Ces photos chevauchent deux rendez-vous.",
  order: "L'ordre de ces photos ne suit pas l'agenda.",
  near: "Aucun rendez-vous à cette heure-là.",
  none: "Aucun rendez-vous ce jour-là.",
  "no-date": "Pas d'heure dans la photo.",
}

/** Thumbnails shown before « +N » — a long burst is still one glance. */
const THUMBS_SHOWN = 12

interface BurstCardProps {
  group: ReviewGroup
  photos: Map<string, ImportPhoto>
  patient: PatientRef | null
  /** True when the patient is the user's pick rather than the matcher's. */
  chosen: boolean
  ignored: boolean
  excluded: Record<string, true>
  /** The import spans several days, so each group names its own. */
  showDay: boolean
  /** Practitioner names, only when the cabinet has more than one. */
  doctorNames: Map<string, string> | null
  /** The camera's name, only when the import holds more than one camera. */
  cameraLabel: string | null
}

function timeRange(start: WallClock | null, end: WallClock | null): string {
  if (start === null) return "Heure inconnue"
  const from = formatWallClockTime(start)
  const to = end !== null ? formatWallClockTime(end) : from
  return from === to ? from : `${from} – ${to}`
}

function candidateLine(candidate: MatchCandidate, doctorNames: Map<string, string> | null): string {
  const when =
    candidate.kind === "visit"
      ? `RDV ${formatWallClockTime(candidate.shownAt)}`
      : `fiche sans RDV à ${formatWallClockTime(candidate.shownAt)}`
  const doctor = candidate.doctorId && doctorNames?.get(candidate.doctorId)
  return doctor ? `${when} · ${doctor}` : when
}

/**
 * One burst of photos and the patient it goes to (`features/camera-photo-import`). A card at every width — a group is a
 * patient, a time and a strip of pictures, which no table column can hold.
 *
 * ⚠️ **The thumbnails are the last line against a wrong clock**: the matcher can be sure of the wrong visit when a
 * camera is off by exactly one slot, and the faces beside the name are what a dentist checks. Tapping one leaves it
 * on the card and out of « Ranger ».
 */
export function BurstCard({
  group,
  photos,
  patient,
  chosen,
  ignored,
  excluded,
  showDay,
  doctorNames,
  cameraLabel,
}: BurstCardProps) {
  const unresolved = !ignored && !patient
  const [choosing, setChoosing] = useState(unresolved)
  const [searching, setSearching] = useState(false)
  const [showAll, setShowAll] = useState(false)

  // A group that needs a patient opens its chooser; one that just got one closes it. Keyed on the id: the patient
  // object is rebuilt on every render, and depending on it would shut the chooser the moment « Changer » opened it.
  const patientId = patient?.patientId ?? null
  useEffect(() => {
    setChoosing(!ignored && patientId === null)
    if (patientId !== null) setSearching(false)
  }, [ignored, patientId])

  const kept = group.photoIds.filter((id) => !excluded[id]).length
  const tone: StatusTone = ignored ? "neutral" : !patient ? "active" : chosen ? "accepted" : "positive"
  const status = ignored ? "Ignoré" : !patient ? "À vérifier" : chosen ? "Choisi" : "Sûr"
  const shown = showAll ? group.photoIds : group.photoIds.slice(0, THUMBS_SHOWN)
  const splitPhoto = group.splitAt !== null ? photos.get(group.photoIds[group.splitAt]) : undefined
  const splitTime = splitPhoto ? correctedTime(splitPhoto) : null
  const label = `Groupe de ${timeRange(group.start, group.end)}`

  const pick = (choice: PatientRef | MatchCandidate) => {
    choosePatient(group.key, choice)
    setChoosing(false)
    setSearching(false)
  }

  return (
    <li
      id={`groupe-${group.key}`}
      aria-label={label}
      className={cn(
        "space-y-3 rounded-lg border border-border border-s-4 bg-card p-3 sm:p-4",
        unresolved ? "border-s-warning" : ignored ? "border-s-border" : "border-s-success",
      )}
    >
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-medium tabular-nums">
            {timeRange(group.start, group.end)}
            {showDay && group.start !== null && (
              <span className="font-normal text-muted-foreground"> · {formatWallClockDay(group.start)}</span>
            )}
          </p>
          <p className="text-xs text-muted-foreground">
            {kept === group.photoIds.length
              ? `${kept} photo${kept > 1 ? "s" : ""}`
              : `${kept} photo${kept > 1 ? "s" : ""} gardée${kept > 1 ? "s" : ""} sur ${group.photoIds.length}`}
            {cameraLabel && ` · ${cameraLabel}`}
            {group.uncertainTime && " · heure incertaine"}
          </p>
        </div>
        <span className={cn("shrink-0 rounded-full px-2 py-0.5 text-xs font-medium", statusToneClass(tone))}>
          {status}
        </span>
      </div>

      <ul className={cn("flex flex-wrap gap-1.5", ignored && "opacity-50")} aria-label="Photos du groupe">
        {shown.map((id) => {
          const photo = photos.get(id)
          if (!photo) return null
          const out = Boolean(excluded[id])
          const at = correctedTime(photo)
          const name = at !== null ? formatWallClockTime(at) : photo.file.name
          return (
            <li key={id}>
              <button
                type="button"
                onClick={() => toggleExcluded(id)}
                disabled={ignored}
                aria-pressed={out}
                aria-label={out ? `Reprendre la photo de ${name}` : `Écarter la photo de ${name}`}
                className="relative block size-16 overflow-hidden rounded-md border border-border bg-muted focus-visible:ring-2 focus-visible:ring-ring coarse:size-[4.5rem]"
              >
                <PhotoThumb photo={photo} />
                {out && (
                  <span className="absolute inset-0 flex items-center justify-center bg-background/70 text-2xs font-medium text-foreground">
                    Écartée
                  </span>
                )}
              </button>
            </li>
          )
        })}
        {!showAll && group.photoIds.length > THUMBS_SHOWN && (
          <li>
            <button
              type="button"
              onClick={() => setShowAll(true)}
              className="flex size-16 items-center justify-center rounded-md border border-dashed border-border text-sm text-muted-foreground hover:bg-accent coarse:size-[4.5rem]"
              aria-label={`Afficher les ${group.photoIds.length - THUMBS_SHOWN} autres photos`}
            >
              +{group.photoIds.length - THUMBS_SHOWN}
            </button>
          </li>
        )}
      </ul>

      {ignored ? (
        <p className="text-sm text-muted-foreground">Groupe ignoré : ces photos restent sur la carte.</p>
      ) : (
        <div className="space-y-2">
          {patient && !choosing && (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <p className="flex min-w-0 items-center gap-2 text-sm">
                <User className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
                <PatientNameLink patientId={patient.patientId} name={patient.patientName} />
                {!chosen && group.match && (
                  <span className="text-muted-foreground">· {candidateLine(group.match, doctorNames)}</span>
                )}
              </p>
              <Button type="button" variant="outline" size="sm" className="coarse:min-h-11" onClick={() => setChoosing(true)}>
                Changer
              </Button>
            </div>
          )}

          {choosing && (
            <div className="space-y-2">
              {!patient && ASK_REASON[group.reason] && (
                <p className="text-sm text-warning-ink">{ASK_REASON[group.reason]}</p>
              )}
              {group.candidates.length > 0 && (
                <div className="flex flex-col gap-1.5 sm:flex-row sm:flex-wrap" role="group" aria-label="Patients possibles">
                  {group.candidates.map((candidate) => (
                    <Button
                      key={`${candidate.patientId}-${candidate.appointmentId ?? "fiche"}`}
                      type="button"
                      variant="outline"
                      className={cn(
                        "h-auto justify-start gap-2 whitespace-normal py-1.5 text-start coarse:min-h-11",
                        patient?.patientId === candidate.patientId && "border-primary bg-primary/5",
                      )}
                      aria-pressed={patient?.patientId === candidate.patientId}
                      onClick={() => pick(candidate)}
                    >
                      {candidate.kind === "visit" ? (
                        <CalendarClock className="size-4 shrink-0" aria-hidden="true" />
                      ) : (
                        <FileText className="size-4 shrink-0" aria-hidden="true" />
                      )}
                      <span className="min-w-0">
                        <span className="block font-medium">{candidate.patientName}</span>
                        <span className="block text-xs font-normal text-muted-foreground">
                          {candidateLine(candidate, doctorNames)}
                        </span>
                      </span>
                    </Button>
                  ))}
                </div>
              )}

              {searching ? (
                <PatientSearchList label={`Patient du ${label.toLowerCase()}`} onPick={pick} autoFocus />
              ) : (
                <div className="flex flex-wrap gap-2">
                  <Button type="button" variant="ghost" size="sm" className="coarse:min-h-11" onClick={() => setSearching(true)}>
                    Autre patient…
                  </Button>
                  {patient && (
                    <Button type="button" variant="ghost" size="sm" className="coarse:min-h-11" onClick={() => setChoosing(false)}>
                      Garder {patient.patientName}
                    </Button>
                  )}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <div className="flex flex-wrap gap-2 border-t border-border pt-3">
        {!ignored && group.splitAt !== null && !group.splitFrom && (
          <Button type="button" variant="outline" size="sm" className="gap-1.5 coarse:min-h-11" onClick={() => splitGroup(group.key)}>
            <Scissors className="size-4" aria-hidden="true" />
            {splitTime !== null ? `Couper à ${formatWallClockTime(splitTime)}` : "Couper en deux"}
          </Button>
        )}
        <Button
          type="button"
          variant="ghost"
          size="sm"
          className="gap-1.5 text-muted-foreground coarse:min-h-11"
          onClick={() => toggleIgnored(group.key)}
        >
          {ignored && <Undo2 className="size-4" aria-hidden="true" />}
          {ignored ? "Reprendre ce groupe" : "Ignorer ce groupe"}
        </Button>
      </div>
    </li>
  )
}

/**
 * The camera's own embedded thumbnail when it wrote one (instant — a few kilobytes already in hand), else the photo
 * itself, decoded by the browser only when the tile scrolls into view.
 */
function PhotoThumb({ photo }: { photo: ImportPhoto }) {
  const [fallback, setFallback] = useState<string | null>(null)

  useEffect(() => {
    if (photo.thumbUrl) return
    const url = URL.createObjectURL(photo.file)
    setFallback(url)
    return () => URL.revokeObjectURL(url)
  }, [photo])

  const src = photo.thumbUrl ?? fallback
  if (!src) return null
  return (
    // eslint-disable-next-line @next/next/no-img-element -- a local object URL; next/image cannot optimise it
    <img
      src={src}
      alt=""
      loading="lazy"
      decoding="async"
      draggable={false}
      className="size-full object-cover"
      // An embedded thumbnail lies the way the sensor did; the photo itself is turned by the browser.
      style={photo.thumbUrl ? { transform: orientationTransform(photo.exif.orientation) } : undefined}
    />
  )
}
