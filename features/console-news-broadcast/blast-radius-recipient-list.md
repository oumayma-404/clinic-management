# Blast radius — the console lists who a send reaches (2026-11-09)

Owner's ask: « in console, can i have always a list for who the mail is gonna reach? just for me to do a sanity check ».

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PlatformNewsAudiencePlan` (+ `SkippedClinics`, count derived) | the one audience rule | `SendPlatformNewsCommand` (reads `Recipients`, `Clinics` only), audience query, `PlatformNewsTests` | unaffected — `From()` is the only constructor; the count keeps its name and value |
| 2 | `PlatformNewsAudienceDto` (+ `RecipientList`, + `SkippedClinics`) | wire DTO | `PlatformNewsController.Audience`, console `PlatformNewsAudience` type, `/nouveautes` page, composer, BFF route (pass-through) | must change — console type + composer in this commit |
| 3 | `PlatformReadShape` (+ `RecipientList`, `SkippedClinics`) | the console's closed field set | `PlatformReadShapeTests` (both directions) | must change — the list rows reuse `ClinicName` / `AdminName` / `AdminEmail`, already declared for the portfolio |
| 4 | `news-composer.tsx` | the console « Nouveautés » screen | `NewsWorkspace` only | must re-test — 320 / 390 / 1536×730, long address, 0 recipients, confirm still re-reads |

No journal row: the portfolio list already shows each cabinet's admin address unjournaled; AC-7.3 journals a single
cabinet's **detail** read only.
