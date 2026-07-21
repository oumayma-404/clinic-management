# Feature Specification: Misc Data-Integrity Cleanups

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-21
**Scope:** BE
**Feature:** Stop draft-invoice edits from wiping their links, and remove a never-dispatched event handler that would create unsendable reminders if ever wired.

## Overview
Two low-severity but real integrity issues. (1) Editing a draft invoice sends only `{ patientId, lines }`, and `UpdateInvoiceCommand` unconditionally re-applies links with null values, so a draft seeded from a dental record loses its header dental-record link, appointment link, and per-line record links on the first edit. (2) `AppointmentCreatedEventHandler` is dead — domain events are never dispatched anywhere — and if event dispatch were ever wired it would create `NotificationType.Both` outbox rows that `NotificationJob` has no sender for (stuck Pending forever).

## What Changes
- `UpdateInvoiceCommand` preserves the invoice's existing header dental-record and appointment links (and per-line record links) when the edit request does not include them, instead of nulling them.
- The never-dispatched `AppointmentCreatedEventHandler` is removed (dead code) so it can never create unsendable outbox rows if event dispatch is later added; no runtime behavior changes today.

## Acceptance Criteria
- **AC-1:** Editing a draft invoice's lines/patient preserves its existing header dental-record and appointment links and per-line record links.
- **AC-2:** `AppointmentCreatedEventHandler` no longer exists in the codebase; the app builds and behaves identically at runtime (events were never dispatched).

## Out of Scope
- Adding a UI affordance to explicitly clear an invoice's dental-record/appointment link (a separate, explicit action).
- Wiring a domain-event dispatch mechanism (a larger architectural change).
