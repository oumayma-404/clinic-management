# list-pagination — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## Every list read is a page, and search is a database question (`list-pagination`)

the lists had **no paging
anywhere** — no backend primitive, no `Skip`/`Take`, no pager — so every table fetched a clinic's entire history and
filtered it in the browser. `Domain/Common/Paging.cs` is now the single authority: **`PagedResult<T>`** (items +
`TotalCount`, because « N résultats » and the page count are the same number and a page carrying only its own rows
cannot tell the client whether there is more) and **`PageRequest`** (clamps, never rejects — a stale bookmark asking
for page 4 of a 3-page list should show rows, not a French error). It lives in **Domain** for one structural reason:
the repository interfaces are there and that project has zero references.
⚠️ **`paging: null` is a first-class case, not a large page** — the pickers, the header lookup, the AI dispatcher and
every money **total** legitimately read everything, and modelling that as "page 1 of size `int.MaxValue`" would put a
bogus `LIMIT 2147483647` in the SQL. On the client the mirror image is `list()` (unwrapped, `T[]`) vs `listPaged()`.
**The load-bearing half is that search moved into SQL.** Free-text filters were in-memory C#, which was *equivalent*
to searching the clinic only because the handler held the clinic; over a page it silently answers a different
question — a patient on page 7 reads as « aucun résultat ». `Application/Common/SearchTerm.cs` normalises the term
(and **escapes LIKE wildcards** — an unescaped `%` matches every row, so the filter appears to do nothing) and
`Infrastructure/Persistence/SqlSearch.cs` maps PostgreSQL **`unaccent`** so `Béchir` matches `bechir` in the
database. A normalised persisted column was rejected: it is a write-path obligation on eleven aggregates, and any
writer that forgets it produces a row invisible to search — indistinguishable from the record not existing.
⚠️ Three traps. (a) **Every paged read must order on a unique column last** (`.ThenBy(x => x.Id)`); `OFFSET` over a
non-unique sort may show a row on two pages and skip another, which looks like "a record vanished".
(b) **An in-memory filter and a SQL page cannot coexist** — the catalogs' `category`/`q`, the patients' flag filter
and the lab orders' patient filter all moved into the repository, because filtering an already-cut window shrinks
pages unpredictably. (c) Paging a list bought nothing while its **companion read** stayed unbounded: the invoice and
devis lists loaded *every* patient of the clinic to resolve names (now `GetByIdsAsync` over the page), and the
recurring-series list read *every* appointment to count occurrences (now one `GROUP BY` over the page's ids).
**Two reads page in memory, deliberately**: « Créances » and the « extrait de caisse » are ordered *unions* of
several ledgers, so no single query knows a row's position — `PagedResult.FromSource` is for exactly those, and the
statement's `RunningBalance` is computed over the **whole window before** filtering or paging, because « Solde de la
période » is a fact about a movement's place in the period, not about the current page.
