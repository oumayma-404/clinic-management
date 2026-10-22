# QA plan — one shared live connection per tab (Part 2a)

**Change under test:** `ClinicRealtimeProvider` (mounted in `app/layout.tsx`) owns the tab's one SignalR
connection; `useClinicRealtime` keeps its signature and only adds a listener. 35 call sites unchanged.

**Environment:** same isolation as `plan.md` — branch API `:5099`, worktree `next dev` `:3099`, own Chrome
(`playwright-core`), fresh contexts. The **baseline** row reads the peer's stack (`:3000` → `:5000`, old code)
**read-only**, in its own context, with its own sign-in.

**Evidence:** WebSockets are counted with CDP (`Network.webSocketCreated` / `webSocketClosed`) on URLs
containing `/hub/clinic`.

| ID | Tier | Layer | Scenario | Expected (observable) |
|---|---|---|---|---|
| RT-0 | baseline | browser | old code (`:3000`): open `/appointments`, then a patient page | the number of hub WebSockets each page opens — recorded, not asserted |
| RT-1 | A | browser | new code: the doctor (password-only, so no TOTP wait) opens `/appointments` | exactly **1** hub WebSocket open |
| RT-2 | A | browser | sidebar to « Patients », then « Liste d'attente » (client-side navigation) | **0** new hub WebSockets created; still 1 open |
| RT-3 | A | browser | heavy screen: the patient page `/patients/{id}` (several subscribers) | exactly **1** hub WebSocket open |
| RT-4 | D | browser + api | tab B (secretary, own context) on `/waiting-list`; an entry is created over the API with a unique « Créneau souhaité » marker, then deleted | B shows the marker within 10 s **without a reload**, and it disappears within 10 s after the delete |
| RT-5 | B | browser | sign out | the hub WebSocket closes; `/login` opens **no** new hub WebSocket over 10 s (it used to retry every 5 s with no session) |
| RT-6 | C | — | reconnect catch-up after a dropped connection | ⏭ not exercised: needs the API taken down mid-walk; the `onreconnected` handler moved verbatim into the provider |

**Widths:** none — nothing visible changed. **Mutations:** RT-4's own waiting-list entry, created and deleted by
the walk (and swept by SQL if the walk dies between the two).
