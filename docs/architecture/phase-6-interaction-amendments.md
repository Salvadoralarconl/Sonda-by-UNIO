# Approved Phase 6 interaction amendments

2026-09-24. These subsequent user instructions supersede conflicting proposal details; implementation remains bounded to Phase 6. Preserve all accepted backend behavior/tests.

- Current live Canva: **Home1 / Monitoring2 / Search3**, each separately authoritative. Read [exact inspection record](../reference/canva-phase6-measurements.md).
- Greeting keeps Canva geometry/type and uses authenticated display/login name plus server reporting timezone. No hardcoded person, browser-local timezone or silent fallback.
- Monitoring fills the central workspace; no compact side panel. Row backgrounds represent returned business health: Stable green, Warning yellow, Error red. Unknown business state stays neutral. Availability remains separate; stale/disconnected/unknown/not-observed adds a minimal clear qualifier, never a false healthy-green claim.
- Each Monitoring row opens an application-only modal without navigation. Show authoritative selected-app health/availability/counts and bounded recent incidents/Investigating/resolved history/runs/Order failures. Do not count only a loaded page and present it as a total. Any needed read-only count projection must remain a projection of existing facts, not a health rule.
- Home and Search retain compact Logs Monitored panels. Both fullscreen affordances enter a dedicated expanded monitoring mode with names, business health/colors and availability qualification; obvious exit, no unrelated account/config/Search/dashboard controls. Browser fullscreen is optional; layout mode must work when permission is denied.
- Bell remains in place, inactive, no count/dropdown, accessible “Notifications are not available yet.”
- The lower-left icon **above Settings is Configuration**. Admin visual forms cover all accepted Profile/source parameters, including include/exclude patterns; no normal JSON editing or file browser.
- Home Info opens a centered Incident modal; mild blurred/darkened visible backdrop, white rounded shadowed surface, X, narrow left Overview/Order-Run/Evidence/History navigation. Preserve Home scroll/focus. Display persisted facts/provenance, allowedActions/revisions/idempotent workflow. No inferred transitions.
- Search stays evidence-centric and deduplicated. One rounded Order block contains matching evidence for that persisted Order Run. Status is Order Detection Result, not Incident workflow. Preserve attempts and scope, not fuzzy transaction grouping.
- Unassigned/unlinked diagnostics, Ignore, unmatched/quarantine/out-of-order lines remain searchable in clearly labeled neutral Unassigned Evidence blocks. No fabricated Failure or Order result. An incomplete/truncated link lookup is not proof of no association: label it incomplete.
- Shared evidence has one full row plus references to every linked Order Run. Other blocks may contain a compact shared-evidence reference, not a copied raw row. Grouping affects presentation only; original evidence identities and backend relationships stay intact.
- Bounded evidence pagination is retained. A group may contain only matching evidence on the current page; show this limitation in expanded context. Never fetch all history to fill a visual group. Filters reset cursors and requests. Existing Search endpoint semantics/regression assertions remain unchanged.

Acceptance additions: three-page fidelity; business-health row fill plus availability; selected-app isolation and truthful counts; both fullscreen entry points, permission-denied layout fallback and exit; modal keyboard focus/Escape/scroll return; shared evidence once with all references; Unassigned neutral and incomplete links explicit; Order result versus workflow labels; source exclude-pattern round-trip; personalized greeting at noon/evening/midnight and DST with a viewer in another timezone.

Greeting presentation boundaries currently implemented in the pure formatter: 00:00–11:59 Morning, 12:00–17:59 Afternoon, 18:00–23:59 Evening in the reporting timezone. This does not change the reporting date/metric policy. The session read projection provides server time and its IANA timezone equivalent, avoiding a browser timezone fallback for Windows server timezone IDs.

Two outstanding visual choices remain explicitly pending: standalone Canva Sans webfont availability/substitution approval, and meaningful authoritative values for the illustrative overall Stable/Warning metric-card badges. Neither was implicitly resolved by these amendments.

## Approved Home badge projections — 2026-09-24

Both Canva badges remain in their original cards. `currentSystemBadge` is calculated inside the Dashboard repeatable-read snapshot from activated monitored applications only: Error before Warning before Stable. It is independent of the daily success percentage. `businessHealth` remains separate from availability; `isQualified`, `qualification` and `availabilityIssues` must be rendered together when qualified. Required-source availability other than Fresh prevents an unqualified Stable. An empty monitored set returns null business health with “No monitored applications.” It must not render green Stable. Created but unactivated applications do not become monitored applications merely by existing.

`activeIncidentsBadge` uses all unresolved Incidents in the team's monitored sessions, not the latest-50 display page. Active and Investigating count; resolved Incidents do not. Error wins over Warning; none returns Stable. React must display these returned projections, not infer them from percentage, loaded incidents or independent severity rules. No domain outcome, contribution fact or acquisition behavior changes.

Typography remains pending explicit approval of a substitute. See `../reference/font-licensing-review.md`. The prior pending-badge decision is superseded by this approval.
