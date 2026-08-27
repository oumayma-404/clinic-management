# Feature Specification: Real-Time Updates (Appointments slice)

**Status:** APPROVED
**Type:** Small
**Created:** 2026-07-10
**Scope:** Full
**Feature:** Live-push data changes to all clients of the same clinic via SignalR, so an open view updates without a manual refresh. Slice 1 established the infrastructure on appointments; slice 2 generalized it so **any edit** (any mutating command) broadcasts automatically.

## Overview
Today the app only loads data on page render / manual refresh, so a change made on one PC isn't visible on another until that page is refreshed. This adds a SignalR real-time layer: the API broadcasts data changes to every connected client of the same clinic, and each view refetches automatically when a change to its resource arrives. Slice 1 established the **reusable** hub/group infrastructure on appointments; **slice 2** moved broadcasting into a cross-cutting MediatR pipeline behavior so it fires after **any** successful mutating command — no per-handler wiring — and wired the frontend views (patients, procedure types, files, records, users, clinic settings, patient detail) to subscribe.

## What Changes
- The API hosts a SignalR hub; an authenticated client joins a group scoped to **its own clinic**.
- **`RealtimeBroadcastBehavior` (MediatR pipeline)** broadcasts an `entityChanged("<area>")` event to the caller's clinic group after **any** successful mutating command (`Features/<Area>/Commands`), derived structurally from the command's feature area — excluding non-data areas (auth / AI / backup / connectivity). Cancellation is an update (`PUT` status `Cancelled`), so it is covered like any other update. New commands are covered automatically.
- Each view subscribes via `useClinicRealtime(resource | resource[], onChanged)` and refetches when its resource changes — no manual refresh — reconnecting transparently after a dropped connection (catch-up refetch on reconnect).
- The hub runs in **both auth modes**, using the same mode-branched auth as the REST API (Local HS256 / Cloud Auth0). This is the one real-time behavior added to Cloud; everything else is additive.
- Real-time is **additive**: if the hub is unreachable the page still works with manual refresh. A broadcast (or its clinic lookup) never affects the committed command's result.

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
- Pushing the changed entity's data (clients refetch on the signal rather than receiving diffs/patches).
- Live refresh for the surfaces still on hardcoded sample data (dashboard stats, appointment-list, notifications-list, stock) — they have no API fetch to refetch yet. `documents` broadcasts but no view lists saved documents today.
- Presence / "who's online", typing indicators, optimistic UI.

## Edge Cases (Critical only)
- Cancellation (an update to `Cancelled`) must broadcast, so removals reflect live — covered by wiring the update path, not only create.
- The broadcast fires only **after** the change is committed — never on a failed/rolled-back save.
- The broadcast is sent to the whole clinic group including the originating client; a harmless self-refetch is acceptable (no need to exclude the caller).
