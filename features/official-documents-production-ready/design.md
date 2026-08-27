# Design: Official Documents — Production-Ready

**Status:** APPROVED
**Created:** 2026-07-20
**Source spec:** `spec.md` (APPROVED, Challenged: Yes) · **Test plan:** `test-plan-integration.md` (APPROVED)

> **Exploration method.** No browser tooling (`agent-browser`), dev scripts, or prior screenshots exist in this repo, so visual exploration was done by reading the actual frontend source (4 parallel Explore agents over `web/`). For shadcn/ui + Tailwind this yields exact tokens/classes; mockups are grounded in real component code, not guesses. Mockups are static HTML in `mockups/` (Tailwind via CDN) — open them directly in a browser.

## Design System Summary (as found in `web/`)

- **Framework:** Next.js 15 App Router, Tailwind **v4** (CSS `@theme`, no config file), shadcn/ui "new-york" style, **Geist** font.
- **Palette (light, oklch→hex approx):** primary `#2f6ac9` (blue), background `#fcfcfc`, card `#ffffff`, foreground `#1f1f1f`, muted `#f3f4f6`, muted-foreground `#6b7280`, accent `#eef2f9` / accent-foreground `#1e4585`, destructive `#dc4b3e`, border `#e5e7eb`. Radius base 8px (`rounded-lg`), cards `rounded-xl`. Dark mode exists but app defaults to light.
- **App shell:** `flex h-screen bg-background` → `DashboardSidebar` (`w-64`, `border-r`, `bg-card`) + `flex-1 flex-col` (`DashboardHeader` h-16 + `main p-6` → `mx-auto max-w-7xl space-y-6`).
- **Primitives:** Button (default/outline/ghost/destructive; `h-9 px-4`, `h-8 px-3` sm), Badge (rounded-full, variants default/secondary/outline), Input (`h-9 rounded-md border-input`), Dialog (`max-w-lg`, overlay `bg-black/50`), Card (`rounded-xl border shadow-sm py-6`, header/content `px-6`), Table (`text-sm`, head `h-10`, row `hover:bg-muted/50 border-b`), Select/Popover+Command combobox.
- **Language:** MIXED — app chrome is English, but the documents/finance/setup modules are French. **Decision: all new/edited surfaces in this feature use French** (consistent with the documents module and the clinical/fiscal domain), even though the procedure-types mirror is English.
- **Currency:** `DT` suffix (e.g. `70.00 DT`); invoices use 3-decimal millimes (`60.000 DT`).

## Screen Inventory

| # | Screen | Type | Mockup |
|---|--------|------|--------|
| 1 | CNAM nomenclature admin screen (`/cnam-nomenclature`) | **New page** | `mockups/01-cnam-nomenclature.html` |
| 2 | Mon profil (practitioner self-service) | **New page** | `mockups/02-mon-profil.html` |
| 3 | Certificat editor form + preview | **Modified** | `mockups/03-certificat-form.html` |
| 4 | Lettre de liaison editor form + preview | **Modified** | `mockups/04-liaison-form.html` |
| 5 | Note d'honoraires → facture flow | **Modified flow** | `mockups/05-honoraires-flow.html` |

---

## 1. CNAM Nomenclature Admin Screen (new, `/cnam-nomenclature`)

**Layout decision:** one page, two stacked cards (not tabs) — everything visible at once.
- **Provisional banner** (FR-5.1): amber `border-amber-300 bg-amber-50` banner at top with a **"Confirmer les données"** button. Shown while any entry/VLC value carries the "à vérifier" flag; disappears once an admin confirms. Nothing is blocked while it shows.
- **Catalog card** — mirrors `procedure-types-table.tsx`: Card header with icon + title + count Badge + **"Ajouter un acte"** primary button; table columns **Code acte · Désignation (FR) · Lettre clé · Coefficient · Catégorie · Statut · Actions**. Lettre-clé shown as an outline Badge. **Statut** column shows an amber `à vérifier` or green `vérifié` pill. Row actions: ghost edit (✏️) + ghost destructive deactivate (🗑️) with an AlertDialog confirm.
- **VLC card** (FR-5.2): a `grid` of small bordered tiles, one per lettre clé (`CD/CDS/VD/D/RD…`), each showing the dinar value (`8.000 DT`) + its provisional pill. A **"Modifier"** button flips tiles to inline number inputs (edit state shown in the mockup with a primary-bordered tile).
- **Add/Edit dialog** — mirrors `procedure-type-form-modal.tsx`: fields Code acte* / Lettre clé* (select) / Désignation* / Coefficient* / Catégorie, a red inline error banner (e.g. **duplicate CodeActe** → "Un acte portant le code « CONS » existe déjà."), Annuler / "Ajouter l'acte" footer.

