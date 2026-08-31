"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { patientFilesApi } from "@/lib/api/patient-files"
import { cn } from "@/lib/utils"
import type { UploadPolicy } from "@/lib/api/upload-policy"
import type { PatientFileDto } from "@/lib/api/types"

import { fileIcon, isThumbnailable } from "./file-kind"

/**
 * A file row's thumbnail (AC-5.2) — an image where one can be painted, the format's icon everywhere else.
 *
 * <p>Three gates before a byte is fetched: the row is **on screen** (`IntersectionObserver`), the entry is
 * **browser-previewable** per the served policy (so a HEIC shows an icon and says why rather than a broken
 * image), and the file is **small enough** to be worth downloading whole for a 40 px square — a panoramique is
 * 25 MB and forty of them is a clinic's morning.</p>
 *
 * <p>Live object URLs are held in a bounded module pool: the oldest is revoked when the pool is full, and its
 * owner drops back to the icon rather than showing a dead `blob:` URL. Without the bound, scrolling a long
 * drawer retains every blob for the life of the tab.</p>
 */
const MAX_LIVE_THUMBNAILS = 24

/** Above this, the icon. A thumbnail is not worth a multi-megabyte download on a clinic's uplink. */
const MAX_THUMBNAIL_BYTES = 8 * 1024 * 1024

interface PooledThumbnail {
  url: string
  drop: () => void
}

const pool: PooledThumbnail[] = []

function retain(url: string, drop: () => void): void {
  pool.push({ url, drop })
  while (pool.length > MAX_LIVE_THUMBNAILS) {
    const evicted = pool.shift()
    if (evicted) {
      window.URL.revokeObjectURL(evicted.url)
      evicted.drop()
    }
  }
}

function release(url: string): void {
  const index = pool.findIndex((entry) => entry.url === url)
  if (index >= 0) {
    pool.splice(index, 1)
    window.URL.revokeObjectURL(url)
  }
}

export function FileThumbnail({
  patientId,
  file,
  policy,
  className,
  iconClassName,
  imgClassName,
}: {
  patientId: string
  file: PatientFileDto
  policy?: UploadPolicy | null
  className?: string
  /** The fallback icon's size — a `h-4` glyph is right in a 40 px row and lost in a 160 px grid tile. */
  iconClassName?: string
  /** `object-cover` crops, which is right for a 40 px square and wrong for a radiograph in a grid tile. */
  imgClassName?: string
}) {
  const [url, setUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)
  const holder = useRef<HTMLDivElement | null>(null)
  const liveUrl = useRef<string | null>(null)

  /*
   * ⚠️ **A stand-in must exist — the original is never fetched to paint a tile.**
   *
   * This used to call `downloadFile` for every tile, pulling the FULL original across the wire to draw a 40 px
   * square. That was always wasteful; it became a correctness problem when the download endpoint started
   * recording an access in the cabinet's journal, because scrolling a file list then wrote one « radiographie
   * téléchargée » row per visible tile. The journal exists to answer « qui a sorti une copie du dossier de ce
   * patient ? », and a row per thumbnail buries the handful that mean something — the same argument that keeps
   * `Notification` off the audit interceptor.
   *
   * Falling back to the original when no stand-in exists was tried and rejected: every file uploaded before the
   * preview feature has `hasPreview: false`, so on a real database the fallback IS the common case and the noise
   * comes straight back. A file with no stand-in therefore shows its icon, which is what it already did for
   * every non-image. **Backfilling previews for existing files restores those thumbnails** — see
   * `follow-up/security-remediation-outstanding.md`.
   */
  const eligible =
    isThumbnailable(file, policy) &&
    file.hasPreview &&
    file.fileSize > 0 &&
    file.fileSize <= MAX_THUMBNAIL_BYTES

  const drop = useCallback(() => {
    liveUrl.current = null
    setUrl(null)
  }, [])

  useEffect(() => {
    if (!eligible || failed) return
    const node = holder.current
    if (!node || typeof IntersectionObserver === "undefined") return

    let cancelled = false
    const observer = new IntersectionObserver((entries) => {
      if (!entries.some((entry) => entry.isIntersecting)) return
      observer.disconnect()

      patientFilesApi
        .downloadPreview(patientId, file.id)
        .then((blob) => {
          if (cancelled) return
          const objectUrl = window.URL.createObjectURL(blob)
          liveUrl.current = objectUrl
          retain(objectUrl, drop)
          setUrl(objectUrl)
        })
        // A thumbnail that cannot be fetched is not an error worth a toast — the row falls back to its icon and
        // every action on it still works.
        .catch(() => { if (!cancelled) setFailed(true) })
    })

    observer.observe(node)
    return () => {
      cancelled = true
      observer.disconnect()
    }
  }, [patientId, file.id, eligible, failed, drop])

  useEffect(() => () => {
    if (liveUrl.current) release(liveUrl.current)
  }, [])

  const Icon = fileIcon(file)

  return (
    <div
      ref={holder}
      className={cn(
        "flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-accent/30 text-primary",
        className,
      )}
    >
      {url ? (
        // A `blob:` URL cannot go through next/image.
        <img
          src={url}
          alt=""
          aria-hidden="true"
          onError={() => { setFailed(true); drop() }}
          className={cn("size-full object-cover", imgClassName)}
        />
      ) : (
        <Icon className={cn("h-4 w-4", iconClassName)} />
      )}
    </div>
  )
}
