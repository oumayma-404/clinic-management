# QA plan — wave 1 (visit · fiche · devis never lock each other)

Arrange over the API on a **fresh throwaway patient** (« QA Flex <ts> »), act in the browser, read back with SQL.
Money/clinical writes are the change under test here, and only on that patient.

| ID | Tier | Scenario | Expected (observable) | Layer |
|----|------|----------|-----------------------|-------|
| A1 | A | Visit on a devis act already recorded (fiche saved, 1-act devis completed) → edit dialog, change notes, Enregistrer | success toast; notes saved (SQL); no « Le plan de traitement est requis » | browser |
| A2 | A | Same visit: price field | no « remettre au tarif », card reads devis-carried | browser |
| A3 | A | Future visit on a devis then cancelled via API → edit notes, Enregistrer | Dialog saves (visit was released → act kept, no link) — SQL notes | browser |
| A4 | A | Future visit with an act later archived in « Mes actes » → edit notes, Enregistrer | saves; act still on the visit | browser |
| A5 | B | Create dialog: patient P1, add a devis act from « Actes du devis », switch to patient P2 | devis act row gone from the list | browser |
| E1 | A | Visit booked with « Implant dentaire » as one séance (no treatment) → edit, move the time, Enregistrer | no new treatment plan for the patient (SQL count unchanged) | browser + sql |
| B1 | A | Reopen the fiche of A1 (devis Completed), change its notes, Enregistrer | saves, devis still Completed | browser + sql |
| B2 | A | Walk-in fiche on a 3-séance couronne, saved, reopened and saved twice more | exactly 1 séance done (SQL) | browser + sql |
| B3 | A | Visit booked on séance 2, séance 2 then removed from the devis (API) → open that visit's fiche deep link, Enregistrer | fiche saves; visit keeps the act, step link null (SQL) | browser + sql |
| B6 | A | New fiche via « Ajouter un acte », pick the couronne devis act (no visit) → « Séance » picker | picker lists pending séances; pick « Scellement »; save → that step done (SQL) | browser + sql |
| C1 | A | Reopen a fiche linked to a devis act, pick « Aucun » | card shows a price field again (tarif), save → act detached (SQL: step/act undone, fiche link null) | browser + sql |
| C3 | B | New fiche, pick the act card first, then link the devis act by hand | card shows « Aucun honoraire sur cette séance » | browser |
| D1 | A | Devis with a future booked visit → « Arrêter le traitement » dialog (nothing delivered → cancel branch) | dialog shows « Les rendez-vous prévus seront libérés. »; confirm with motif → visit Cancelled, no link (SQL) | browser + sql |
| D2 | A | Followed treatment (Draft) with a booked visit → « Supprimer le traitement » | dialog text names the release; confirm → visit Cancelled (SQL) | browser + sql |
| D3 | A | Stop (API) a devis with one delivered act + one booked parked act | booked visit Cancelled, link null | api + sql |
| D4 | B | Disregard (API) a visit booked on a devis step → devis workspace | act reads « À planifier », not « planifié » | browser |
| C-320 | C | B6's fiche modal at 320 px with the Séance picker | no horizontal overflow of the dialog | browser |
| C-730 | C | A1's edit dialog at 1536×730 | Enregistrer reachable | browser |
| R1 | D | Ordinary visit (no devis) → edit notes, save | saves | browser |
| R2 | D | Create dialog from the devis workspace « Planifier » on a live act | booking saves, linked (SQL) | browser + sql |
| R3 | D | Amend (API) removing a booked, not-done act | visit Cancelled (existing behaviour) | api + sql |
| R4 | D | Fiche from a booked live devis step (deep link) | price locked, save marks that step done | browser + sql |
