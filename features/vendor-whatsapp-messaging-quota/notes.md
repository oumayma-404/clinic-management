# vendor-whatsapp-messaging-quota — shipped notes

What this feature actually does in the code, and the decisions that are easy to undo by accident.
Moved out of the root `CLAUDE.md` verbatim so it is no longer loaded into every session; the root
indexes it under **Architecture notes**. `spec.md` is what was asked for, `stories/` how it was built,
and this is what shipped.

## The vendor buys the WhatsApp messages and a cabinet spends them (`vendor-whatsapp-messaging-quota`, Parts 0–5)

each practice gets a **monthly allowance of WhatsApp appointment reminders**, counted one per message actually
sent; past it a reminder is **held, never dropped** — it goes out when the vendor tops the cabinet up. Gated on
the 18th capability **`SellsVendorMessaging`** (`HostedMultiTenant` only). SMS is never counted, and nothing but
WhatsApp reminders is affected.
⚠️ **The month's allowance is a stored snapshot of a fold, and that is the design's whole risk.**
`MessagingAllowanceEntry` is an append-only ledger (a standing figure, plus one-off top-ups per named month) and
`Domain/Services/MessagingAllowanceLedger` folds it — **pure, total and clock-free**, with the month a
*parameter*. `ClinicMessagingMonth` stores that answer per (cabinet, Tunisian month) because the vendor console
filters and sorts the whole portfolio on consumption-against-allowance **before a page is cut**, which folding N
ledgers cannot serve. Nothing in the model can say the two agree, so `verify-schema` gained
**`monthly-allowance-matches-ledger`**, re-deriving every row through the **real** fold and reporting **both**
directions — a snapshot *above* the fold lets a cabinet send messages nobody allocated, one *below* holds
reminders it has paid for. `subscription-end-date-matches-ledger`'s reasoning, one dimension over.
⚠️ **`null` and `0` are opposite facts and every layer keeps them apart.** No entry reaching a month folds to
**null** — « our bookkeeping is wrong », held under its own `OutboxBlockReason` and its own French sentence —
while `0` is a cabinet the vendor decided sends no WhatsApp reminders. Likewise a **missing counting row**
(« non mesuré ») is not « 0 rappel envoyé »: the row is *provisioned* for every cabinet rather than created on
first send (FR-1a), which is what makes a quiet month distinguishable from a broken counter.
⚠️ **A raise applies this month and a lowering waits for the next; a cancellation reaches every month it fed,
the current one included.** The vendor states an amount and never a month — `MessagingAllowancePlan.Decide` is
the server's single answer, extracted rather than written twice because the console wrapper cannot send the
vendor command (it commits on its own, and the journal row would be a second transaction). ⚠️ A standing figure
of **zero is a lowering** and defers; turning a cabinet off *now* is a cancellation.
⚠️ **Enforcement is a term on the existing `OutboxMessagingGate`, asked at dispatch *and* at un-park** — never a
second gate — so both call sites inherit it, and Part 4's template term slots in ahead of the allowance terms
(a cabinet meeting two conditions is told the one it can act on). It reads **nothing at all** where the
capability is off, and it passes a cabinet with **no stored template status**: reading null as `NotSubmitted`
would have held every WhatsApp reminder on the deployment the day it shipped, for a template Meta approved long
ago.
⚠️ **The counting increment is staged into the dispatcher's own per-row save**, so a crash loses the send and the
unit or neither. The month row is a plain `Entity<Guid>` and deliberately **not** an `AggregateRoot`: it is
incremented minutely, and `AuditSaveChangesInterceptor` writes one row per mutated root — the audited artefact
is the ledger **entry**.
**Surfaces**: the practice sees « il vous reste N rappels » + a twelve-month history on `/rappels` and is warned
at **80 / 95 / 100 %** (all crossed thresholds, deduped on a real column, withdrawn by one reconciling call);
the vendor gets two portfolio columns, a « presque épuisé » filter whose threshold is **one** figure end to end,
a cabinet-file section, and three console verbs — **`messaging-grant` / `messaging-cancel` / `messaging-report`**
(exit 0/1/**2**, `--month` answering for a **closed** month). Operator runbook in
[`deploy/README.md`](deploy/README.md).
⚠️ **Meta's own side is Part 4**: Embedded Signup moved **v3 → v4** (extracted to
`web/lib/hooks/use-whatsapp-embedded-signup.ts` so two surfaces cannot drift back to handling only `FINISH`), the
reminder **template is submitted post-exchange** and its status has two writers — a signed
`message_template_status_update` webhook **and** a reconciling daily poll, neither a substitute for the other —
and `WhatsAppSender.Classify` reads Meta's refusals off the **full** body to tell a throttle from a stopped
number. The template carries **one** body variable, not two: the sender sends one pre-rendered French sentence,
and formatting inside the sender would move the wording away from `ReminderScheduler`, where
`ReminderMessage.AnnouncesStaleMoment` reads it.
⚠️ **Still owed, and none of it is a code gap**: no `Meta:AppId`/`AppSecret` exists on any deployment, so the
template has never been submitted, the webhook never called by Meta and the v4 popup never opened — everything is
verified against documented payload shapes and ~150 unit tests. See
[`follow-up/vendor-messaging-open-questions.md`](follow-up/vendor-messaging-open-questions.md), which also carries
the Graph `v21.0 → v26.0` bump and the two Meta rules the spike could not settle.
