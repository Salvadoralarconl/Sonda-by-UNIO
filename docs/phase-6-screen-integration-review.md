# Three-screen integration review — awaiting visual acceptance

This increment addresses the user's priority of fully connecting Home, Monitoring and Search before broader Phase 6 sign-off. It does not approve or freeze any visual baseline. Canva Page 1 Home, Page 2 Monitoring and Page 3 Search remain authoritative. Approved Inter and the exact foreground-only contrast changes in `phase-6-visual-review.md` are preserved.

## Connected interactions

| Interaction | Implementation and evidence |
|---|---|
| Home Info | Real Incident detail/presentation, occurrence/run, chronological evidence and history endpoints; centered native dialog with nested run navigation. Escape restores Home focus without routing away. |
| Incident workflow | Uses server `allowedActions`, expected revision and a retained operation identity. The real test drops an acknowledgment after server commit, closes/reopens the modal, reconciles the same ID and verifies the history. Uncertain intents survive modal navigation in memory and are cleared on session expiration; no browser persistent log/token store was added. |
| Monitoring row | Selected application only: business health, availability, complete unresolved counts, dedicated bounded Investigating list, recent incidents/resolved history, Application Runs and failed Order Runs. Nested Incident and Run dialogs preserve the Monitoring page. |
| Fullscreen | Monitoring-only expanded layout, explicit exit and browser-fullscreen support. A real browser test forces Fullscreen API rejection and verifies the layout fallback. Idle polling remains consistent with accepted session behavior; paused updates are now explicitly labeled instead of appearing live. |
| Search groups | Persisted Order Run relationships determine blocks and results. Result buttons open actual run details. No raw line is promoted to a run or incident. |
| Unassigned evidence | Application-only/outside-cycle/unmatched evidence remains searchable with a neutral label and no invented Order Run result. Detail retains distinct timestamps and recorded interpretation. |
| Shared evidence | A cycle-end entry linked to two Order Runs and the Application Run is displayed once, with a reference in the other group and all three links in evidence detail. |
| Greeting | Authenticated display name and reporting/server timezone; tested with the browser deliberately set to Asia/Tokyo. |
| Badges | Compared directly against real Dashboard responses; not derived from percentage or recent-row counts in React. |

Real fixture: two synthetic applications, eight completed Order Runs across three Application Runs, successful/failed/undefined outcomes, outside-cycle evidence and shared completion evidence. The UI workflow changes one Incident to Investigating while the eleven durable metric facts remain unchanged. No production source was read. No API response is replaced in this integration test; only one network acknowledgment and Fullscreen permission failure are injected.

Source: `tests/Sonda.Api.Tests/ScreenIntegrationTests.cs`, `tools/verify-screen-integration.cjs`. Evidence: `artifacts/phase6/screen-integration-results.json` and the current TRX directory recorded in the progress report. The earlier synthetic setup failures (Profile identity mismatch and missing revision increment) are retained; accepted assertions and database constraints were not weakened.

## Updated visual evidence

[Three-page comparison](../artifacts/phase6/screens-review/comparison.html) includes side-by-side views and an adjustable overlay. Individual browser-rendered 50% overlays:

- [Home](../artifacts/phase6/screens-review/home-overlay.png)
- [Monitoring](../artifacts/phase6/screens-review/monitoring-overlay.png)
- [Search](../artifacts/phase6/screens-review/search-overlay.png)

`capture-metadata.json` records the fixed 1366×736 viewport, server instant, actual layout rectangles and Inter font. `comparison-provenance.json` records hashes of both the unchanged official references and implementation captures. The browser fixture waits for fonts/images and disables screenshot animations. These presentation fixtures intentionally remain separate from real PostgreSQL behavior evidence.

Corrections from the overlays: Monitoring's workspace/right gutter now matches the recorded native Canva geometry; Home's lower edge no longer grows from excess table margin; Search text is positioned within the unchanged grouped block dimensions. Foregrounds, fills, font sizes/weights and radii were not changed in this increment. All captures are labeled **UNREVIEWED — NOT A FROZEN BASELINE**; there are no new approved Playwright image snapshots.

Content differences are explicit: current-state badges can differ from Canva's illustrative values even at 97% daily health; the fixture includes two unresolved incidents; the mini-chart shows exactly five daily buckets; bounded Search adds paging controls. These preserve accepted behavior rather than copying illustrative data into the frontend.

## Remaining acceptance work

Phase 6 is incomplete. The user must review these comparisons before any baseline is frozen. Fine icon/raster alignment, shadow fidelity, all visual states, responsive/zoom behavior and the complete keyboard/accessibility matrix remain subject to review. The unchanged green foregrounds are outside the approved Warning/Error/muted adjustment; the limited zero-violation audit fixtures do not establish all-state contrast compliance.

Broader Configuration/account/diagnostics round trips, revocation/conflict/error scenarios, large-report limits and generic command recovery still need the complete Phase 6 matrix. Incident pending intents are retained only in memory across modal navigation; full document reload is not a persistent command-payload store. The operation ID is exposed for recovery, and reload while that modal is open warns about the uncertain request.

All 382 accepted backend tests remain mandatory gates. No accepted engine behavior, database migration, acquisition checkpoint model, Canva file, production deployment or later-phase implementation is changed. The five Phase 4 environmental verification gates remain pending.
