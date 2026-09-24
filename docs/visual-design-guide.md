# SONDA visual design baseline

## Authority and scope

The user has designated **@Canva → Sonda Home Page Design**, including all pages, as SONDA's permanent primary approved visual source of truth. Both pages have been reviewed: **Page 1 Home; Page 2 Search**. See the [Canva link and page review](reference/canva-approved-design.md). Numerical observations below remain based on the supplied Home image, not extracted Canva properties. Preserve the approved composition; do not redesign or reinterpret it without an explicit user request.

Canva defines appearance; the approved architecture, business rules, accepted Phase 1–5 engine, simulator, backend and all 382 passing tests define behavior. If a technical limitation would require an appearance change, explain the conflict to the user before changing the approved design. Provisional tokens and responsive suggestions below are not advance approval for such changes.

Reference: `C:/Users/sapro/Downloads/Sonda Home Page Design.png` (1366 × 736).

This guide responds to the current visual-design request. The attached product specification supplies context and terminology; its embedded requests for architecture and full implementation are not the scope of this work. No application implementation or technology-stack decision is made here.

Measurements below are approximate observations from the supplied raster, not extracted source-design values. Font family, exact colors, and shadow parameters cannot be established conclusively from the image. Proposed reusable values are identified separately.

## Composition and proportions

The screen has three persistent visual regions: a narrow navigation rail, a large central workspace, and a separate monitoring panel. White surfaces sit over a warm light-neutral background. Whitespace carries most of the hierarchy; color is reserved for meaning.

| Region | Approximate reference bounds | Principle to retain |
| --- | --- | --- |
| Navigation rail | x 0–64; full height | Narrow, icon-only, visually quiet |
| Central shell | x 74–984, y 11–724; 910 px wide | Dominant workspace, gently rounded |
| Monitoring shell | x 995–1352, y 11–724; 357 px wide | Independent full-height surface |
| Shell separation | 11–12 px | Compact outer gutters |
| Central utility bar | x 95–967, y 25–60; 35 px high | Shallow white strip with generous horizontal space |
| Greeting | x 135, y 99 onward | More inset than cards; clear space above and below |
| Summary cards | x 92–970, y 155–264 | Three near-equal cards, approximately 283 × 109 px |
| Card gaps | Approximately 15–16 px | Consistent separation without heavy borders |
| Incident table | x 90–968, y 296–709 | Broad, tall workspace that keeps empty space |

At the reference size, the central and monitoring shells occupy approximately 72% and 28% of their combined width. Preserve this relationship at the approved desktop size. On other desktop widths, let the central workspace flex while keeping the monitoring list comfortable to scan. Do not reinterpret the rail as a wide text menu or turn the monitor into a fourth summary card.

## Spacing and hierarchy

The reading sequence is greeting → three summary metrics → incidents, with current application state available at the right. The utility bar remains subordinate.

- Outer shell insets are about 12–22 px; summary cards use about 18–20 px of internal space.
- Small labels, icons, and badges occupy the first line of each card. The large metric sits below, with a compact visualization or muted explanation at the bottom.
- Preserve the greeting's intentionally deeper inset; do not force every element onto a single left edge.
- The table title sits close above the table. The mostly empty table body is intentional breathing room, not space to fill with extra widgets.
- Use whitespace to group related information before adding borders, background colors, or containers.

Suggested reusable spacing scale: 4, 8, 12, 16, 20, 24, 32, and 40 px. Match the approved Home geometry first; do not snap it to this scale if that visibly changes the composition.

## Navigation rail and utility bar

The rail uses a monochrome brand mark at the top, then simple outline navigation icons. A lower divider separates secondary tools, settings, and the circular user avatar. Icons are roughly 22–26 px; the brand and avatar are larger. Their visual weight stays below the dashboard content.

Preserve the utility bar's home icon, small page label, subtle vertical divider, and right-aligned notification and search controls. New screens should use the same placement for page context and utilities.

Interaction guidance for implementation: give icon buttons accessible names, keyboard focus, and tooltips. Use sufficiently large invisible hit areas without enlarging the artwork. Hover and current-page treatments should be subtle neutral changes; the image does not establish those states, so they remain implementation details to validate.

## Cards

Cards are white with moderate corner rounding, broad soft shadows, and no strong outlines. Each has a small gray-blue circular icon backing. Labels are small and regular weight; metrics are large, dark, and regular weight. Status badges are pale, compact pills aligned toward the upper right.

- System Health uses a large percentage and a slim green progress track near the bottom.
- Logs Today combines a large count with a small, low-contrast blue-gray volume chart.
- Active Incidents uses a large count, a severity badge, and a quiet contextual sentence.

