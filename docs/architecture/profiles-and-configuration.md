# Profiles and configuration lifecycle

Status: overall direction and the five pre-implementation decisions approved; Phase 1 is implemented and stopped for review before Phase 2. Profile ownership is team-wide: an admin configures logs once, and members share interpretation and results. Remaining policy details are identified in the decision register.

## Profile contract

One Application owns one stable Profile in the first release. A Profile has mutable drafts and immutable published versions. Versions carry complete settings, never a patch that depends on whichever version happens to be current.

| Section | Required data | Behavior |
| --- | --- | --- |
| Identity | Application name, description, Profile key, enabled | Stable ownership independent of display-name edits |
| File locations | Array of sources, monitoring host, path/glob, exclusions, encoding, framing, stream association | Multiple paths are represented from the start; no shared global log directory |
| Log Parsing | Timestamp, message, level, and metadata extractors; format presets/custom fields; parse error behavior | Detection normally targets the parsed message while original evidence remains intact |
| Timestamps | Parser format/field, source timezone for timestamps lacking an offset, missing/invalid timestamp policy | Source timezone is separate from the dashboard's “Today” timezone |
| Application Run Cycles | Begin, Success, Failure, optional End; correlation mode; reliable-boundary mode; optional profile-specific expected duration/grace | Match ANY alternatives; deterministic boundaries first; no global business timeout |
| Order grouping | Identifier label and extractor(s), normalization, identity namespace, Begin/Success/Failure/optional End, optional fallback timeout/grace | Every attempt belongs to its cycle; unfinished orders become Undefined at cycle close; retry identity spans cycles within the same application/Profile |
| Detection rules | Condition key, scope, alternatives, Error/Warning/Ignore, priority, recovery policy | Configured meaning; no keyword-based inference |
| Monitoring behavior | Initial read policy, incomplete-run handling, overlap policy, limits | Explicit, validated choices with safe previews |

Each Log Source has its own Enabled flag. Disabling one stops new capture for that source, preserves its cursor/history, and leaves other enabled sources active. Disabling a Profile stops all its sources. Completion of already-captured input and active runs follows the documented pause policy; disabling never deletes history or implies success.

## User-configured parsing

SONDA knows parser primitives, not application log formats. Provide simple field/delimiter/prefix-based extraction and optional regex captures for timestamp, level, message, and metadata. A configured structured-record parser can be an adapter when justified by samples. Parsing a level named ERROR does not assign SONDA severity unless a detection rule explicitly uses that field.

Normal matching targets the extracted message by default; advanced rules may explicitly select raw text or a parsed field. Preserve both the original bytes and the parsed representation with parser version and quality flags. A Profile sample preview shows exactly what is extracted before any lifecycle interpretation.

Timestamp format uses a documented UI convention or a validated runtime-specific format with examples. Do not pass the specification's illustrative `YYYY-MM-DD HH:mm:ss` directly to .NET, whose format tokens differ. The UI should translate its chosen convention explicitly and preview real parsed instants. Logs containing only `10:00:00` require configured date context (for example a filename date); do not silently infer the current day during backfill. Invalid dates, ambiguous local times, and parsing failures are visible diagnostics.

Configuration fields are typed and versioned. A visual form owns ordinary editing; advanced regex is optional. Do not make normal users edit raw JSON. Internally, a canonical export enables backups, comparisons, simulation, and integrity hashing.

### Pattern model

Each rule has a stable key, role, target field, target scope, and an ordered collection of alternatives. An alternative defines Contains, Exact, or Regex, its expression, case sensitivity, and supported options. The rule matches if ANY alternative matches. Record which alternative matched for explanation. Alternatives within one rule do not produce multiple occurrences for the same input/target.

Recommended form defaults, following the parameter specification's suggestions: Contains and case-insensitive matching. The saved values remain explicit, and identifier normalization remains independent of pattern case sensitivity. An identifier's case is not automatically changed because its extraction pattern ignores case.

