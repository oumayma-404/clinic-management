/**
 * The cabinet's letterhead PAGE, as a canvas: the first page of a PDF, or a scanned / exported image.
 *
 * Everything the import tool does happens on this one canvas, in the browser — the source file never leaves the
 * machine; only the two bands cut from it are uploaded.
 *
 * ⚠️ The page's width becomes the document's 210 mm, so it is always brought to 300 dpi across A4 (`PAGE_WIDTH_PX`).
 * A small image is ENLARGED, smoothly: it adds no detail, but a cabinet's only copy of its paper is often a 450 px
 * image from WhatsApp, and refusing it blocked the import outright. The dialog says the print will be softer instead.
 */

/** 300 dpi across 210 mm. */
export const PAGE_WIDTH_PX = 2480

/** 150 dpi across 210 mm: a narrower source prints visibly soft, and the dialog says so. */
export const SHARP_SOURCE_WIDTH_PX = 1240

/** ~48 dpi: below it a letterhead's small print cannot be read at all, so the image is refused. */
export const MIN_SOURCE_WIDTH_PX = 400

export interface PageImage {
  canvas: HTMLCanvasElement
  /** The source's own width in pixels; a PDF counts as sharp. */
  sourceWidthPx: number
}

export const ACCEPTED_SOURCES = "application/pdf,image/png,image/jpeg,.pdf,.png,.jpg,.jpeg"

/** French refusal for a file that is neither a PDF nor a PNG/JPEG image. */
export const UNSUPPORTED_SOURCE =
  "Ce fichier n'est ni un PDF ni une image PNG ou JPEG. Exportez votre en-tête en PDF depuis Word, ou scannez-le."

export function isPdf(file: File): boolean {
  return file.type === "application/pdf" || /\.pdf$/i.test(file.name)
}

function isImage(file: File): boolean {
  return /^image\/(png|jpeg)$/.test(file.type) || /\.(png|jpe?g)$/i.test(file.name)
}

/** Renders the source onto a white canvas `PAGE_WIDTH_PX` wide. Throws a French `Error` on failure. */
export async function loadPageImage(file: File): Promise<PageImage> {
  if (isPdf(file)) return { canvas: await renderPdfFirstPage(file), sourceWidthPx: PAGE_WIDTH_PX }
  if (isImage(file)) return renderImage(file)
  throw new Error(UNSUPPORTED_SOURCE)
}

async function renderImage(file: File): Promise<PageImage> {
  let bitmap: ImageBitmap
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: "from-image" })
  } catch {
    throw new Error("Cette image n'a pas pu être lue.")
  }

  const sourceWidthPx = bitmap.width
  if (sourceWidthPx < MIN_SOURCE_WIDTH_PX) {
    bitmap.close()
    throw new Error(
      `Image trop petite (${sourceWidthPx} px de large) : le texte de l'en-tête serait illisible. ` +
        "Importez le PDF de votre imprimeur, un scan, ou une image plus grande.",
    )
  }

  const scale = PAGE_WIDTH_PX / sourceWidthPx
  const canvas = whiteCanvas(PAGE_WIDTH_PX, Math.round(bitmap.height * scale))
  const context = canvas.getContext("2d")!
  context.imageSmoothingQuality = "high"
  context.drawImage(bitmap, 0, 0, canvas.width, canvas.height)
  bitmap.close()
  return { canvas, sourceWidthPx }
}

async function renderPdfFirstPage(file: File): Promise<HTMLCanvasElement> {
  // Lazy: pdf.js is ~1 Mo and only this one tool needs it. The legacy build covers the Android WebView shell.
  const pdfjs = await import("pdfjs-dist/legacy/build/pdf.mjs")
  // Same-origin worker file from the bundle — a `blob:` worker would be the production-only CSP failure.
  pdfjs.GlobalWorkerOptions.workerSrc = new URL(
    "pdfjs-dist/legacy/build/pdf.worker.min.mjs",
    import.meta.url,
  ).toString()

  try {
    const data = new Uint8Array(await file.arrayBuffer())
    const task = pdfjs.getDocument({ data })
    const pdf = await task.promise
    try {
      const page = await pdf.getPage(1)
      const viewport = page.getViewport({ scale: 1 })
      const scaled = page.getViewport({ scale: PAGE_WIDTH_PX / viewport.width })
      const canvas = whiteCanvas(Math.round(scaled.width), Math.round(scaled.height))
      await page.render({ canvas, viewport: scaled, background: "#ffffff" }).promise
      return canvas
    } finally {
      await task.destroy()
    }
  } catch (error) {
    if (error instanceof Error && error.name === "PasswordException") {
      throw new Error("Ce PDF est protégé par un mot de passe. Exportez-en une version sans protection.")
    }
    throw new Error("Ce PDF n'a pas pu être lu.")
  }
}

// A transparent PNG would otherwise print its transparent areas as whatever the PDF puts behind them.
function whiteCanvas(width: number, height: number): HTMLCanvasElement {
  const canvas = document.createElement("canvas")
  canvas.width = width
  canvas.height = height
  const context = canvas.getContext("2d")!
  context.fillStyle = "#ffffff"
  context.fillRect(0, 0, width, height)
  return canvas
}
