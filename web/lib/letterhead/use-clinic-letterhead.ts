"use client"

import { useCallback, useEffect, useState } from "react"

import { clinicsApi, type ClinicLetterheadDto, type LetterheadBand } from "@/lib/api/clinics"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/** One band of the cabinet's letterhead, ready to draw on screen and to embed in a Word export. */
export interface LetterheadBandImage {
  url: string
  blob: Blob
  width: number
  height: number
}

export interface ClinicLetterheadState {
  /** Null until the first read answers. */
  letterhead: ClinicLetterheadDto | null
  /** The read failed — never shown as « no letterhead ». */
  failed: boolean
  header: LetterheadBandImage | null
  footer: LetterheadBandImage | null
  /** « Page entière »: the strip between the bands, stretched behind the text. */
  body: LetterheadBandImage | null
  reload: () => void
  /** Adopt what a save or a removal answered, without waiting for the broadcast. */
  set: (letterhead: ClinicLetterheadDto) => void
}

/**
 * The cabinet's letterhead and its band images — the one reader the settings card and the document editor share,
 * so the two cannot show different paper. Refreshed by the `clinics` broadcast; the images are re-read only when
 * the bands themselves change (`revision`).
 */
export function useClinicLetterhead(): ClinicLetterheadState {
  const [letterhead, setLetterhead] = useState<ClinicLetterheadDto | null>(null)
  const [failed, setFailed] = useState(false)
  const [images, setImages] = useState<BandImages>(NO_IMAGES)

  const reload = useCallback(async () => {
    try {
      setLetterhead(await clinicsApi.getLetterhead())
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  useClinicRealtime(RealtimeResource.Clinics, () => void reload())

  const revision = letterhead?.hasHeader ? letterhead.revision ?? null : null
  const hasFooter = letterhead?.hasFooter ?? false
  const hasBody = letterhead?.hasBody ?? false

  useEffect(() => {
    if (!revision) {
      setImages(NO_IMAGES)
      return
    }

    let active = true
    const created: string[] = []
    void (async () => {
      try {
        const header = await readBand("header", revision, created)
        const footer = hasFooter ? await readBand("footer", revision, created) : null
        const body = hasBody ? await readBand("body", revision, created) : null
        if (active) setImages({ header, footer, body })
      } catch {
        if (active) setImages(NO_IMAGES)
      }
    })()

    return () => {
      active = false
      created.forEach((url) => URL.revokeObjectURL(url))
    }
  }, [revision, hasFooter, hasBody])

  return {
    letterhead,
    failed,
    header: images.header,
    footer: images.footer,
    body: images.body,
    reload: () => void reload(),
    set: setLetterhead,
  }
}

interface BandImages {
  header: LetterheadBandImage | null
  footer: LetterheadBandImage | null
  body: LetterheadBandImage | null
}

const NO_IMAGES: BandImages = { header: null, footer: null, body: null }

async function readBand(band: LetterheadBand, revision: string, created: string[]): Promise<LetterheadBandImage> {
  const blob = await clinicsApi.getLetterheadBand(band, revision)
  const bitmap = await createImageBitmap(blob)
  const size = { width: bitmap.width, height: bitmap.height }
  bitmap.close()
  const url = URL.createObjectURL(blob)
  created.push(url)
  return { url, blob, ...size }
}
