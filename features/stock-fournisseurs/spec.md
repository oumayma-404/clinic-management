# Feature Specification: Fournisseurs (suppliers) for the stockroom

**Status:** APPROVED
**Type:** Small
**Created:** 2026-08-15
**Scope:** Full
**Feature:** A fournisseur is a real record with a category and a phone; a stock item links to one, and a low-stock alert offers to WhatsApp them.

## Overview
`StockItem.Supplier` is free text today — a name nobody can call. This turns it into a **`Supplier`** aggregate (nom, catégorie, téléphone, adresse, notes) with its own page, links each stock item to at most one, and puts a **WhatsApp** action wherever a supplier's name appears — including the « Stock faible » notification, whose whole point is « commander chez qui ? ». Categories on both sides follow the repo's existing `ProcedureTypeCategories` shape — a canonical French suggestion list, open to whatever the practice types, canonicalised server-side — so **no category table and no category CRUD screen**.

## What Changes
- New `Supplier` aggregate (clinic-scoped, tenant-filtered) + `/fournisseurs` page in the « Gestion » zone: searchable, category-filtered, paged list with create/edit/delete and a per-row WhatsApp action.
- `StockItem.Supplier` (string) is **replaced** by a nullable `SupplierId` FK; the migration creates one supplier per distinct existing name per clinic, links the items, then drops the column.
- The stock item form's « Fournisseur » text input becomes a searchable picker (« Aucun » + « + Créer un fournisseur » inline); the stock table's Fournisseur cell gains the WhatsApp action.
- Stock categories become the same open-with-suggestions field as supplier categories: the six English keys (`Medical Supplies`, …) are rewritten to their French labels by the migration, and `STOCK_CATEGORIES`/`STOCK_CATEGORY_LABELS_FR` are deleted.
- A low-stock `StaffNotification` row for an item that has a supplier renders « Contacter {fournisseur} » plus a WhatsApp button in the bell panel.
- One authority for the link: `web/lib/whatsapp.ts` builds every `wa.me` URL, over the existing `toE164Tunisian`.

## Acceptance Criteria
- **AC-1:** Creating a fournisseur requires **only a nom**; catégorie, téléphone, adresse and notes are optional. A duplicate nom in the same clinic is refused with a French message naming the existing record.
- **AC-2:** The catégorie field (both fournisseur and stock article) offers the canonical French list **and** the clinic's own existing values, accepts a new one typed in, and folds case/accents/punctuation server-side so « prothèse » and « Prothese » are one category.
- **AC-3:** A fournisseur with a deliverable Tunisian number shows a **WhatsApp** action that opens `wa.me` in a new tab with no pre-filled text. With no number — including every record the migration created — the action is replaced by « Ajouter un numéro », which opens the edit form. Never a disabled or absent control with no explanation.
- **AC-4:** Deleting a fournisseur linked to stock articles is **refused**, naming the count; « Désactiver » removes it from the pickers while existing links keep resolving and keep their WhatsApp action.
- **AC-5:** A stock article may have no fournisseur (the common case) and never more than one; clearing it is possible, not only overwriting it.
- **AC-6:** A « Stock faible » bell row whose article has a fournisseur reads « Contacter {fournisseur} » and carries a WhatsApp button pre-filled with a French order message naming the article and its on-hand figure. Tapping the row's **text** still lands on `/stock` with the article highlighted, as it does today.
- **AC-7:** The supplier shown on that row is resolved **at read time** from the article's current link — so adding a fournisseur to an article after the alert fired makes that existing alert actionable, and the button can never name a supplier the article no longer has.
- **AC-8:** After the migration, no stock article names a supplier that is not a `Supplier` row, and the count of created suppliers is reported by `verify-schema` (`supplier-links-backfill`).
- **AC-9:** At 320 px the fournisseurs list renders as cards with no horizontal scroll; WhatsApp is a **visible action on the card**, not folded into the « ⋯ » menu, and every target meets the 44 px coarse floor. Floor: `~/.claude/skills/DEVICE-CONTRACT.md`.