Structural roles determine lifecycle events; detection roles classify conditions. For a classification decision on the same input/target, all active matching detection rules compete by explicit priority, highest first. The winner alone supplies the classification and condition for that decision, even if it is Ignore. Lower-priority rules do not create extra incidents just because they have different condition keys. Do not use Error > Warning > Ignore as a fallback. Separately correlated targets are evaluated separately; successful Begin/End recognition is not a competing severity. Interaction between a separately configured structural Failure and a winning Ignore remains an explicit D04 follow-up, not an implicit forced Error.

Priority is a saved numeric field. Equal priorities are invalid only when active rules can compete for the same input/target. Unrelated rules may share priorities. Static validation rejects proven overlaps at equal priority; unproven regex intersections are warnings, and simulation rejects an actual equal-priority collision without choosing by severity, rule ID, or insertion order. No blanket per-scope numeric uniqueness constraint is permitted.

Application completion mode is explicit: `TerminalMarker` closes on configured Success/Failure; `ExplicitEnd` records outcome evidence and closes on the independent End marker. Both are required by the additional specification. Outcome conflict precedence in ExplicitEnd mode remains an approval item (D03).

### Identifier extraction

Offer simple key/value extraction (for example a configured `OrderID=` prefix), structured-field selection, and advanced regex with a named capture. The user chooses the label; the engine never assumes it is literally “Order ID.” Store IDs as text to preserve leading zeros and letter case.

Proposed default normalization is identity-preserving: no case folding, numeric conversion, trimming of captured meaningful characters, or separator removal unless configured. Alternative extractors have an explicit order; if successful alternatives disagree, quarantine the association and show a configuration diagnostic instead of choosing silently.

An `identityNamespace` records the meaning of the extractor. Cosmetic rule edits keep it stable so later successes can resolve older incidents. Changing what the identifier means requires a new namespace and review of unresolved incidents. Automatic cross-namespace migration is forbidden. The user-confirmed base identity is `(team, application, profile, namespace, identifier)`; cycle ID belongs on each attempt, not in the identity used for cross-cycle recovery.

## CAM example as configuration data

This is a human-readable proposal, not an executable configuration file. Paths, durations, source timestamp rules, and production phrases must be supplied/verified before activation.

| Setting | Example value |
| --- | --- |
| Application / Profile | CAM / cam |
| Cycle Begin → Match ANY | Contains “CAM process started” |
| Cycle Success → Match ANY | Contains “CAM process completed” |
| Cycle Failure → Match ANY | Contains “CAM process failed” |
| Cycle failure classification | Error |
| Order identifier | Key/value field named OrderID |
| Identifier namespace | cam-order-v1 |
| Order Begin → Match ANY | Contains “Finding order” |
| Order Success → Match ANY | Contains “Order sent” |
| Order Failure → Match ANY | Contains “Unable to send order” |
| Order failure classification | Warning |
| Ignore example | Contains “Retrying connection”; only if the admin explicitly adds this rule |
| Incomplete order | Undefined at parent-cycle close without an outcome; severity configured by Profile. Optional Profile timeout/grace is a fallback without a reliable boundary; no global timeout |

In an actual Profile, several text/regex alternatives may be supplied for every marker. These CAM strings appear only in fixtures and configuration. There will be no CAM-specific branches in application code.

## Validation before publication

Validate nonempty alternatives, supported pattern types, regex compilation, execution limits, identifier capture names, unique rule keys, enabled sources, compatible versions, nonempty state-transition rules, and explicit incomplete-run policies. Reject impossible structural references and ambiguous equal priorities. Warn about overlapping active detection rules even when different priorities make the winner deterministic; intentional overlap remains valid.

Static validation can identify duplicate predicates and straightforward overlaps; sample simulation checks every active rule and flags every observed multi-match input with rule IDs, priorities, and the winner. Do not claim static analysis can prove arbitrary regex rules never overlap. Show sample coverage and unexercised rules. Multiple matching alternatives within one rule are one rule match, not an inter-rule conflict.

Test source access using the monitoring process's identity, not the admin browser's identity. Canonicalize paths on that host, respect configured allowed roots, reject accidental overlapping ownership of the same physical files, and show initial-read consequences. Credentials belong in protected host configuration, not pattern definitions or exported Profile files.

