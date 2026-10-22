"""Reads a PDF the way the QA walk needs: pages, where each image sits, the text, and a PNG per page to look at.

Usage: python pdfcheck.py <pdf> <png-prefix>   → prints one JSON object.
"""
import json
import sys

import fitz  # PyMuPDF


def main(path: str, prefix: str) -> None:
    doc = fitz.open(path)
    pages = []
    for index, page in enumerate(doc):
        images = []
        for info in page.get_image_info():
            x0, y0, x1, y1 = info["bbox"]
            images.append({"x0": round(x0, 1), "y0": round(y0, 1), "x1": round(x1, 1), "y1": round(y1, 1)})
        width, height = page.rect.width, page.rect.height
        text = page.get_text("text")
        png = f"{prefix}-p{index + 1}.png"
        page.get_pixmap(dpi=60).save(png)
        pages.append({
            "width": round(width, 1),
            "height": round(height, 1),
            "images": images,
            # A band drawn edge to edge at the top / bottom of the page.
            "topBand": any(i["y0"] <= 1 and i["x0"] <= 1 and i["x1"] >= width - 1 for i in images),
            "bottomBand": any(i["y1"] >= height - 1 and i["x0"] <= 1 and i["x1"] >= width - 1 for i in images),
            "head": " ".join(text.split())[:160],
            "png": png,
        })
    full = " ".join(" ".join(p.get_text("text") for p in doc).split())
    print(json.dumps({"pages": pages, "naissance": "naissance" in full.lower(), "bytes": doc.tobytes().__len__()}))


main(sys.argv[1], sys.argv[2])