## API Contract
### GET /api/suppliers
Query: `q?`, `category?`, `includeInactive?`, `page?`, `pageSize?`
Response 200: `{ items: SupplierDto[], categories: string[], page, pageSize, totalCount, totalPages }`
`SupplierDto`: `{ id, name, category?, phoneNumber?, phoneE164?, address?, notes?, isActive, linkedItemCount, version, createdAt, updatedAt? }`

### POST /api/suppliers · PUT /api/suppliers/{id}
Request: `{ name, category?, phoneNumber?, address?, notes?, isActive? }` (PUT echoes `version`)
Errors: `400 supplier_duplicate — Un fournisseur « X » existe déjà.` · `409` on a concurrency conflict

### DELETE /api/suppliers/{id}
Errors: `400 supplier_in_use — N article(s) de stock référencent ce fournisseur. Désactivez-le plutôt.`

### Modified
- `POST/PUT /api/stock` — `supplier: string` → `supplierId: Guid?` (tri-state on update: omitted = unchanged, `null` = cleared).
- `GET /api/stock` — `StockItemDto.supplier` → `supplierId` + `supplierName` + `supplierPhoneE164`.
- `GET /api/notifications` — `StaffNotificationDto` gains `supplierName?` + `supplierPhoneE164?`, populated for `LowStock` rows only.

Controller policy: `AnyClinicRole` throughout — ordering supplies is reception's job, and this is not clinic-wide money.

## Data / Schema Changes
- **`Supplier`** (new, `AggregateRoot<Guid>`): `ClinicId`, `Name` (required), `Category?`, `PhoneNumber?`, `Address?` (one free-text line, **not** the `Address` VO — a supplier has no state/zip to require), `Notes?`, `IsActive` (default true), `CreatedAt`, `UpdatedAt?`. Unique index on `(ClinicId, Name)`; index on `(ClinicId, Category)`.
- **`StockItem`**: `Supplier` (string) **dropped**, `SupplierId Guid?` added with an FK to `Suppliers` (`ON DELETE RESTRICT` — AC-4 is enforced in the handler with a French message, and the constraint is the backstop).
- **Migration** (one, with two backfills): create a `Supplier` per distinct non-empty trimmed `StockItem.Supplier` per clinic and point the items at it; rewrite the six English category keys on `StockItem.Category` to their French labels.
- `Domain/Services/SupplierCategories` + `Domain/Services/StockCategories`: `Canonical` / `IsCanonical` / `Normalize`, mirroring `ProcedureTypeCategories` — the entity ctor and every update path go through `Normalize`.
- Realtime: `Features/Suppliers` emits the `suppliers` key, which **must** be declared in `web/lib/realtime/clinic-hub.ts` or `RealtimeResourceResolverTests` fails in both directions.

## Device Behaviour
- **Leading device:** tablet at the desk; the low-stock → WhatsApp path is used on a phone.
- **Narrow width (< 768):** the fournisseurs list is `CARDS_ONLY` — nom + catégorie badge as identity, then téléphone, then « N articles liés »; WhatsApp and « Modifier » are visible actions, delete/deactivate live in the « ⋯ » menu. The create/edit dialog is a full-screen `Sheet` in `dvh` with a sticky footer. Empty / filtered / failed stay three distinct states.
- **Touch:** WhatsApp, « ⋯ » and the picker rows grow their own box (`coarse:size-11` / `coarse:py-3`) — they sit in a row, so `.touch-target` would steal neighbouring taps.

## Out of Scope
- Purchase orders, order history, prices per supplier, deliveries, or anything that turns « contacter » into a transaction.
- Sending a message from the server (this is a `wa.me` deep link the user sends themselves — it never touches the reminder outbox, the WhatsApp Business template or the vendor messaging forfait).
- Suppliers for anything but stock (labo prothèse stays `LabWorkOrder`'s own free text).

## Edge Cases
- A supplier phone that is not a deliverable Tunisian number is **stored** but produces no WhatsApp action (AC-3's « Ajouter un numéro » path); refusing the save would lose a real foreign number.
- Two existing stock items whose free-text supplier differs only by case/whitespace fold into **one** supplier row on migration.
- The article is deleted after the alert fired: the bell row keeps its stored message and simply loses the contact line and button (AC-7's read-time resolution returns nothing).
- A deactivated supplier still linked to articles keeps rendering its name and its WhatsApp action everywhere — deactivation hides it from *pickers*, it does not erase it.
