# SONDA approved Canva visual reference

**Current authority: Page 1 Home, Page 2 Monitoring, Page 3 Search. User explicitly approved the live three-page mapping on 2026-09-24. [Fresh inspection and exact geometry](canva-phase6-measurements.md). Earlier two-page observations below are historical.**

## Authority

The user designates **@Canva → [Sonda Home Page Design](https://www.canva.com/design/DAHV7wwVO9g/ey_I9os2basGua69TXtdFA/edit)**, including all pages, as SONDA's permanent primary approved visual source of truth. Both pages have now been inspected through rendered screenshots and accessibility content in the Canva editor. No design edits were made.

Canva defines appearance. The approved architecture, business rules, accepted Phase 1–5 implementation, simulator behavior and all 382 passing tests define behavior. Illustrative counts, chart marks, labels or states in a design do not replace domain rules.

Preserve overall proportions, left navigation rail, central workspace, separate right monitoring panel, spacing/gutters, card dimensions, typography hierarchy, icon placement/sizing, shadows, border radii, status colors, table layout, visual density and interaction hierarchy. Do not substitute a generic dashboard or reinterpret the visual language. No redesign without an explicit user request. Explain any technical/accessibility/responsive limitation and obtain a design decision before changing the approved appearance.

The [permanent visual guide](../visual-design-guide.md) retains the earlier raster observations. The [approved Home image](sonda-approved-home.png) remains an unchanged local reference. Its measurements are approximate and must not be presented as extracted Canva properties.

## Access and review record

| Item | Verified status |
| --- | --- |
| Design name | Sonda Home Page Design — supplied by user |
| Canva plugin | Installed/enabled, confirmed through plugin discovery |
| Design URL / design ID / revision | Linked above; ID `DAHV7wwVO9g`; title “Sonda Home Page Design - Website”; immutable revision identifier not exposed |
| Design-reading tools | Not exposed in this session |
| Browser access | User opened the design; both pages accessible in editor |
| Page count / page names | 2 pages, confirmed by editor; user identifies Page 1 Home and Page 2 Search; Canva page-title fields themselves are unset |
| All-page visual review | Completed for both visible pages; numerical font/color/shadow extraction remains unverified |
| Remote design changes | None |

Review provenance: inspected during the current Canva-reference request after the user's Home/Search clarification, at editor zoom 56%. Both complete pages were viewed. Screenshots were inspected inline; no new local Canva export or immutable snapshot was created. The existing Home PNG remains the local snapshot. Future sessions should reopen the exact link and identify changes rather than silently replacing the approved baseline.

## Reviewed pages

### Page 1 — Home

The rendered page agrees with the supplied Home baseline: narrow icon navigation rail, dominant central workspace, separate tall right monitoring panel, compact gutters and shallow utility bar. A greeting and supporting line precede three similarly sized System Health, Logs Today and Active Incidents cards. The tall Error Logs table uses Application, Time, Severity, Message and Status columns, with Message widest; fine vertical separators, pale header and compact state pills preserve sparse density. The monitor has its own header/expand icon, left-aligned names and right-aligned green/yellow/red dots. Lower navigation tools/settings and circular avatar stay near the bottom.

### Page 2 — Search

Search reuses the narrow rail, shallow utility bar and separate full-height monitoring panel. The bar identifies Search with a magnifier; notification/search utilities remain right-aligned. The results table begins directly below the bar without Home's greeting or metric cards. Columns are Application, Time and Message, with Message much wider. The example has three time/message rows under CAM, then a second three-row CAM group. Fine horizontal rules separate groups; a thin vertical rule follows the Application column. This visual example does not establish query/grouping business semantics.

The Search table is a rounded white surface with a diffuse shadow and substantial blank body space. It ends above the bottom of the central workspace, unlike Home's taller table; the workspace continues below it. Preserve this distinction. The monitor retains CAM/UNITELLER/DIGICEL/TERRAPAY examples, aligned dots and generous empty space. No severity/status columns or large filter toolbar are visible here; do not substitute Home's table layout.

### Shared appearance and interaction hierarchy

Both pages use warm pale-neutral surroundings, white surfaces, regular-weight sans-serif typography, near-black primary text, muted small labels, compact line icons, soft shadows and rounded surfaces. Hierarchy comes from placement, spacing and size; pastel colors are localized to status information. The rail selects a screen, the utility bar identifies it, the central workspace carries its task, and the monitor supplies secondary awareness. Hover/focus behavior and responsive variants were not demonstrated by these static pages. Exact font families, hex colors, radii and shadow values have not been extracted; existing numerical guide values remain estimates.

## Implementation boundary

No frontend, Home Page or Canva implementation is authorized now. Phase 2 durable persistence is implemented and stopped for review. Phase 3 requires approval. Access to Canva does not authorize implementation or change the approved domain contract.

Current boundary (2026-09-24): Phase 5 accepted; Phase 6 documentation proposal only. Earlier implementation-boundary notes are historical. This update does not change visual observations or assert a fresh Canva inspection. Home PNG remains unchanged; exact Canva properties and a local Search export remain unverified.

## Phase 6 authorized implementation — latest authority (2026-09-24)

The user approved Phase 6 implementation with exact Canva fidelity and the two narrow read-only query additions. See docs/reference/phase-6-approval.txt. Preserve all 382 accepted tests and backend behavior. Frontend work is authorized; earlier proposal-only statements are historical and superseded. No Phase 7, deployment, real GiroSol, notifications, collectors or AI.

The user subsequently confirmed the CURRENT live Canva mapping: **Page 1 Home; Page 2 Monitoring; Page 3 Search**. These are three separate approved screens, not alternate Search designs. Earlier two-page mappings are obsolete. Match all three; preserve the original Home image as a historical unchanged reference. Current Canva API page dimensions are 1366×768; visible design content includes a 1366×736 region. Exact API element geometry is recorded in docs/reference/canva-phase6-elements.json. Do not stretch the earlier raster to resolve this difference.

Greeting: use authenticated account display/login name; no hardcoded Salvador. Morning/Afternoon/Evening follows SONDA reporting/server timezone. Preserve greeting typography, location and structure. The smallest read-only session name field is approved.

Home Error Logs Info opens a centered Incident modal over Home: visible background with slight blur/subtle darkening, white rounded shadowed panel, top-right X, narrow left sections Overview / Order-Run / Evidence / History and larger right detail. Preserve page scroll and return focus on close. Use backend allowedActions. Overview includes application, severity/status, summary, first-detected/last-updated, identifiers, linked runs/result/version; Order-Run includes parent, attempt/previous attempt/recovery; Evidence is chronological and grouped with explicit provenance; History preserves transitions/recovery/manual actors/times. Do not invent absent fields or group distinct facts implicitly.

Five Phase 4 environmental gates remain pending separately. Phase 6 completion requires actual reference-versus-implementation captures/overlays, tests and review; no generated baseline is automatic approval.

Full official Canva PNG export (version 162, 2026-09-24) now preserves all three pages under docs/reference/canva-phase6-export. Each is 1366x736; this resolves screenshot framing without stretching. Inter is the explicitly approved webfont substitute. See artifacts/phase6/visual-comparison.html for reference/implementation comparison; no final visual acceptance is claimed.
