# Run 3 — GREEN

**Status: `GREEN` — 0 product findings. Eye pass done, regression sweep done.**

## Environment

- API `:5000` pid 19840 (owned by `clinic-management-20`), fresh build carrying every changed file.
- web `:3000` pid 37080, `.next` rebuilt cleanly before the drive (not during it).
- Browser profile claimed for the drive, released after.
- Peers live: `clinic-management-20`, `-d6`, `-bf`, `-af`, `-8b`, `investigate-phone`, `anakin-28`, `anakin-9c`.

## Scenarios

| id | Scenario | Layer | Outcome | Evidence |
|---|---|---|---|---|
| A2 | The caisse warning renders | browser | ✅ | « Cet argent est déjà entré dans la caisse — 80,000 DT seront annulés. » |
| A2-devis | The bullet names the **devis** the money sits on | browser | ✅ | « 80,000 DT encaissés sur le devis 2026-0531 » |
| A3 | Confirming deletes and reverses | browser + sql | ✅ | toast « Fiche de soins supprimée. » |
| A4 | The note paragraph is correct | browser | ✅ | « La note d'honoraires n° 2026-0589 sera **annulée**. Elle garde son numéro… » |
| A6 | The preview endpoint tells the truth | api | ✅ | `totalReversed 80 · plans[2026-0531] · affectedCaisseDays[2026-09-22] · refusal null` |
| C1 | **Eye pass** at 320 / 390 / 820 / 1180 / 1440 / 1536×730 | browser | ✅ | no horizontal overflow at any width; no descendant wider than the dialog; « Supprimer » visible in the viewport at every width |
| C1-devis | Same, on the devis variant | browser | ✅ | 320 and 1536×730 |
| C2 | The affected caisse day is named | browser | ✅ | « La caisse du 22/09/2026 sera modifiée — pas celle d'aujourd'hui. » |
| D1 | La caisse | browser + api | ✅ | 1000 → 920, **−80 exactly**, on the payment's day |
| D2/D3 | « Créances » == « Solde patient » | api | ✅ | both 80,000 DT after the reversal |
| D3b | The patient owes it again | api | ✅ | solde 0 → 80 |
| D4 | Dashboard « Encaissé » | api | ✅ | 53 015,5 → 52 935,5, **−80 exactly** |
| D6 | The records tab reloads clean | browser | ✅ | no error, no stuck dialog |
| B2 | Banked-cheque refusal | — | ⏭ | not arranged; covered by `A_Cheque_Already_Banked_Refuses_The_Deletion` |
| B5 | Preview read fails | — | ⏭ | not arranged; the disabled-until-loaded branch was observed, the failure branch was not |
| D5 | Bridged-note detach | — | ⏭ | not arranged in the browser; covered by `A_Bridged_Note_Is_Released_From_Its_Devis_Before_It_Is_Cancelled` |

## The ledger, after a deletion driven through the product

```
devis 2026-0533 · TotalPlanned 80.000 · échéance Amount 80.000 · AmountPaid 0.000
  paiement 80.000 · IsVoided = true
              VoidReason   = « Fiche de soins du 22/09/2026 supprimée »
              VoidedByName = « Dr Salma Ben Youssef »
```

Three things worth naming, because each is a rule this change had to get right:

- the payment row is **kept and marked**, never deleted — the correction has a trail with an author;
- `Installment.Amount` stays 80 while `AmountPaid` returns to 0, so **Σ échéances == TotalPlanned** still holds;
- the voided row is excluded from `reconcile-money`'s new `no-money-without-a-fiche` check, so a correction
  that has already been made does not report as drift.

## Eye pass — what was actually looked at

Screenshots read, not merely captured: `shots/dialog-320x720.png`, `dialog-390x844.png`,
`dialog-1536x730.png`, `devis-320x720.png`, plus the measured set at 820/1180/1440.

At 320 px the red box holds its own width, the bullet wraps, and the two buttons stack full-width with
« Supprimer » on top. At 1536×730 — the owner's real laptop — the title sits on one line and both buttons are
well above the fold (`confirmBottom 529` of 730).

## Observations (off-plan)

| # | What | Severity |
|---|---|---|
| 1 | At 320 px the dialog **title** wraps as « Supprimer cette fiche de soins » / « ? », leaving the question mark alone on its own line. French typography wants a narrow no-break space before `?`, which would also stop it wrapping. **Pre-existing** — the title is not part of this change — and the same class of defect `quoteFr()` / check N1 already exists for guillemets. | observation |

## ⚠️ Three suite failures that are NOT this change

The unfiltered suite currently reports `Failed: 3, Passed: 4740`. All three are
`clinic-management-bf`'s in-flight `DentalRecordBillingGuard` work (+88 lines), which changes which refusal
code wins on the fiche **re-dating** path:

```
Expected: "dental_record_payment_banked"
Actual:   "dental_record_acts_changed_after_billing"
```

`DentalRecordCorrectionTests.cs` is committed, unmodified, and references **none** of this change's symbols.
The five files in flight are theirs, not mine. Reported to them; not touched.

**This change's own areas: 57/57 green**, and the whole suite was `0 failed / 4744 passed` immediately
before their edits landed.

**Status: `GREEN` (0 findings).**
