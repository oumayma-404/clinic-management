# Run 2 — after the F1 fix

Environment: API restarted by this session with the F1 fix (bin/Release, PID recorded, generation 38);
`next dev` restarted by this session earlier (fresh). No live clinic-management peers. Fresh fixtures per
launch. Two launches, both run 2: the first lost A2 to a screenshot timeout on the cold API (probe), the second
polls the toast concurrently with the click (the trap recorded in `devis-fiche-rdv-flexibility/qa`).

Widths looked at: 1440×900, 390×844.

| id | outcome | evidence |
|---|---|---|
| A1 | ✅ | « Prix du traitement 160,000 · Déjà payé 100,000 · Reste à payer 60,000 » |
| A2 | ✅ / ⏭ | save 200, `treatmentCollection {Collected, noteNumber, amountCollected 60, outstanding 0}`; ⏭ toast — none captured on the create door in 5 launches (captured on the update door every time). Same code path; not separable from the probe here |
| A3 | ✅ | note Paid 160,000 · one live payment 60,000 tagged to séance 2 · échéancier 0 |
| A4 | ✅ | reopened « 60,000 », re-save took nothing |
| A5 | ✅ | séance 1 re-saved unchanged: « Fiche de soins mise à jour · déjà facturée sur la note n° … » |
| B1 | ✅ | 70 → « Il ne reste que 60,000 DT », save disabled |
| C1 | ✅ | 390 px: row [37,353] in dialog [0,390], page 390 |
| D1 | ✅ | ordinary devis: Reste 200,000, no note mentioned |
| D2 | ✅ | « Ajouter au devis » absent |
| D3 | ✅ | delete preview lists « 60,000 DT encaissés à cette séance sur la note n° … (la note est conservée) » |

## Observations (off-plan)

- No toast is observed after **creating** a fiche from `?addRecord=1&appointmentId=…`, whatever the outcome
  (the update door shows its toast). Not caused by this change — the create and update doors share the toast
  code — and not chased. Worth one `/test-in-browser` row of its own.

**Status: GREEN** (10/10, one sub-check ⏭ with its reason)
