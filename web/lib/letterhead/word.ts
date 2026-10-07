import {
  Footer,
  Header,
  HorizontalPositionRelativeFrom,
  ImageRun,
  Paragraph,
  VerticalPositionRelativeFrom,
  type ISectionOptions,
} from "docx"

import type { LetterheadBandImage } from "@/lib/letterhead/use-clinic-letterhead"

// Word measures a page in twips and an image in pixels at 96 dpi (9 525 EMU each); A4 is 794 × 1123 px.
const PAGE_WIDTH_PX = 794
const PAGE_HEIGHT_PX = 1123
const TWIPS_PER_PX = 15
const EMU_PER_PX = 9525
const A4_TWIPS = { width: 11906, height: 16838 }
// The PDF's 2 cm margins, and the same small gap under the header band.
const SIDE_MARGIN_TWIPS = 1134
const GAP_BELOW_HEADER_PX = 23

/**
 * The Word export's page set-up on the cabinet's own paper: the bands pinned to the page edges at true scale —
 * exactly where the PDF draws them — and the text kept between them; with « Page entière », the strip between them is
 * stretched behind the text as the PDF does. Nothing on a cabinet without a letterhead.
 */
export async function wordLetterheadSection(
  header: LetterheadBandImage | null,
  footer: LetterheadBandImage | null,
  body: LetterheadBandImage | null = null,
): Promise<Partial<ISectionOptions>> {
  if (!header) return {}

  const headerPx = Math.round((PAGE_WIDTH_PX * header.height) / header.width)
  const footerPx = footer ? Math.round((PAGE_WIDTH_PX * footer.height) / footer.width) : 0

  return {
    properties: {
      page: {
        size: A4_TWIPS,
        margin: {
          top: (headerPx + GAP_BELOW_HEADER_PX) * TWIPS_PER_PX,
          bottom: footer ? (footerPx + GAP_BELOW_HEADER_PX) * TWIPS_PER_PX : SIDE_MARGIN_TWIPS,
          left: SIDE_MARGIN_TWIPS,
          right: SIDE_MARGIN_TWIPS,
          header: 0,
          footer: 0,
        },
      },
    },
    headers: {
      default: new Header({
        children: [
          await bandParagraph(header, headerPx, 0),
          ...(body ? [await bandParagraph(body, PAGE_HEIGHT_PX - headerPx - footerPx, headerPx * EMU_PER_PX)] : []),
        ],
      }),
    },
    ...(footer
      ? {
          footers: {
            default: new Footer({
              children: [await bandParagraph(footer, footerPx, (PAGE_HEIGHT_PX - footerPx) * EMU_PER_PX)],
            }),
          },
        }
      : {}),
  }
}

async function bandParagraph(band: LetterheadBandImage, heightPx: number, topEmu: number): Promise<Paragraph> {
  return new Paragraph({
    children: [
      new ImageRun({
        type: "png",
        data: new Uint8Array(await band.blob.arrayBuffer()),
        transformation: { width: PAGE_WIDTH_PX, height: heightPx },
        floating: {
          horizontalPosition: { relative: HorizontalPositionRelativeFrom.PAGE, offset: 0 },
          verticalPosition: { relative: VerticalPositionRelativeFrom.PAGE, offset: topEmu },
          behindDocument: true,
        },
      }),
    ],
  })
}