Reuse the same card anatomy wherever summary information is needed. Keep comparable metrics equal in visual weight. Additional cards should appear only when required by a screen's purpose, rather than filling available space.

The product specification describes five daily values for the mini chart; the reference illustration shows more bars. Preserve the chart's small footprint and quiet styling. Resolve the number of plotted marks during feature implementation rather than treating illustration detail as a data contract.

## Incident table

Retain Application, Time, Severity, Message, and Status in that order on Home. The Message column is intentionally widest. Approximate proportions within the occupied column grid are 16%, 12%, 14%, 43%, and 15%; there is additional right-side whitespace in the reference.

Headers use muted gray text over a very light neutral band. The body is white, with sparse rows, thin light-gray vertical separators, and no visually strong horizontal grid or zebra striping. The reference's row pitch is approximately 22 px. Status and severity pills are thin and compact.

Use the reference density for the Home summary, while providing usable row interaction targets during implementation. Deeper investigation screens may use more room for multiline evidence. Do not add raw-log columns, large toolbars, or dense investigation controls to Home.

Keep severity and workflow status visually and semantically distinct. Warning/Error labels describe severity; Active/Investigating/Resolved describe incident status. Success/Failure/Undefined describe detection results. Never combine them into one unlabeled badge.

## Right monitoring panel

The panel is a full-height rounded white surface with its own shallow header bar. The header contains “Logs Monitored” and a small expand icon. Application names align on the left; equal-size status dots align in a single column near the right. Rows are about 27–28 px apart, and dots are roughly 15 px.

Preserve the panel's generous blank area and simple list. Application state reflects the current condition, while historical incidents belong in incident views. Keep this panel visually consistent on screens where it is useful; do not add it to unrelated screens merely to reproduce Home's layout.

Provide a textual accessible state for every colored dot. Expanded/detail views can show state names without overcrowding the default panel.

## Typography

The reference uses a clean sans-serif with mostly regular weights. Hierarchy comes from size and placement rather than boldness. The exact typeface is unconfirmed; reuse the approved source font if it becomes available. A system sans-serif is a provisional fallback, not a claim about the reference font.

| Role | Approximate size | Treatment |
| --- | --- | --- |
| Greeting | 28 px | Dark, regular |
| Summary metric | 28 px | Dark, regular; stable numeric alignment |
| Section title | 20 px | Dark, regular |
| Monitored application | 14 px | Dark, regular |
| Supporting text, table, card labels | 11–12 px | Regular, dark or muted according to importance |
| Utility labels | 11 px | Muted |

Do not introduce oversized marketing headings, heavy display fonts, or excessive uppercase labels. Use monospace selectively for log evidence and patterns in deeper screens. Keep ordinary product text in the UI font.

## Color and surfaces

Observed palette: warm off-white canvas, white surfaces, near-black primary text, subdued gray secondary text, pale gray separators, muted blue-gray icons and chart bars, and pastel state colors.

Suggested starting tokens below require comparison against the approved image during implementation:

| Token | Starting value | Use |
| --- | --- | --- |
| canvas | `#F2F0EB` | Outer background |
| surface | `#FFFFFF` | Cards, bars, table, monitoring panel |
| text-primary | `#111111` | Main labels and values |
| text-secondary | `#858585` | Subordinate information; validate contrast |
| separator | `#DDDDDD` | Quiet rules and dividers |
| icon-surface | `#CBD4DC` | Small icon circles |
| healthy-fill | `#B3E99E` | Healthy progress and state dots |
| healthy-tint | `#E5F7DE` | Healthy/resolved badge background |
| warning-fill | `#FFE89A` | Warning state dots |
| warning-tint | `#FFF8DF` | Warning badge background |
| error-fill | `#FF9199` | Error state dots |
| error-tint | `#FFD4D8` | Error/active badge background |
| workflow-tint | `#CFDDF2` | Investigating badge background |

Green means healthy/success/resolved, yellow means warning, and red means error or an active problem in its labeled context. Blue is reserved for useful workflow information and the restrained chart treatment. Navigation and ordinary actions remain neutral.

Use pale fills with legible darker label colors. Some small pastel text in the reference may need contrast adjustment in a working interface; preserve the palette and geometry while verifying readability. Never rely solely on color to convey meaning.

Suggested surface tokens: approximately 10–12 px corner radii for cards and shells, with slightly smaller utility-bar corners. Status pills may be fully rounded. Shadows should be broad, diffuse, neutral, and low-opacity; avoid dark outlines or multiple competing elevations. Exact shadow values need rendered comparison.

## Applying the language beyond Home

