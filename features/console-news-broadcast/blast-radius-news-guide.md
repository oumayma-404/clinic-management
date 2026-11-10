# Blast radius — guide par nouveauté + contact WhatsApp (2026-11-10)

| # | Touching | What it is | Other consumers | Verdict |
|---|----------|------------|-----------------|---------|
| 1 | `PlatformNewsItem` (+`Description`, +`Steps`) | owned VO, persisted | `PlatformNewsBroadcast.Create`, EF config, `OrderedItems()` readers (dispatcher via `PlatformNewsDraft.From`, `ListVisibleInAppAsync`) | must change — config + migration, both readers carry the fields |
| 2 | `PlatformNewsItemInput` (+2 optional) | positional record | `Normalize`, `ForPreview`, **`PlatformNewsDraft.From` (a copy)**, `Create`, tests | must change — `From` re-lists positional args (§ 2.1), so it must pass the guide or a resumed send drops it |
| 3 | `PlatformNewsItemRequest` (+2 optional) | wire request | `PlatformNewsDraftRequest`, `SendPlatformNewsRequest`, 3 handlers, controller tests | unaffected — omitted = no guide |
| 4 | `EmailBullet` (+3 optional) | shared e-mail model | `PlatformNewsEmail.Compose`, `EmailLayoutTests` | unaffected — additive; a guide-less bullet pinned byte-identical (hash) |
| 5 | `EmailContent.Contact` (new, optional) | shared e-mail model | every transactional sender | unaffected — null renders nothing; pinned by hash |
| 6 | `EmailLayout.Html` / `PlainText` | the one renderer | every transactional e-mail | must re-test — SHA-256 of HEAD's output for 3 fixtures, asserted after |
| 7 | `PlatformNewsEmail.Compose` (+contact) | the one news composer | preview query, test command, dispatcher, tests | must change — all three pass `PlatformNewsContact.From(ISubscriptionPricing)` |
| 8 | `ISubscriptionPricing` | config seam | `GetSubscriptionQuery`, `AuthController` | unaffected — read only, injected into 3 more handlers |
| 9 | `PlatformNewsAppItem` / `NewsItemDto` (+2) | in-app read | repo projection, `NewsReading.ToEntry`, `web/lib/api/news.ts`, `InAppNewsTests` | must change — projection, mapping, TS type |
| 10 | `PlatformReadShape` | closed set of console response leaves | every console response | unaffected — no console response carries items (the preview is `NewsHtml`) |
| 11 | `useHelpContact` (+`whatsappNumber`) | shared hook | « Aide » menu, Paramètres « Besoin d'aide ? » | unaffected — additive member |
| 12 | console `news-composer.tsx` + `check-responsive` N10 | form + derived guard | — | must change — N10 gains the 3 new limits |
| 13 | migration + model snapshot | schema | — | must change — scaffolded, checked for `xmin` |
| 14 | console `news-history.tsx` | past sends | — | unaffected — reads no rows' content |
