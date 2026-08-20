# stock-fournisseurs — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## A fournisseur is a record with a number, not a name on a row (`stock-fournisseurs`)

`StockItem.Supplier` and
`LabWorkOrder.Prosthetist` were free text — the same dépôt under three spellings, none with a number behind it —
so « Stock faible » told a dentist *what* had run out and a bon en retard told them *what* was late, while
neither could answer « qui est-ce que j'appelle ? », which is the only question either alert leads to. There is
now a **`Supplier`** aggregate (nom · catégorie · téléphone · adresse · notes · actif), `/fournisseurs` in the
« Gestion » zone, `GET/POST/PUT/DELETE /api/suppliers` (`AnyClinicRole` throughout — ordering supplies and
chasing a prothèse is reception's job, and none of it is clinic-wide money), and a **WhatsApp** action wherever a
supplier's name appears: the fournisseurs list, the stock table, the laboratory board and the « Stock faible »
bell row.
⚠️ **It deliberately covers more than the stockroom.** The prothésiste who makes the crowns, the laboratory that
reads a biopsy, the dépôt that delivers the composite and the technician who services the fauteuil are one kind
of record, so `StockItem.SupplierId` **and** `LabWorkOrder.SupplierId` both point here rather than each carrying
its own free-text name. `LabWorkOrder.Prosthetist` is **kept beside** the link (unlike the stock column, which is
dropped): the name is what is printed on the bon, and a laboratory used once must be recordable without first
filing a fiche.
⚠️ **The migration's statement order is the design.** EF scaffolded `DropColumn("Supplier")` as the *first*
statement — it cannot know the backfill below reads that column — which would have created zero suppliers and
linked zero articles on every existing database while reporting a clean migration. It also emitted an **`xmin`**
column PostgreSQL rejects, the same defect `AddClinicSubscriptions` hit. Rows fold on `lower(btrim(…))` so
« Dentalex » and « dentalex  » become one (EC-2), the lab pass runs **after** the stock pass with a `NOT EXISTS`
so a dépôt that is both reuses one row, and every backfill is gated so `Up()` re-runs safely.
⚠️ **AC-3's two states are the feature's whole UX**: a deliverable Tunisian number gets a `wa.me` link, and one
without gets « **Ajouter un numéro** » — never a disabled control and never an absent one, because a greyed icon
says « broken » while the truth (nobody recorded a number) is fixable in seconds if the user is told. A
non-Tunisian number is **stored** rather than refused (EC-1); what it costs is the action, not the record.
Deliverability is resolved **server-side** (`phoneE164`), so four surfaces cannot disagree about who is reachable,
and `web/lib/whatsapp.ts` is the one builder of every `wa.me` URL.
⚠️ **The bell row resolves its supplier at READ time from the article's current link** (AC-7), never frozen into
the notification's message: an alert fired last week becomes actionable the moment somebody files the supplier,
and it can never name one the article no longer has. A deleted article simply loses the contact line (EC-3).
⚠️ **Deleting a referenced fournisseur is refused with the counts named per table** — « 3 articles de stock » and
« 2 bons de prothèse » send somebody to two different screens, where a bare « 5 » sends them to the wrong one —
and « Désactiver » is the route: it hides the contact from the **pickers** while every existing link keeps
rendering its name and its WhatsApp action (AC-4, EC-4).
⚠️ **The six English stock categories are retired.** They were a *closed* set mapped to French at display time,
this repo's standing convention — but the set had stopped being closed (`GET /api/stock` already served the
clinic's own categories as a filter facet), so a clinic-authored one rendered raw beside six translated ones.
Both category fields are now open-with-suggestions over `Domain/Services/CategoryFolding`, which also absorbed
`ProcedureTypeCategories`' private fold so three open sets share one rule.
⚠️ `verify-schema` gained **`supplier-links-backfill`**: it counts bons still unlinked while a fournisseur of
their name exists — the one failure invisible everywhere else, since an unlinked bon renders exactly like a
laboratory nobody has filed. The stock side has no equivalent line **and cannot**, because its free-text column
is dropped by the same migration.
