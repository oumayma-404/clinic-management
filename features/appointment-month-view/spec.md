# Feature Specification: Appointment Month View

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-16
**Scope:** FE
**Feature:** Add a third "Month" view to the appointments page (alongside Day and Week), rendering a Google-Calendar-style month grid of day cells with compact appointment chips.

## Overview
The `/appointments` page currently offers Day and Week views, both backed by the time-grid `AppointmentCalendar`. This adds a **Month** view: a 7-column week grid (Mon–Sun) of day cells for the selected month, each cell listing its appointments as compact chips. It is a navigation-and-overview surface — no time-of-day layout — reusing the existing appointment fetch, status colors, and status filters. All client-side; no API/backend changes.

## What Changes
- A third **"Month View"** tab is added to the appointments page, after Day and Week.
- `AppointmentCalendar` gains a `view: "month"` render path: a 6-row × 7-column grid (weeks start Monday) covering the selected month, with leading/trailing days from adjacent months shown dimmed, and today's cell highlighted.
- Each day cell lists that day's appointments as compact chips (start time + patient name), colored by the existing status/procedure logic; when a cell has more appointments than fit, it shows a "+N more" affordance.
- Navigation (prev/next arrows + Today) moves by **month** in month view; the header title reads the month + year (e.g. "juillet 2026" / "July 2026" per existing `date-fns` formatting).
- Appointments are fetched for the full visible grid range (start of the first week to end of the last week), reusing `useAppointments`.

## Acceptance Criteria
- **AC-1:** Selecting the Month tab renders a 6×7 day-cell grid for the selected month; days from the previous/next month that fill the first/last weeks are visually dimmed, and the cell matching today is visually highlighted.
- **AC-2:** Each day cell shows its appointments as chips (start time + patient name) using the same color rules as day/week views (`getStatusColor`, incl. "Occupé" busy slots); a cell with more appointments than the visible cap shows a "+N more" indicator.
- **AC-3:** In month view the prev/next controls move one calendar month, "Today" returns to the current month, and the header shows the selected month and year. Data is fetched for the full grid range (first visible day → last visible day).
- **AC-4:** Clicking an appointment chip opens the existing edit dialog for that appointment. Clicking a day cell's empty area or its "+N more" indicator switches the page to **Day view** focused on that date.
- **AC-5:** The existing status filters (show cancelled / show completed) apply in month view exactly as in day/week: cancelled and completed appointments appear as chips only when their toggle is on.

## Out of Scope
- Per-chip Google "Push to Google" / "non synchronisé" controls (kept to day/week; too dense for month cells).
- Creating an appointment directly from a month cell (empty-cell click navigates to Day view instead), drag-to-move/resize, and any time-of-day positioning within a cell.
- The current-time line and scroll-centering (day/week only — meaningless in a month grid).
- Any backend / API / DTO changes.

## Edge Cases (Critical only)
- **Variable week count:** months span 4–6 week rows depending on start weekday; the grid renders a consistent layout (fixed 6 rows) so height doesn't jump between months.
- **Busy slots ("Occupé"):** render as chips like any appointment (consistent with day/week).
- **Empty month/cells:** a day with no appointments renders an empty cell (still clickable → Day view); the view never shows a blank/error state while loading.
