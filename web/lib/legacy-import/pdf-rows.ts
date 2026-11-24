/**
 * The text of a record PDF as rows, top to bottom, each row its cells left to right — all a fixed-form export needs.
 *
 * Throwaway, with the rest of `lib/legacy-import/`: it moves one practice's old software into the app, and is
 * deleted once that import is done.
 */

/**
 * One text run of the PDF: where it starts on the line, its rounded baseline and page (PDF points), and what it says.
 * `y` and `page` are what tell one history entry from the next — rows of an entry sit closer than two entries do.
 */
export interface PdfCell {
  x: number
  y: number
  page: number
  s: string
}

export interface PdfText {
  rows: PdfCell[][]
  /** 0 for a scanned PDF (an image with no text layer). */
  textChars: number
}

export async function pdfRows(file: File): Promise<PdfText> {
  // Same loader as `lib/letterhead/page-image.ts`: lazy, legacy build, and a same-origin worker file — a `blob:`
  // worker is the production-only CSP failure.
  const pdfjs = await import("pdfjs-dist/legacy/build/pdf.mjs")
  pdfjs.GlobalWorkerOptions.workerSrc = new URL("pdfjs-dist/legacy/build/pdf.worker.min.mjs", import.meta.url).toString()

  const task = pdfjs.getDocument({ data: new Uint8Array(await file.arrayBuffer()) })
  try {
    const pdf = await task.promise
    const rows: PdfCell[][] = []
    for (let p = 1; p <= pdf.numPages; p++) {
      const page = await pdf.getPage(p)
      const byY = new Map<number, PdfCell[]>()
      for (const item of (await page.getTextContent()).items) {
        if (!("str" in item) || !item.str.trim()) continue
        const y = Math.round(item.transform[5])
        const cells = byY.get(y) ?? []
        cells.push({ x: Math.round(item.transform[4]), y, page: p, s: item.str.trim() })
        byY.set(y, cells)
      }
      for (const y of [...byY.keys()].sort((a, b) => b - a)) rows.push(byY.get(y)!.sort((a, b) => a.x - b.x))
    }
    return { rows, textChars: rows.flat().reduce((n, c) => n + c.s.length, 0) }
  } finally {
    await task.destroy()
  }
}