## First-class Profile simulation workflow

The user can paste or upload sample logs against a draft before publishing or activating it. Supply source/encoding/date context and a simulated clock where necessary; no production directory access is implied. The same parser, matcher, state machines, and incident logic used in production run in an isolated session. Simulation never changes live cursors, runs, incidents, or dashboard counters.

The report exposes:

- Original sample lines and parsed timestamp/message/fields, including parse failures and unmatched evidence.
- Every extracted identifier, its Profile scope, cycle association, and resulting order attempt.
- All matching rules/alternatives with priorities, winning and suppressed classifications, and overlap/tie warnings.
- Application Runs and Order Runs with boundaries, evidence links, Success/Failure/Undefined results, and still-Running states where evidence has not closed a run.
- Resulting severities, Incident identities, repeated Occurrences, workflow history, and successful recovery links.
- Current application state and metric contributions, including completed Order Runs processed on the simulated server date.

Sample EOF is not a synthetic cycle close. Users can supply a real configured End marker or advance the simulated clock for an explicitly configured fallback policy. This makes missing-boundary behavior inspectable without inventing Undefined outcomes merely because a sample ended.

Store simulation provenance: draft content hash, sample-set hash, engine/policy versions, timezone, simulated clock inputs, report ID, warnings/errors, and review state. Any interpretation-changing draft edit makes the prior report stale. No claim of production correctness follows merely from a passing small sample.

Proposed publication gate: require a reviewed, non-stale simulation report for the exact draft being published; structural/priority errors block publication, while explained overlap warnings may be acknowledged because intentional Ignore overrides are valid. Activation verifies the published version/report association. This gate implements the approved pre-activation workflow; its precise review controls will be reviewed with the website phase. Phase 1 provides the runnable engine/report contract; the paste/upload website flow follows in Phase 6.

## Editing, publishing, and activating

1. Admin clones the active version into a draft and edits using revision checks.
2. Validate and simulate sample logs as a first-class step. Review the complete report and semantic diff: sources, identities, priorities, classifications, boundaries, and fallback timing. Any subsequent interpretation edit requires a fresh report.
3. Publish an immutable version with author/time/hash and reviewed simulation-report reference under the proposed gate. Publishing does not immediately change processing.
4. Request activation. The recommended first implementation waits for a quiescent application boundary: no open cycles/orders, no pending multiline frame, and all records up to a captured source boundary interpreted with the old version.
5. Briefly pause acquisition for that application, drain captured complete-line boundaries, and commit the activation and per-file offsets. The next records use the new version. A line split across the cutover remains wholly owned by one version; never split its bytes between parsers.
6. Existing runs, matches, incidents, and evidence keep their old version references. Already committed receipts are not reevaluated automatically.

If a cycle never closes, activation remains visibly pending. Apply the already-approved old-version deadline policy or let the admin explicitly choose a reviewed cutover; do not silently close a run to publish a Profile. For continuously overlapping workloads with no quiescent boundary, multi-version concurrent interpretation is a later design extension. Do not pretend the initial barrier works for them (D09).

Keeping only future inputs under the new version means historical failures are not magically reclassified or resolved after editing a rule. A later success may still resolve a prior-version incident when application/Profile, identity namespace, recovery condition, and temporal order are compatible.

### Source and enabled-state changes

Preserve cursors for unchanged physical files. Adding a source uses its explicit initial-read policy; changing a directory never implies rereading existing files. Removing a source stops new discovery after the boundary and retains evidence/history. Disabling an application pauses monitoring after a clean checkpoint; it does not mark incidents Resolved or history successful. Re-enabling resumes from durable positions while reporting retention gaps.

Disabling mid-cycle, emergency cutover, and forced abandonment require an explicit reason and a reviewed outcome policy. They must not create invented Success outcomes.

### Reprocessing history

Reprocessing is an explicit isolated operation over preserved raw evidence with a chosen Profile/engine version. It produces a comparison report, never mutates live history or resolves live incidents by default. Promoting corrections would require a separate, audited design and user action; it is outside the first release. Simulator runs cannot send notifications or read production directories implicitly.
