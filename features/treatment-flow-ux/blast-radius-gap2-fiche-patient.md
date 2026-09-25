# Blast radius — gap2/run3 QA fixes, fiche + patient page + tooth arch (items 1–9)

Display only. No request, payload, reducer action, tri-state rule or id changes.

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `ChequeFields` — new optional `fold` prop (summary row « Chèque : n° · banque · date », expands inline) | shared sub-form | fiche footer (passes `fold`), `payment-modal`, `installment-payment-modal`, `settle-plan-modal` (pass nothing) | unaffected — additive prop; without it the markup is byte-identical. Fields stay MOUNTED when folded (`hidden`), so ids `fiche-cheque-*` and `chequePaymentFields` are unchanged |
| 2 | fiche footer, mixed séance: « Payé (traitement) » label inline beside its field below `sm:` | layout | none; ids `#paid` `#paid-method` `#session-total` `#collected-on-plan` kept (e2e FICHE-20/22/24 read ids) | unaffected — only when the mode is NOT in that row (`!withholdSeanceMoneyFields`) |
| 3 | « DT » suffix inside `#paid`, `#session-total`, `#collected-on-plan`; label « Prix » → « Prix du traitement »; struck old price `formatAmount` → `formatDT` | display | e2e: no label/text match on these (grep « Prix », « Déjà payé ») | unaffected — suffix is `aria-hidden`, value/state untouched |
| 4 | Haut / Bas / Toute la bouche / Vider: `coarse:h-11` | layout | none | unaffected — grows the box; the Button's own overlay then coincides |
| 5 | `act-card` `AddedTag`: `Plus` icon dropped | display | e2e: no match on « Ajouté au traitement » | unaffected |
| 6 | `act-card` head: trash gets negative block margins on the ARMED card only, so its 44 px box stops setting the head height | layout | at-rest head untouched (margins gated on `focused`) | must re-test — armed card at 820 coarse + 1440 fine |
| 7 | `act-card` « Détails » row → `flex-wrap` + `basis-48`, « Changer d'acte » wraps under it at 320 (one line kept at 390) | layout | none | unaffected |
| 8 | patient page: panel title « Actes dentaires » → « Fiches de soins » (+ `ariaLabel`), header action hidden while the empty state (with its own action) is on screen, columns « Montant payé » / « Reste » → « Payé » / « Reste à payer » | display | e2e: none by text (grep); the header action still shows while loading or on a failed read (the failure banner has no create action) | unaffected |
| 9 | `patient-outstanding-strip` `documentTitle` → « Devis n° 2026-0762 » / « Note d'honoraires n° … » | display, 3 uses (card title, table cell, « Encaisser sur … » aria-label) | e2e: none (grep « Devis 20 ») | unaffected — same string in all three |
| 10 | `ToothArchLayout`: scroll box measured (ResizeObserver + scroll) → mask fade on the side(s) holding hidden teeth; merged ref with `dragSelect.containerProps.ref` (stable `useCallback`, since an inline ref would flip the drag hook's `useState` container every render) | shared layout | `odontogram.tsx`, `odontogram-acts-chart.tsx`, `record-tooth-chart.tsx` (fiche + patient summary) | must re-test all 3 — drag-select still attaches; `arch-clipping` (`mx-auto w-max`, no `justify-center`) untouched |

## Capabilities moved (none removed)

| Capability | Before | After |
|---|---|---|
| Cheque identity in the fiche (n°, banque, encaissable le) | three fields always open under the figures | same three fields, same ids, behind one summary row that shows what is typed; one tap opens them |
| « Nouvelle fiche de soins » on an empty list | header button + empty-state button | empty-state button only (header button returns once a fiche exists, or on a failed read) |

## Not mine — reported to the plans-strip owner

- C4-390 dangling « · » and C4-820 « … » menu on its own line: both in `treatment-plans/patient-plans-strip.tsx` (facts row separators; header row `flex-wrap` + `ms-auto` trigger), not in `page.tsx`.
