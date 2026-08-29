# Mobile audit — dialogs, tablet portrait, interaction state (2026-08-29)

Scope for this pass (narrowed mid-run — the plain 390px route sweep is covered by other agents with a
measurement harness): dialogs/sheets at 390×844, the full tablet-portrait (820×1024) sweep, and interaction
state (dropdowns/selects/comboboxes/collapsibles/toasts) at 390px. Findings from my earlier, broader pass are
folded in at the end, clearly marked.

**Environment caveat, unchanged from the earlier pass:** `matchMedia('(pointer: coarse)').matches` is `false`
throughout this session regardless of viewport width (Playwright emulates a mouse, not touch, and cannot be
made to report coarse from here) — so no touch-target sizes are reported anywhere in this file, per
instruction. Every finding below is a viewport-width / DOM-measurement fact, independent of pointer type.

## Findings, worst first

### 1. [Degraded, recurring — now confirmed on 8 screens] Tables that never got the `lg:` card hinge clip their own Actions column at tablet portrait (820px)

This is the same defect reported in the earlier pass, now with the tablet sweep extended to every remaining
route. Eight files import the plain `CARDS_ONLY`/`TABLE_ONLY` pair (`md:` = 768px hinge) instead of the `_LG`
(1024px) pair, so at 820px — with the 256px sidebar rail still present — each table's own columns overflow its
~451–500px card box, and the row's own Actions menu (edit/delete/etc.) is clipped, reachable only via an
unlabeled horizontal scrollbar inside the card:

| Route | File : line | Columns | Wrapper width | Table width | Columns clipped |
|---|---|---|---|---|---|
| `/users` | `web/components/user-management.tsx:500,598` | 6 | 466px | 1013px | Rôle, Statut, Dernière connexion, **Actions** (4/6) |
| `/dental-acts` | `web/components/dental-acts-table.tsx:269,327` | 8 | 451px | 960px | Catégorie, Tarif, Statut, **Actions** (4/8) |
| `/cnam-nomenclature` | `web/components/cnam-nomenclature-table.tsx:250,302` | 7 | 451px | 921px | Catégorie, Statut, **Actions** (3/7) |
| `/journal` | `web/app/journal/page.tsx:292,328` | 5 | 501px | 816px | Dossier, **Détail** (2/5) |
| `/waiting-list` | `web/app/waiting-list/page.tsx:489,548` | 6 | 466px | 787px | Date d'ajout, **Actions** (2/6) |
| `/patients` | `web/components/patients-table.tsx:418,510` | 6 | 451px | 771px | Signalements, **Actions** (2/6) |
| `/medications` | `web/components/medication-catalog-table.tsx:248,299` | 6 | 451px | 755px | Statut, **Actions** (2/6) |
| `/rappels` (delivery log) | `web/components/rappels/reminder-log-table.tsx:162,196` | 6 | 499px | 693px | Prévu, **Statut** (2/6) |

Screenshots: `820-users-table.png`, `820-patients-table-scrolled.png` (proves the Actions column only appears
after an inner horizontal scroll), `820-waiting-list.png`, `820-journal.png`.