**Admin-gating (new — the procedure-types mirror is NOT gated):**
- Nav entry added to the **admin-only** array (`mode === "local" && user?.role === "admin"`) in `dashboard-sidebar.tsx`, with an `admin` chip.
- The page itself must also enforce the role (ClinicGuard alone doesn't check role) — non-admins redirected / shown "réservé aux administrateurs". Reads remain available to all authenticated users where the catalog is consumed (bulletin editor), per FR-5.3.

**States:** loading ("Chargement du catalogue…"), empty (table empty-row with an "Ajouter le premier acte" button), error (red banner in card), populated (shown).

---

## 2. Mon Profil (new practitioner self-service screen)

**Decision:** a dedicated **"Mon profil"** page for the logged-in practitioner (chosen over adding fields only to the Settings → Doctors card).
- **Identité card** — read-only nom/spécialité snapshot (managed in Settings → Médecins).
- **Informations pour les documents card** (editable, own record):
  - **Numéro d'ordre (CNOMDT)** text input (FR-2.5) — label spells out "Ordre National des Médecins Dentistes (CNOMDT)".
  - **Cachet / signature** upload — mirrors the clinic-logo dropzone (`clinic-settings.tsx`): preview thumbnail with hover-remove, dashed dropzone (hidden `<input type="file" accept="image/*">`), helper noting PNG/JPEG, content-type preserved, and the no-cachet fallback (plain signature line).
  - Annuler / Enregistrer footer.
- **Admin path note** (FR-3.1): admins set *another* practitioner's ordre number + cachet from **Settings → Médecins** — the same two fields (`Ordre CNOMDT` + cachet upload) are added to each doctor card in that existing section. Authorization (own-or-admin) enforced by the API. This keeps self-service and admin-manage-others both covered without a per-doctor route.

---

## 3. Certificat Editor (modified)

Two-pane editor unchanged in structure (left `w-[420px]` form, right A4 preview card).
- **Form (left):** patient combobox + date, then **"Objet / motif"** textarea as the primary required field (FR-2.1), then a **collapsible "Repos médical (optionnel)"** section (chosen layout) holding *Date de début* + *Durée (jours)*. **Numéro d'ordre (CNOMDT)** shown pre-filled + disabled with a "modifiable dans Mon profil" hint (FR-2.5).
- **Preview (right), now NON-editable (FR-6.3):** header reads "Aperçu en lecture seule — modifiez via le formulaire". Letterhead uses the cabinet's city → **"Tunis, le 20/07/2026"** (never "Paris", FR-6.1). Body renders objet/motif + (conditionally) the repos sentence, with the ordre label **"Ordre National des Médecins Dentistes (CNOMDT)"** (FR-2.4). The mandatory mention **"Certificat établi à la demande de l'intéressé(e) et remis en main propre."** sits above the signature (FR-2.3). Signature area shows the practitioner **cachet image** (FR-3.2), falling back to a plain line if none.

---

## 4. Lettre de Liaison Editor (modified)

- **Form (left):** patient combobox, then an **external "Confrère destinataire" block** (free-text **Nom*** / Spécialité / Adresse) replacing the clinic-doctor `Select` (FR-4.1). Then discrete guided fields — **Motif, Examen clinique, Examen radiologique, Actes réalisés, Prescriptions (posologie/durée)** — all optional except recipient name (FR-4.2).
- **Preview (right), non-editable:** cabinet letterhead → recipient block → "Tunis, le …" → guided sections rendered **only when filled** (empty ones omitted, FR-4.2) → practitioner cachet (FR-4.3). The old single free-text `contentEditable` write-back box is removed (superseded by the structured fields, FR-6.3).

---

## 5. Note d'Honoraires → Facture Flow (modified)

The "Note d'honoraires" card no longer opens the document editor (FR-1). New 3-step flow (mockup shows all three side by side):
1. **Card click** → opens a **patient-selection dialog** (Popover+Command combobox pattern, "Rechercher un patient...").
2. **Patient chosen** → the existing **`InvoiceFormModal`** opens with the patient locked and **actes pre-seeded from the patient's not-yet-invoiced dental records** (FR-1.2, reusing `presetPatientId`/`presetLines`/`dentalRecordId`).
3. **"Créer le brouillon"** → creates a **draft** invoice (no number consumed); user lands in the Factures context where it can be issued (number + TVA + timbre + El Fatoora) — no auto-issue (FR-1.3).

The `honoraires` document type is removed from the editor; legacy euro notes stay viewable in the patient file (FR-1.4/1.5).

---

## Design Decisions & Rationale

| Decision | Rationale |
|---|---|
| French for all new/edited surfaces | Matches the documents/finance module and the Tunisian clinical/fiscal domain; diverges deliberately from the English procedure-types mirror. |
| CNAM screen = two stacked cards (not tabs) | Catalog + VLC visible together; VLC is small and benefits from being seen alongside the acts that use it. |
| Cachet/ordre = dedicated "Mon profil" page | Faithful to "that doctor edits their own record"; admin-manage-others handled via existing Settings → Médecins cards. |
| Collapsible repos block | Keeps the common certificat case (objet/motif only) clean; repos available on demand. |
| Reuse `InvoiceFormModal` for honoraires | No parallel invoice system; the compliant pipeline already exists (spec non-functional hint). |
| Cachet dropzone mirrors clinic-logo control | Established pattern; only change is persisting the real content type (logo bug not copied). |

## Accessibility Notes

- All inputs keep visible `<label>`s; required fields marked with `text-destructive *`.
- Comboboxes use the existing Popover+Command pattern (keyboard nav, `role="combobox"`, `aria-expanded`).
- Preview panes become non-interactive (no `contentEditable`), removing a confusing/lossy edit surface (FR-6.3).
- Cachet upload uses a real `<label>`-wrapped file input (keyboard focusable); remove action is a real button with title.
- Status pills use text ("à vérifier"/"vérifié"), not color alone.

## Responsive Behavior

Desktop-first (clinic workstation app), consistent with the rest of the app. The CNAM catalog table scrolls horizontally inside `overflow-x-auto`; the VLC grid collapses `grid-cols-5 → 3 → 2`. The document editor's two-pane layout is desktop-oriented as today (not re-flowed for mobile — out of scope).

## Mockup Index

| File | Shows |
|---|---|
| `mockups/01-cnam-nomenclature.html` | Full admin screen: banner, catalog table (provisional + verified rows), VLC card (read + edit tile), add-entry dialog with duplicate-code error |
| `mockups/02-mon-profil.html` | Practitioner profile: CNOMDT number, cachet upload (preview + dropzone), admin-path note |
| `mockups/03-certificat-form.html` | Restructured certificat: objet/motif, collapsible repos, non-editable A4 preview with mention + CNOMDT + cachet + Tunis date |
| `mockups/04-liaison-form.html` | Restructured liaison: external recipient block, guided fields, preview omitting empty sections |
| `mockups/05-honoraires-flow.html` | 3-step honoraires flow: card → patient picker → seeded InvoiceFormModal draft |

## Out of Scope (design)

- BS1 CNAM bulletin overlay (already correct; untouched except consuming the verified nomenclature).
- Invoice PDF / El Fatoora visuals (reused unchanged).
- Mobile re-flow of the document editor.
- The reimbursement estimate's exact in-editor placement in the bulletin editor (indicative figure; minor, decided at implementation) — the value/labelling ("estimation indicative, non contractuelle") is a spec rule, not a new screen.
