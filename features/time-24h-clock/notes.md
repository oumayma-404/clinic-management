# L'heure se saisit et se lit sur 24 heures, partout

Tunisian practices work on a 24-hour clock. Every *display* in this product already did — `formatDateTime`,
`formatTimeSpan`, the agenda, `ReminderMessage`, `NotificationGenerator`, `CsvTable` are all `HH:mm` under
`FrCulture`. The **inputs** did not, and the reason is worth writing down because it is not guessable from the
code.

## What was actually wrong

`<input type="time">` renders in the **browser's UI locale**. Not the document's, not `lang`, not the page's
`Intl` settings — the browser's own application language. There is no attribute that overrides it.

Measured in Chrome, 2026-09-13, on a browser whose UI language is en-GB:

| Input | Rendered |
|---|---|
| inherits `<html lang="fr">` | `14:30` |
| `lang="fr"` | `14:30` |
| `lang="ar-TN"` | `14:30` |
| `lang="en-US"` | `14:30` |
| `<input type="date" lang="en-US">` | `13/09/2026` |

All four followed the browser and ignored the attribute. On a Chrome whose UI language is **English (US)** —
the ordinary case on a Windows PC in a clinic — all four would have read `02:30 PM`, and every time in the
product would have been picked and displayed that way.

⚠️ **`ui/time-field.tsx` asserted the opposite in its own docstring** — « it is already localized to a 24-hour
clock by the browser under `lang="fr"` » — which is why nobody looked here. The claim was plausible, written
down, and false.

## What shipped

`ui/time-field.tsx` is a **masked 24-hour text input**: `inputMode="numeric"`, digits only, `HH:MM` always.
`0930` off the numpad, `9` and `930` normalised on blur, `2599` clamped to `23:59`, ↑/↓ stepping five minutes.
It emits on the fourth digit and again on blur — and blur fires on a button's `mousedown`, before its `click`,
so « Enregistrer » sees the time on screen.

`TimeField` (the `HH`/`mm` pair the booking dialogs' validation, duration arithmetic and overlap detection are
written against) is now a thin adapter over it, so the control changed without rippling into their state.

**11 native controls replaced**, all of them: `TimeField` (create + edit appointment dialogs, ×4),
`clinic-settings` (×4), `doctor-working-hours-card` (×4), `setup-wizard` (×2), and the one `datetime-local` on
`/recurring-series`, split into a date field and a time field. Nothing on the wire changed — `HH:mm` in, `HH:mm`
out — so `WorkingHoursDto`, the appointments API and every backend formatter are untouched.

## ⚠️ The trap the replacement created, and the guard that holds it

**A native `type="time"` had an intrinsic width of about 105 px that no flex context could shrink past.** A row
too narrow for two of them therefore *wrapped*. A text input has no such floor, and the first version of this
change did not declare one — so on `/settings` the two optional « Pause » fields collapsed to **26 px at
320 px**, narrower than their own `--:--` placeholder, while `tsc`, `check:responsive` and `npm run build` were
all green. Found only by the eye pass.

`TimeInput` now declares `min-w-[4.5rem]`, and **a call site must not pass `min-w-0`** — same tailwind-merge
group, so the caller wins and the floor is gone. That is `ui/popover.tsx`'s documented trap one primitive over.
`check:responsive`'s **`time-input-keeps-its-width-floor`** fails on it.

⚠️ The sibling `doctor-working-hours-card` did **not** regress, and that is instructive rather than lucky: its
pause row carries `flex-wrap`, so the floor produces a wrap instead of a crush. `clinic-settings`' did not, and
now does. Both rows in both files now wrap.

## What deliberately did not change

- **No backend anything.** Every server-side formatter was already 24-hour under `FrCulture`.
- **No wire format.** The masked control emits the same `HH:mm` the native one did.
- **The native clock popup is gone**, and that is the one capability traded. What replaced it is typing (which
  is how a receptionist actually works, and which the old docstring already argued was the point) plus ↑/↓
  stepping. On a phone `inputMode="numeric"` still raises the numeric keypad. A custom popup picker would be a
  design decision, not a locale fix, and is not here.
- **`/recurring-series` is behind `RetiredPageCard`** and its form is unreachable, so the edit there is inert —
  made for completeness, not for a user.

## Verification

`features/time-24h-clock/qa/` — `plan.md` (21 scenarios over four tiers), `run-1.md` (RED, 1 major: the 26 px
collapse), `run-2.md` (GREEN). The premise probe above is in `run-1.md` § « Premise ».
