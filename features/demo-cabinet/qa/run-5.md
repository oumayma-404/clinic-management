# Run report — story 5 (« SPÉCIMEN » PDFs, per-visit limit)

**Date:** 2026-11-03 · **Plan:** `qa/plan-5.md` · **Driver:** `qa/walk-5.mjs`
**Environment:** isolated stack — API :5099 (`PublicDemo`, DB `clinic_demo_local`, fresh cabinet each restart), web :3099 (`next dev`), own Chrome contexts. No peer stack touched.

## Cycle 1 — RED (2)
| ID | Result | Verdict |
|---|---|---|
| P10 | 400 `validation_failed` | probe bug — `content` is a string map, the walk sent a string; fixed in the walk |
| R2 | visitor B got 429 after visitor A spent the budget | **product defect** — the limiter runs before `UseAuthentication`, so `User` carried no visit and both keys fell back to the address |

## Fix (cycle 1 → 2)
- `RateLimiting.DemoSessionPartitionKey` reads `family_id` off the raw bearer. A first version used `JwtSecurityTokenHandler.ReadJwtToken` and still returned nothing on a real demo token while an unsigned test token passed; it now decodes the payload itself, and the test mints its token with the real `LocalAuthService` (red-proofed against the handler version).
- Eye pass also showed the footer line's white box clipping the demo letterhead's address line → moved 8 pt lower (text ~5 mm above the edge).

## Cycle 2 — GREEN
| ID | Result |
|---|---|
| P1–P4 | ordonnance, demande d'examens, certificat, arrêt de travail (P 061) — 200, stamped |
| P5–P8 | note d'honoraires, reçu, devis, reçu d'échéance — 200, stamped |
| P9 | ⏭ the demo holds no avoir (covered by `SpecimenPdfTests.Every_Renderer_Is_Stamped_On_Every_Page(GenerateAvoirPdfAsync)`) |
| P10–P11 | « Imprimer » and the ordonnance aperçu — 200, stamped |
| E1 | ordonnance, arrêt de travail, note d'honoraires rendered (pdf.js, page 1) — mark light, text and cachet legible, footer line clear of the letterhead: `shots/S5-E1-*.png` |
| R1 | 429 `demo_pdf_limited` « La démo limite le nombre de documents ouverts. Réessayez dans 5 minutes. » on the 31st PDF, `Retry-After: 300` |
| R2 | a second visitor right after: 200 |
| R3 | the limited visit still reads patients: 200 |
| D1 | no write beyond sign-in and the two allowed PDF POSTs |

**Status:** GREEN (17/17 exercised, 1 ⏭ with reason)
