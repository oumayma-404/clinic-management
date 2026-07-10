# Retrospective: windows-desktop-app — Phase 5 (Packaging, Installers & Manual Backup)

**Feature:** windows-desktop-app (Phase 5 — final phase of the 5-phase umbrella)
**Date:** 2026-07-09
**Duration:** ~1 day (two sessions: S1–S4 in-repo, then S5–S7 packaging)
**Story review score:** 100/100 (`reviews/story-1.md`)
**Feature review:** COMPLETE · Challenged: Yes — 18 findings (0 Critical / 6 Major / 9 Minor / 3 Suggestion), **all 18 applied** post-challenge (`206c31a`)

## Summary

Repackaged the clinic app as a self-contained offline-LAN Windows product: one-click admin **backup** (pg_dump + file-storage copy), the API as an **auto-start Windows service** with clear startup-failure diagnostics, **self-generated HTTPS** trust material (CA + server cert), a same-origin **Kestrel/YARP front door**, a **WebView2 desktop shell**, and **Inno Setup server + client installers** (bundled PostgreSQL 16, Node runtime, NSSM, CA-trust import). All behavior is additive and gated to Local mode; the Cloud path is byte-for-byte unchanged.

## What Went Well

- **Discipline of the mode gate held to the last phase** — every Phase 5 behavior keys off `isLocalAuthMode`; Cloud HTTPS bind/redirect stayed byte-for-byte, and the Phase 4 `ControllerAuthorizationCoverageTests` allow-list needed no change (the new `BackupController` is `[Authorize(AdminOnly)]`).
- **In-repo C# was solid** — the review's theme was that the backup service, cert provisioner, and front door are clean (argument-list `Process` shell-out, `PGPASSWORD` via env, RSA-2048/SHA-256 CA, ephemeral CA key, admin guard + defense-in-depth). All 6 Majors were in the operator-verified installers, not the app code.
- **R-1 boundary was set honestly** — S5–S7 landed as committed, reviewable build recipes + a per-AC operator checklist in `packaging/README.md`, rather than pretending an un-runnable installer was verified.
- **Backup fails loud on every foreseeable error** — missing pg_dump / unwritable dest / disk full / timeout each map to a distinct operator-facing message (AC-8.2/8.3).

## What Could Be Improved

- **The installers were never executed** — Inno Setup / live PostgreSQL / WebView2 can't run in this environment, so 10 of 18 review findings (incl. all 6 Majors) were fixed by static inspection only and still need an operator run-through of `packaging/README.md`. The riskiest applied-but-unvalidated changes: scram-sha-256 DB auth + pgpass bootstrap (#10), the `BCryptGenRandom` DLL import (#12), service teardown-on-upgrade (#15), WebView2 detection (#16).
- **Installer logic carried several fail-open bugs** — swallowed Postgres-setup exit codes (#5), a service dependency on a conditionally-created service (#6), a plain-HTTP bind on all interfaces guarded only by a firewall rule (#2), and a CA-name mismatch that left the root CA trusted after uninstall (#3). These are the classic "scripts don't get the same review rigor as code" gaps.
- **Two known security gaps remain open** (carried from earlier phases, out of Phase 5 scope): the anonymous Google OAuth `state` is still unvalidated, and the committed `appsettings.json` still holds real-looking secrets (scrubbed only at publish).

## Learnings

12 learnings captured/merged in `features/LEARNINGS.md` (all tagged Phase 5):

1. **Pattern:** A service constructed before `builder.Build()` has no DI — give it a real logger, and don't leave a dead registration.
2. **Pitfall:** A reverse-proxy/loopback hop makes request-scheme-derived security decisions on the internal leg (cookie `Secure` trap).
3. **Pitfall:** A multi-step operation must delete its partial output on failure.
4. **Pitfall:** Make a loopback-only guarantee a property of the bind, not a firewall rule.
5. **Pitfall:** An orchestration script must check every external step's exit code and abort loudly.
6. **Pitfall:** Don't declare a hard dependency on a resource created later/conditionally in the same routine.
7. **Pitfall:** Guard browser globals (`window`) in any module importable server-side.
8. **Pitfall:** Enforce DB password auth at init — never rely on `-A trust` / network isolation on a shared host.
9. **Convention:** A store-deletion keyed on a literal must match the generator's exact value — pin both to one source of truth.
10. **Convention (merge):** CSPRNG requirement extends to installer scripts — Inno `Random` is non-crypto/unseeded; use `BCryptGenRandom`.
11. **Tools:** `Microsoft.Extensions.Hosting.WindowsServices` 8.0.1 pins `System.Diagnostics.EventLog` 8.0.1 (NU1605).
12. **Tools:** PowerShell 5.1 `Set-Content -Encoding UTF8` writes a BOM — use `UTF8Encoding($false)`.

## Follow-ups (not blocking Phase 5 completion)

- **Operator verification pass** of the installers on real Windows hardware (the `packaging/README.md` checklist) — the only thing between this repo and a running desktop app.
- **Supply the third-party runtimes** on the build box (PostgreSQL 16, Node, NSSM, offline WebView2 installer, Inno Setup 6).
- **Close the two residual security gaps** (OAuth `state` validation; rotate/remove the committed appsettings secrets).
- **Code signing** for the installers/exe (SmartScreen / Smart App Control) — real-world deployment need, out of the current spec's scope.
