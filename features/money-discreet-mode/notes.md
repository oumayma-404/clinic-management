# Mode discret — what shipped

One admin press hides the cabinet's clinic-wide money on every device; an authenticator code brings it back.
Display only — no money row is touched.

## The pieces

| Layer | Piece |
|---|---|
| Domain | `Clinic.IsMoneyHidden` + `HideMoney()` / `ShowMoney()`. Migration `AddClinicMoneyHidden` adds the column only (no `xmin` line). |
| Application | `HideMoneyCommand`, `ShowMoneyCommand`, `ClinicMoneyMask` (action name, refusal code, French sentences). `StepUpCommand` refuses a password for `show-money`. `GetDashboardQuery` skips the money readers while hidden. |
| API | `POST /api/clinics/money/hide` · `POST /api/clinics/money/show` (spends the step-up token first). `MoneyMaskGateMiddleware` + `[HiddenWhenMoneyMasked]` on 13 reads. |
| Web | `MoneyVisibilityProvider` (root layout) → `useMoneyVisibility()`. Rail, bottom bar, `/factures`, `/caisse`, `/cheques`, the dashboard's « L'argent ». `money-discreet-card.tsx` on `/settings`. |

## Decisions that are easy to undo by accident

- **Hiding needs an authenticator on the account.** Showing needs a code, so an admin without one could hide and
  never show again. Both the handler and the card refuse; the card disables the button and links « Sécurité ».
- **A password never confirms « Afficher ».** The step-up has a `CodeOnlyActions` set; every other action still
  takes a password. A wrong code says « Code de vérification incorrect », not « mot de passe ou code ».
- **The dashboard does not zero the figures, it does not read them.** `Money`/`Receivables` are `null` and `Trend`
  is `[]` on the wire. A zero would be a false claim about the practice.
- **`GET /api/invoices?patientId=` stays open** (`UnlessQuery`). The patient file's Factures tab is out of scope.
  An empty `patientId=` is still the clinic list and is refused.
- **`unknown` ≠ `shown`.** Until the first read answers, a Finances page shows the loader and the dashboard leaves
  « L'argent » out — no figure is painted and then withdrawn. The menu keeps the rows during `unknown`, or every
  reload would flicker the rail.
- **Four ways another PC learns of a change**: first read, the `clinics` realtime broadcast, window focus (30 s
  floor), and any 403 `money_hidden` (the page swaps for its card at once, then re-reads).
- **Both POSTs are `[AllowsWithoutSubscription]`** and neither checks the clinic's `Version`: they set one value,
  so another admin's edit of the clinic info never refuses them.
- **`IsMoneyHidden` is a valued audit property**, so the journal shows false → true → false with the actor.

## Guards

- `MoneyMaskGateMiddlewareTests` — the marked set equals the spec list; every marked endpoint is a GET; every
  `billing/*` and `expenses` read is marked (derived from routes, so a new one cannot ship unguarded).
- `ClinicMoneyMaskTests` — hide/show handlers, the code-only step-up, the dashboard read.

## Out of scope (spec)

Money inside one patient's screens, every write, other exports, a per-PC or per-user hide, an automatic re-hide.
