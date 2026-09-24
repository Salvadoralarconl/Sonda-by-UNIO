# Phase 6 visual review — in progress

The approved authority remains **Canva → Sonda Home Page Design**, Page 1 Home, Page 2 Monitoring, Page 3 Search. The official unchanged exports are under `docs/reference/canva-phase6-export/`, all 1366×736. [Interactive comparison and overlays](../artifacts/phase6/visual-comparison.html). Screenshots use synthetic data; data differences are not substituted business rules.

## Approved font substitution

Inter is embedded locally with its OFL license, following explicit user approval. Text size, line height, spacing and position are tuned inside the existing measured geometry. Card dimensions, page proportions, rail, right panel, gutters and component sizes are not changed to accommodate glyph widths. Inter is a substitute for font software only, not permission to reinterpret Canva.

## Approved foreground-only contrast amendment

The user approved darkening affected small Warning/Error/muted foreground text to reach at least 4.5:1, preserving fills, dimensions, spacing, font size/weight, radii and layout. The table records exact CSS values, not an unrecorded palette redesign. Adjustments retain the original hue direction and use the lightest rounded RGB scaling that reaches the target against the stated background. Gray remains neutral gray.

| Text | Original foreground | Adjusted foreground | Unchanged background | Calculated contrast after |
|---|---|---|---|---|
| Warning badge | `#E3BA26` | `#897017` | `#FFF9DF` | 4.522:1 |
| Error / Active / Failure badge text sharing the error class | `#FB4F59` | `#B0373E` | `#FFD4D7` | 4.521:1 |
| Failed Run result text | `#D6535A` | `#A74046` | `#FFD4D7` | 4.517:1 |
| Home table muted heading | `#8C8C8C` | `#727272` | `#F8F8F8` | 4.530:1 |
| Search muted headings | `#888888` | `#727272` | `#F8F8F8` | 4.530:1 |
| Incident caption | `#8C8C8C` | `#767676` | `#FFFFFF` | 4.542:1 |
| Page title / Search footer muted text | `#888888` | `#767676` | `#FFFFFF` | 4.542:1 |
| Secondary labels / empty-state / evidence metadata | `#777777` | `#767676` | `#FFFFFF` | 4.542:1 |

Monitoring row backgrounds, status dots, icons, and green/blue foregrounds are not modified by this amendment. Automated checks cover specific fixtures, not every possible state; broader accessibility review remains required. The earlier contrast proposal HTML is retained as the pre-approval comparison, not the final exact-color specification.

## Evidence and remaining review

`artifacts/phase6/accessibility-findings.json` records actual axe findings. A successful audit-capture test means the report was generated, not that every accessibility criterion passed. The logo/chart ARIA roles were corrected without visual change. Native dialog focus restoration and fullscreen exit have browser checks; complete keyboard, zoom, responsive, contrast-state and real-backend workflow acceptance is still pending.

The original user Home PNG hash is unchanged. No Canva source edit, layout redesign, engine/persistence migration or later-phase work is part of these changes.

Latest unreviewed comparison: artifacts/phase6/screens-review/comparison.html, with separate Home/Monitoring/Search 50-percent overlay PNGs and reference/capture hashes. See docs/phase-6-screen-integration-review.md. No approved visual baseline has been frozen. Core workspace geometry was aligned to recorded Canva coordinates; the foreground color values above are unchanged.
