# Feature Specification: Partial Payments, Patient-Page Reorder & Real AI Summary

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-17
**Scope:** Full
**Feature:** Track partial payments (avance / reste à payer) on dental records, surface medical records right under the summary on the patient page, and make the "AI summary" actually AI-generated.

## Overview
Three cohesive changes to the patient detail experience. (1) Patients often pay an advance rather than the full procedure cost (multi-session work). The data model already stores `Cost` + `AmountPaid`; we surface the derived **reste à payer** everywhere it matters and stop the modal from silently forcing a full payment. (2) The medical-records tabs currently sit at the bottom of the patient page — move that whole tabbed section directly under the summary so it's the first thing a doctor sees. (3) The "AI-Generated Patient Summary" card is a hardcoded string template today; wire it to the existing HuggingFace AI service to produce a brief, bulleted, highlight-important French overview of the patient and their procedures.

## What Changes
- Dental record responses expose a derived **balance** (`Cost − AmountPaid`); it is shown in the dental-records table (patient page + summary modal) alongside Amount Paid.
- The record entry modal shows a live **"Reste à payer"** readout as the doctor edits cost/amount paid, and no longer auto-forces `amountPaid` to equal `cost` (full payment stays the convenient default but a partial advance is easy to enter).
- Records with a positive balance are visually flagged (e.g. amber "Reste: X") so unpaid balances are obvious at a glance.
- On the patient detail page, the medical-records tab section moves to directly **under the AI summary card**, above the Personal/Medical/Administrative info grid.
- The AI summary card is populated by a **real AI call** (existing `IHuggingFaceAIService`) via a new patient-scoped endpoint. It generates **automatically on page load**, renders as concise French bullet points highlighting important items (allergies, flags, balances, key procedures), shows a loading state while generating, offers a **"Régénérer"** button, and degrades gracefully when the AI is unavailable/offline.

## Acceptance Criteria
- **AC-1:** `DentalRecordDto` includes `balance = Cost − AmountPaid`, populated in the create, update, and get-list mappings; the frontend `DentalRecordDto.balance` is now backed by the API.
- **AC-2:** The dental-records table (patient page **and** patient-summary modal) shows the remaining balance per record; a positive balance is visually emphasized (amber), a zero/negative balance is not.
- **AC-3:** In the record modal, editing cost or amount paid updates a live "Reste à payer" figure; saving a record where `amountPaid < cost` persists that partial amount (no auto-overwrite to full cost on save).
- **AC-4:** On the patient detail page, the tabbed medical-records section renders directly beneath the AI summary card and above the three info cards.
- **AC-5:** Opening a patient page triggers a real AI summary request; a loading indicator shows while it runs, then the AI-produced bulleted French summary renders in the summary card.
- **AC-6:** A "Régénérer" control re-requests the summary on demand.
- **AC-7:** When the AI call fails or (Local mode) the internet is unreachable, the card shows a clear French fallback message instead of a spinner or a crash — the page and all other data still load normally.
- **AC-8:** The summary endpoint is clinic-scoped: requesting a patient outside the caller's clinic returns not-found, never another clinic's data.

## API Contract
### GET /api/patients/{patientId}/ai-summary
Request: none (patientId in route)
Response 2XX: `{ summary: string }`  — AI-generated French text (bullet lines)
Errors: `404 Patient not found` (missing / other clinic); `400 { error }` when the AI backend is unavailable or returns nothing (frontend maps to the French fallback).

## Data / Schema Changes
- `DentalRecordDto.Balance` (decimal) — **derived, read-only** (`Cost − AmountPaid`). No entity/DB column added; computed in handler mappings.
- No new persisted columns (AI summary is generated live, not stored — per chosen "auto on load, no persist").

## Out of Scope
- A payment ledger / multiple discrete payment transactions per record (single cumulative `AmountPaid` per record stays the model).
- Persisting/caching the AI summary in the database.
- Currency changes (records keep the app's existing `$` formatting).
- Touching the dormant `IPatientSummaryService` placeholder / `AISummaryJob` (the new endpoint calls `IHuggingFaceAIService` directly).

## Edge Cases (Critical only)
- `amountPaid > cost` (overpayment/data entry): balance goes negative — display as `0` reste à payer (no amber flag), never a negative "reste".
- Patient with no dental records / no history: AI summary still generates a brief "peu d'informations disponibles"-style note rather than erroring.
- Local mode offline: summary card shows the French "connexion requise" fallback (reuse `useConnectivity()`); the rest of the page is unaffected.
