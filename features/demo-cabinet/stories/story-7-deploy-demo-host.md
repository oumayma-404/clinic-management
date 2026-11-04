# Story 7: Deploy - demo.apexa.tn prepared beside production

**Status:** APPROVED
**Story Status:** reviewed (2026-11-04) — every server / DNS step waits for the owner
**Layer:** Deploy (compose, Caddy, CI, runbook)
**Depends On:** 3, 5, 6
**Blocks:** 8

## Objective

Everything needed to run the demo next to production on the same VPS is written and rehearsed locally: a compose
overlay with its own database, role, keys, bucket and resource limits; a Caddy block with noindex and no install; the
deploy workflow bringing the demo up on the same image tag; and a runbook. ⚠️ **Nothing touches the VPS, DNS or
GitHub Actions secrets without the owner's explicit OK for each step.**

## Acceptance Criteria

_From spec:_

- [x] AC-7.1 nothing leaves (no SMTP / Meta / Google / Push / Reminders env on the demo)
- [x] AC-7.2 demo token refused by production; AC-7.3 demo never reaches production's DB or bucket
- [x] AC-7.4 every production deploy updates the demo to the same version
- [x] AC-7.6 failed rebuild visible (deploy summary warns on a stale `demoBuiltOn`)
- [x] AC-7.7 noindex; EC-3 demo cannot starve production (mem/cpu limits); FR-4 install not offered

_Story-specific:_

- [x] `--remove-orphans` can never delete the demo (overlay in all three `$COMPOSE` places)
- [x] Workflow unchanged when `DEMO_DOMAIN` is absent from the server `.env`

## Entry Criteria

- [ ] Stories 3, 5, 6 implemented
- [ ] Docker available locally; throwaway compose project name chosen (not `clinic-management`)

## Steps

1. **Overlay** — `deploy/docker-compose.demo.yml`: `api-demo` / `web-demo` (`clinic-api-demo`, `clinic-web-demo`),
   same images; env per plan.md US-7 step 1 (profile, connection string, signing key file, issuer/audience, key ring
   volume + cert, bucket + keys, `FrontendUrl`, `Demo__SignupUrl`); `mem_limit` + `cpus`; `web-demo`
   `API_INTERNAL_URL=http://api-demo:5000/api`.
2. **Caddy** — `{$DEMO_DOMAIN}` block copied from the app block → demo upstreams, `X-Robots-Tag: noindex`,
   `/manifest.webmanifest` → 404; `DEMO_DOMAIN` in caddy env (`docker-compose.prod.yml`).
3. **Workflow** — `deploy-hosted.yml`: when `.env` has `DEMO_DOMAIN`, add the overlay to `$COMPOSE` (3 places), pull
   `api-demo web-demo`, `/health` + `verify-schema` on `clinic-api-demo`, summary warning on a stale `demoBuiltOn`.
4. **Runbook** — role + DB, MinIO user + bucket policy, secrets owned by uid 1654, DNS A record, first boot,
   `Demo__RebuildOnStart`, reading `demoBuiltOn`, Caddy `--force-recreate` (single-file bind mount).
5. **Rehearse locally** under the throwaway project: two host names, replay a demo token on the « prod » API → 401,
   demo role cannot open the « prod » DB, noindex header, manifest 404. Then `docker compose -p <throwaway> down -v`,
   remove its images, `docker builder prune -a -f`.
6. **Hand to the owner** — diff + runbook; wait for her OK on each VPS / DNS step.

## Files to Create/Modify

### Files to Create

| File | Purpose |
|------|---------|
| `deploy/docker-compose.demo.yml` | overlay |
| `deploy/demo/README.md` (or § in `deploy/README.md`) | runbook |

### Files to Modify

| File | Changes |
|------|---------|
| `deploy/Caddyfile` | demo block |
| `deploy/docker-compose.prod.yml` | `DEMO_DOMAIN` into caddy |
| `.github/workflows/deploy-hosted.yml` | demo services, checks |

## Verification Steps

- [ ] `caddy validate` on the new Caddyfile (local container)
- [ ] Local rehearsal checks of step 5 all pass
- [ ] Workflow dry read: with no `DEMO_DOMAIN`, every command identical to today
- [ ] Docker cleanup done and reported

## Exit Criteria

- [ ] All ACs checked locally; committed; owner has the diff + runbook; README status → `implemented`