**Every other table checked at 820px is already fixed and renders as cards, zero clipping**: `/stock`,
`/factures`, `/cheques`, `/treatment-plans`, `/fournisseurs`, `/procedure-types`, `/caisse`
(caisse-ledger-table), `/lab-orders`, `/abonnement` (subscription-history-table), `/a-cloturer`. The project's
own `web/CLAUDE.md` documents this exact fix and names the tables that already carry it (`/lab-orders`,
`cheques-table`, `stock-table`, `procedure-types`, `suppliers`, `caisse-ledger-table`, `invoices-table`,
`treatment-plans-table`) — the eight files above are the ones that were never switched over. `suppliers-table`
in particular documents this precise defect having been fixed for that exact reason ("the Actions column — the
WhatsApp button — sat off screen at 820 px"), which is strong confirmation the same fix is what's missing on
the eight above. **One fix (swap the import and the two JSX props on eight files), not eight investigations.**

Non-clipped tables also checked clean at 820px for completeness: `/dashboard`, `/a-cloturer`, `/documents`
(card grid, no table), `/fichiers` (card grid, no table), `/settings`, `/securite`, `/mon-profil`. All: `over:
0` on the documented measurement.

### 2. [Cosmetic, judgement call] A dropdown menu's right edge sits 0.275px past the viewport at 390px

On the edit-appointment sheet (`/appointments?appointmentId=<guid>`), opening "Autres actions" → the menu's
`getBoundingClientRect().right` measured `390.275` against a 390px-wide viewport — a sub-pixel rounding
artifact, not a real clip (the menu's full text, "Annuler le rendez-vous", is completely visible in the
screenshot). Not counting this as a defect; noting it only because it's the kind of number that looks alarming
in a raw measurement. The menu visually overlays the sheet's sticky footer ("Enregistrer"/"Fermer") while open
— that is ordinary dropdown-over-content behavior (Radix flips the menu upward when it's near the bottom of
the viewport, which is what happened here) and disappears once an item is picked or the menu is dismissed.
Screenshot: `dlg-edit-appointment-more-actions.png`.

### 3. [No defects found] Every dialog/sheet checked at 390px is correct

- **"Nouveau rendez-vous"** (create-appointment-dialog, via `/appointments` → `button[data-size="sm"]:has-text('Nouveau')`): full-height sheet (`data-mobile="sheet"`, `inset-0 h-dvh`), sticky footer, body scrolls to reveal every field including the act picker and "Notes et options". `over: 0`. Screenshot: `dlg-nouveau-rdv-patient-combobox.png`.
- **Edit-appointment modal** (`/appointments?appointmentId=<guid>`): same full-height sheet shape. Verified the body's own scroll container reaches `scrollHeight: 869` inside a `clientHeight: 573` box and the sticky footer ("Enregistrer"/"Fermer") stays visible and reachable throughout. `over: 0`. Screenshots: `dlg-edit-appointment-check.png`, `dlg-edit-appointment-scrolled.png`.
- **"Ajouter une fiche médicale"** (patient-record-modal, via `/patients/<id>?addRecord=1&appointmentId=<id>`): full-height sheet; the act-picker list is its own bounded `overflow-y-auto` (`scrollbar-thin max-h-[290px]`, 1156px of content in a 290px box — reachable, not clipped); the "Ajouter un autre acte" button at the very bottom of the form is fully within the viewport (`bottom: 820` vs `innerHeight: 844`). `over: 0`. Screenshot: `dlg-patient-record.png`.
- **"Ajouter un patient"** (edit-patient-dialog, via `/patients`): full-height sheet, sticky footer. `over: 0`. Screenshot: `390-add-patient-dialog.png` (earlier pass).
- **Confirmation "Supprimer cet article ?"** (stock, an `alertdialog` not a `dialog`): renders as a bottom sheet, names the record ("Aiguilles 30 G courtes"), destructive-red primary button, "Annuler" secondary. `over: 0`. Screenshot: `390-stock-delete-confirm.png` (earlier pass).
- **Confirmation "Annuler le rendez-vous ?"** (nested `alertdialog` opened from inside the edit-appointment sheet's "Autres actions" menu): also a bottom sheet, layered correctly over the parent sheet, names the patient ("Nadia Jelassi"), states the action is reversible, destructive-red "Oui, annuler le rendez-vous" vs. "Non, conserver". `over: 0`. Screenshot: `dlg-cancel-appointment-confirm.png`. (Declined the cancellation — no data was mutated.)
- **Dialog at 820px** ("Nouveau rendez-vous" reopened at tablet width): correctly renders as a normal **centered dialog** with a visible gutter, not a sheet — this is correct per the project's own rule (a sheet is only mandated below `md:` = 768px). `over: 0`. Screenshot: `820-appointments-new-dialog.png`.

### 4. [No defects found] Every interaction-state surface checked at 390px is correct

- **Patient combobox** inside "Nouveau rendez-vous": opens as a full-width, self-contained list with its own scrollbar, well within the viewport. `over: 0`. Screenshot: `dlg-nouveau-rdv-patient-combobox.png`.
- **Multi-select act picker** ("Actes du rendez-vous"): opens and **stays open** (by design, per the project's own component notes) as a bounded panel below its trigger; does **not** cover the sheet's sticky footer, which remained visible the whole time. `over: 0`. Screenshot: `dlg-nouveau-rdv-actes-open.png`.
- **Native-styled Select** ("Mode: Espèces" inside the fiche médicale dialog): opens correctly positioned, all four options visible, no clipping. Screenshot: `dlg-patient-record-select-open.png`.
- **Inline collapsible "Filtres"** (agenda bar, phone `<details>`-style disclosure): expands in-flow (two switches: "Terminés affichés" / "Annulés affichés"), no popover, no clipping. `over: 0`. Screenshot: `dlg-appointments-filtres-popover.png`.
- **Inline collapsible "Légende"**: expands in-flow, shows both the statut and acte legend groups plus the grid key, no clipping. `over: 0`. Screenshot: `dlg-appointments-legend-expanded.png`.
- **Notification bell panel**: opens as a well-positioned, self-scrolling popover reaching near the right edge with margin to spare; not clipped. `over: 0`. Screenshot: `dlg-notification-panel.png`. (Incidentally revealed several "Session interrompue" notifications — see note below.)
- **Client-side validation state** (submitting "Nouveau rendez-vous" with no patient selected): renders the shared inline `form-error-banner` ("Sélectionnez un patient.") inside the dialog body — not a toast — dialog stays open, every typed field intact, no overflow. This matches the project's documented convention (`showErrorToast` / inline banner, dialog never closes on error). Screenshot: `dlg-toast-validation.png`.

### Testing note, not a rendering defect

Mid-session the auth cookie was invalidated several times with no code change on my end. The notification
panel explains why: "Session interrompue : un identifiant déjà remplacé a été présenté" — repeated calls to
the session-refresh helper each replace the previous session, so back-to-back refreshes raced each other.
Not a UI bug; just why this pass needed several `refresh-session.mjs` calls.

## Folded in from the earlier (now-superseded) route-sweep pass

The full 390px/320px route sweep is being redone by other agents with a measurement harness, so it is not
repeated here. Kept for continuity, unchanged from the first report:

- **0 confirmed document-level horizontal overflows and 0 confirmed third-scrollbar bugs** across all 27
  requested routes at both 390px and 320px.
- **Odontogram tooth-chart scroller (patient file page) is correctly contained** — re-verified at 390 and
  320px: `scrollLeft: 0` shows tooth 18 first, the scroller's own bounding box never exceeds the viewport, and
  the document itself never overflows. The historical "teeth 18–15 unreachable" bug is fixed.
- **`/fournisseurs` search box, historically "4px wide on a phone"**, is now full-width at 390px — fixed.
- `/creances` and `/recurring-series` render a deliberate "Page retirée" placeholder (the features were
  withdrawn) — by design, not a bug.

All screenshots (both passes) are under
`C:\Users\Oumayma Benkhalifa\Desktop\clinic-management\follow-up\mobile-audit-shots\`, with this pass's files
prefixed `dlg-` or `820-`.
