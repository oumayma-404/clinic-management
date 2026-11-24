# Old-software history — a place for what a switching cabinet brings with it

Type: Small · Status: APPROVED (owner, 2026-11-24: « if we do not have a place for it, we create it now »; read-only tab chosen)

## Why

A dentist leaving her old software hands over one PDF per patient. Everything in it must land somewhere in her
new patient file. Five fields already have a place; three do not; and her past care (consultations, ordonnances,
radios, diagnostic) has none — and must never become fiches, which create money, chart marks and debts.

## What

1. **Three new patient fields**, optional, free text, on the record and in « Modifier le patient »:
   - **Profession** (`Occupation`)
   - **Situation familiale** (`MaritalStatus`) — the form offers Célibataire · Marié(e) · Divorcé(e) · Veuf / veuve,
     and the stored value is that French word itself (no enum, so no key/label mirror); an imported value outside
     the list is kept and shown as is.
   - **Pays d'origine** (`CountryOfOrigin`) — nationality, deliberately NOT `Address.Country` (« Guinée » is where a
     patient is from, not where they live).
2. **« Ancien logiciel » tab** on the patient file, read-only: one line per old consultation / ordonnance / radio /
   diagnostic — date (may be absent) · category · title · details. Newest first, PDF order kept within a day.
   No edit, no delete, no money, no chart. Shown only when the patient has such lines.
3. **API**: `GET /api/patients/{id}/legacy-history` (any clinic role) and `POST` (append a batch — the import
   script's door; any clinic role, because the « Import » account is a secretary).
4. **Import script** (`C:\dev\clinic-migration`, outside the repo) sends: sexe → Gender, Tél 2 → additional phone,
   Gouvernorat → Address.State, Adressé par → ReferredBy, the three new fields, and the history lines.

## Rules

- A history line belongs to the patient and the clinic (tenant filter, audited like every clinical child).
- A patient holding history lines cannot be merged away or deleted (same guard as files and antécédents).
- History lines leave with the cabinet archive and with the patient dossier export.
- The tab never offers to create anything from a line.

## Out of scope

- Turning old ordonnances into documents (would reprint a 2022 paper under today's cachet — owner chose not to).
