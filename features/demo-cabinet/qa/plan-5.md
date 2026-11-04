# QA plan — story 5 (« SPÉCIMEN » PDFs, per-visit limit)

Backend-only diff (no rendering file). Driven by `qa/walk-5.mjs` over the isolated stack (:5099 / :3099, `PublicDemo`).
A PDF is « stamped » when its `/Keywords` reads `SPÉCIMEN — DÉMONSTRATION`; the eye check is a screenshot of one
rendered page per renderer family.

| ID | Tier | Scenario | Expected | Layer |
|---|---|---|---|---|
| P1 | A | Ross's ordonnance PDF (`GET medical-documents/{id}/pdf`) | 200, `%PDF`, stamped | api |
| P2 | A | Ross's demande d'examens PDF | 200, stamped | api |
| P3 | A | Joey's certificat PDF | 200, stamped | api |
| P4 | A | Frank's arrêt de travail PDF (CNAM P 061 overlay, bypasses `DocumentPage`) | 200, stamped | api |
| P5 | A | A note d'honoraires PDF | 200, stamped | api |
| P6 | A | A reçu (invoice payment) PDF | 200, stamped | api |
| P7 | A | A devis PDF | 200, stamped | api |
| P8 | A | A reçu d'échéance PDF (if the demo holds one) | 200, stamped, or ⏭ none seeded | api |
| P9 | A | An avoir PDF (if the demo holds one) | 200, stamped, or ⏭ none seeded | api |
| P10 | A | « Imprimer » (`POST generate-pdf-download`) | 200, stamped — not refused by the read-only gate | api |
| P11 | A | Ordonnance aperçu (`POST fiche-ordonnance-preview`) | 200, stamped | api |
| E1 | A | Eye: ordonnance, arrêt de travail, note d'honoraires rendered in Chrome | diagonal mark + footer line visible, letterhead/text still readable | browser |
| R1 | B | Same visit keeps asking for PDFs | 429 with `code: demo_pdf_limited` and a French sentence, by request 31 | api |
| R2 | B | A second visitor right after | 200 — the first visit's limit is not theirs | api |
| R3 | B | Ordinary reads still work for the limited visit (`GET patients`) | 200 | api |
| D1 | D | No write reached the server during the walk | only `auth/*` and the two allowed PDF POSTs | browser |
| D2 | D | Production profile carries no mark | covered by `SpecimenPdfTests.A_Production_Document_Carries_No_Mark` + the per-kind theory | unit |