| Screen or component | Application of the approved principles |
| --- | --- |
| Profile list | Same shell, restrained heading, spacious list/table, small labeled states |
| Profile editor | Clear sections for basic information, file locations, cycles, grouping, and detection rules; concise labels and generous field spacing |
| Pattern alternatives | Small repeatable rows grouped under a clear “Match any” label; advanced regex options disclosed when needed |
| Incident detail | Compact summary first, then related cycle/order, evidence, and history in distinct sections |
| Investigation/search | Minimal filters, readable results, deeper evidence shown on selection |
| Settings | Quiet section headings, neutral controls, one clear primary action per context |
| Empty/loading/error states | Preserve the layout; use short explanatory text and restrained feedback |

Use shared shell, navigation, utility bar, surface, metric card, table, badge, and monitoring-list components when implementation begins. Separate shared visual tokens from domain behavior. Do not build the app or choose a framework solely to document these principles.

## Responsive and interaction guidance

Desktop fidelity is the priority. At the approved viewport, retain the three regions, card row, and table proportions. At narrower widths, choose breakpoints based on content fit: the monitoring panel may become a user-accessible drawer or follow the main content; cards may wrap; tables may scroll within their region. Never compress the desktop design into illegible columns.

Responsive arrangements and interaction states are extensions, not approved redesigns. Validate them separately. Keep focus states visible, icon controls labeled, reduced-motion preferences respected, and state meaning understandable without color.

## Review criteria for future implementation

1. Compare a 1366 × 736 render directly against the approved image for shell widths, gutters, greeting position, card dimensions, table bounds, and monitoring alignment.
2. Confirm that greeting, metrics, incidents, and current application states remain the primary reading order.
3. Check real long application names, large numbers, long messages, and empty states without shifting the core layout.
4. Verify that new screens share spacing, surface treatment, icon weight, typography, and state semantics.
5. Confirm accessible contrast, focus, labels, and hit areas while retaining the approved visual character.
6. Keep detailed investigation below the Home summary level. Do not fill intentional whitespace with extra widgets.

Current status: visual baseline documented; no Home Page redesign and no application code generated. The workspace had no existing screens to update.

Current boundary (2026-09-24): Phase 5 accepted; Phase 6 documentation proposal only. Earlier implementation-boundary notes are historical. This update does not change visual observations or assert a fresh Canva inspection. Home PNG remains unchanged; exact Canva properties and a local Search export remain unverified.

## Phase 6 authorized implementation — latest authority (2026-09-24)

The user approved Phase 6 implementation with exact Canva fidelity and the two narrow read-only query additions. See docs/reference/phase-6-approval.txt. Preserve all 382 accepted tests and backend behavior. Frontend work is authorized; earlier proposal-only statements are historical and superseded. No Phase 7, deployment, real GiroSol, notifications, collectors or AI.

The user subsequently confirmed the CURRENT live Canva mapping: **Page 1 Home; Page 2 Monitoring; Page 3 Search**. These are three separate approved screens, not alternate Search designs. Earlier two-page mappings are obsolete. Match all three; preserve the original Home image as a historical unchanged reference. Current Canva API page dimensions are 1366×768; visible design content includes a 1366×736 region. Exact API element geometry is recorded in docs/reference/canva-phase6-elements.json. Do not stretch the earlier raster to resolve this difference.

Greeting: use authenticated account display/login name; no hardcoded Salvador. Morning/Afternoon/Evening follows SONDA reporting/server timezone. Preserve greeting typography, location and structure. The smallest read-only session name field is approved.

Home Error Logs Info opens a centered Incident modal over Home: visible background with slight blur/subtle darkening, white rounded shadowed panel, top-right X, narrow left sections Overview / Order-Run / Evidence / History and larger right detail. Preserve page scroll and return focus on close. Use backend allowedActions. Overview includes application, severity/status, summary, first-detected/last-updated, identifiers, linked runs/result/version; Order-Run includes parent, attempt/previous attempt/recovery; Evidence is chronological and grouped with explicit provenance; History preserves transitions/recovery/manual actors/times. Do not invent absent fields or group distinct facts implicitly.

Five Phase 4 environmental gates remain pending separately. Phase 6 completion requires actual reference-versus-implementation captures/overlays, tests and review; no generated baseline is automatic approval.

Current Phase 6 instructions: [approved interaction amendments](architecture/phase-6-interaction-amendments.md) and [live Canva measurements](reference/canva-phase6-measurements.md). Mapping is Home1 / Monitoring2 / Search3; all earlier two-page mappings are historical. See the amendments for grouped evidence Search, modal details, fullscreen monitoring and navigation.
