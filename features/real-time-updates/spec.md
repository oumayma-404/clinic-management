# Feature Specification: Real-Time Updates (Appointments slice)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-10
**Scope:** Full
**Feature:** Live-push appointment changes to all clients of the same clinic via SignalR, so an open calendar updates without a manual refresh.

## Overview
Today the app only loads data on page render / manual refresh, so an appointment created on one PC isn't visible on another until that page is refreshed. This adds a SignalR real-time layer: the API broadcasts appointment changes to every connected client of the same clinic, and the appointments calendar refetches automatically when a change arrives. This first slice establishes the **reusable** real-time infrastructure and wires up **only** the appointments view; other views subscribe in later slices.

## What Changes
- The API hosts a SignalR hub; an authenticated client joins a group scoped to **its own clinic**.
- Creating or updating an appointment broadcasts an "appointments changed" event to that clinic's group only. Cancellation is an update (`PUT` with status `Cancelled`), so it is covered by the update broadcast — there is no separate server-side delete endpoint.
- The appointments calendar auto-refetches (via its existing `refetch()`) when it receives that event — no manual refresh — and reconnects transparently after a dropped connection.
- The hub runs in **both auth modes**, using the same mode-branched auth as the REST API (Local HS256 / Cloud Auth0). This is the one real-time behavior added to Cloud; everything else is additive.
- Real-time is **additive**: if the hub is unreachable the page still works with manual refresh.

## Acceptance Criteria
- **AC-1:** With the appointments page open on two clients of the same clinic, creating / editing / cancelling an appointment on one makes it appear/update on the other within ~2s, with no manual refresh.
- **AC-2:** A client receives events for **its own clinic only** — a client of a different clinic sees nothing (multi-tenant isolation).
- **AC-3:** The hub rejects connections without a valid session (same auth as the REST API, in both modes).
- **AC-4:** If the connection drops (server restart / network blip), the client reconnects automatically and resumes receiving events.
- **AC-5:** No regression when the hub is unreachable — the calendar still loads and works via manual refresh.

## API Contract
### WS `/hub/clinic` (SignalR hub)
- **Auth:** bearer JWT, passed as the `access_token` query param on the WebSocket handshake (browser WS can't set headers); validated by the same mode-branched scheme as the REST API (Local HS256 / Cloud Auth0) via the JWT `OnMessageReceived` event for hub paths.
- **Server → client:** `appointmentsChanged` (no payload — it signals the client to refetch).
- On connect the server resolves the caller's clinic id (from the authenticated user, same DB lookup the REST handlers use) and adds the connection to group `clinic-{clinicId}`.

## Out of Scope
- Real-time for other entities (patients, documents, dental records, stock, users) — future slices reuse this hub/group infrastructure.
- Pushing the changed entity's data (clients refetch on the signal rather than receiving diffs/patches).
- Presence / "who's online", typing indicators, optimistic UI.

## Edge Cases (Critical only)
- Cancellation (an update to `Cancelled`) must broadcast, so removals reflect live — covered by wiring the update path, not only create.
- The broadcast fires only **after** the change is committed — never on a failed/rolled-back save.
- The broadcast is sent to the whole clinic group including the originating client; a harmless self-refetch is acceptable (no need to exclude the caller).
