# Live Canva Phase 6 inspection — 2026-09-24

Authority: design DAHV7wwVO9g, **Sonda Home Page Design**. Current user-approved mapping is **1 Home / 2 Monitoring / 3 Search**. The editor now labels them Home Page, Monitoring Page and Search Page. Earlier two-page records are historical. All three screens were directly inspected in the editor this session. The read-only connector transaction was cancelled; no design edits were requested/performed.

Machine-readable exact element rectangles: [canva-phase6-elements.json](canva-phase6-elements.json). Connector page metadata reports 1366×768; the Home background/content element reports 1366×736. Preserve both observations rather than stretching the old raster. Full-resolution capture/export comparison must settle the final screenshot framing before visual sign-off. Existing approved Home PNG is unchanged.

| Property | Observed value / provenance |
|---|---|
| Home central shell | x73.600, y10.953, w910.595, h712.698; connector |
| Home/Search monitor shell | x995.195, y10.953, w356.973, h712.698 on Home; page-specific Search rectangles in JSON |
| Home utility | x95.184, y25.430, w872.706, h35.009; connector |
| Home cards | x92.595 /390.478 /687.803; y155.040; widths282.676 /282.220 /282.676; height109.732; connector |
| Home table | x90.114, y295.684, w877.884, h414.623; white body starts y320.682; connector |
| Greeting container | x120.165, y98.771, w338.522, h31.667; connector. Text is centered within this box, not left-aligned at its left edge |
| Greeting typography | **Canva Sans**, editor font size **20**, normal/non-bold. Rendered DOM font26.6664px, weight400, line-height37px, centered, letter-spacing0em, black. Editor point-like size and DOM pixel size must not be conflated |
| Greeting subtitle | x120.165, y129.220, w338.522, h12.089; connector |
| Card label typography | Canva font family ID YAFdJjTk5UU_0 (same as selected Canva Sans); DOM12px, weight400, line16px, black. Nested element scaling must be considered when mapping to final screenshot |
| Monitored-name typography | Same Canva Sans family ID; DOM13.3335px, line18px, weight400, left aligned, black |
| Monitoring central shell | x73.600,y10.953,w1280.601,h712.698; **no right panel** |
| Monitoring utility | x95.427,y29.059,w1236.946,h35.009 |
| Monitoring rows | x95.427,w1236.946,h21.922; y76.811/104.229/131.562/158.200; connector |
| Monitoring names/dots | names x136.610; dots x1275.437,w15.153,h15.333; row name/dot y80.105/107.524/134.857/162.190 |
| Home table row pitch | 22.089 design units from consecutive text/pill rectangles |
| Search | Page3; rounded groups, Application/Time/Message/**Status** columns. “Failed Run” and “Successful Run” describe Order results, per user clarification. Exact text/group rectangles in JSON |
| Rail icons | Exact asset rectangles retained in JSON; Home icon x22.203,y108.685,w21.914,h20.129; Search icon x23.851,y190.161,w21.774,h21.772; avatar x12.334,y678.565,w41.162,h41.162 |
| Rail width/gutters | Rail~64; outer central offset73.600; central/right gutter11.000. Rail width is a measured visual region, not an exposed discrete Canva property |
| Colors/radii/shadows/dividers | Connector does not expose complete styling. Prior visual guide remains approximate, not exact extraction. Editor exposed background element color #fffcf5. Remaining exact fills/opacity, corner and shadow settings still require inspection/measurement before sign-off |

Canva Sans has been identified, so a system-font substitute is not treated as approved. The webfont delivery/fallback question is pending. Do not download internal Canva font resources and assume a web embedding license. Canva's [typography documentation](https://www.canva.dev/docs/apps/design-guidelines/typography/) describes Canva Sans for apps inside Canva; it does not establish a standalone SONDA embedding grant.

Reference captures/overlays and final approved implementation screenshots are not yet produced. These observations are inspection evidence, **not visual completion**.

Framing resolved: the official all-page PNG export at size 1 is 1366x736 for each page. Use this native viewport for comparisons. User-approved Inter substitutes only the font, preserving the measured element geometry.
