# Phase 1 implementation review

Status: **implemented and accepted by the user; its 42 passing tests are the reference contract for later phases**. Results below record the Phase 1 delivery. [Phase 2](architecture/phase-2-scope.md) is now implemented and stopped for review; see [Phase 2 results](phase-2-review.md).

## Delivered

- `Sonda.Domain`: typed Profile/configuration contracts; parsing and bounded matching; serial cycle/order interpretation; explicit Problem Identity; Incident/Occurrence/recovery model; pure metric projections.
- `Sonda.Application`: deterministic isolated simulation orchestration, validation, provenance hashes, per-input traces, rule coverage, and readable/JSON reports.
- `Sonda.Simulator`: console runner accepting a sample JSON document. Phase 1 included no server, database, account system, browser UI, file watcher, or production deployment.
- `Sonda.Phase1.Tests`: 42 passing xUnit cases, including the user's three amendments.

The approved Home image and visual guide are unchanged. Application-specific phrases appear only in fixtures/tests, not the engine.

## Source entry points

| Responsibility | Source |
| --- | --- |
| Profile model | [Profile.cs](../src/Sonda.Domain/Profiles/Profile.cs) |
| Competing-rule validation | [ProfileValidator.cs](../src/Sonda.Domain/Profiles/ProfileValidator.cs) |
| Matching and overlap analysis | [PatternMatcher.cs](../src/Sonda.Domain/Detection/PatternMatcher.cs) |
| Parsing / extraction | [SampleParser.cs](../src/Sonda.Domain/Evidence/SampleParser.cs) |
| Exact Problem Identity / Incident Key | [ProblemIdentity.cs](../src/Sonda.Domain/Incidents/ProblemIdentity.cs) |
| Cycle/order and incident transitions | [ProfileInterpreter.cs](../src/Sonda.Domain/Processing/ProfileInterpreter.cs) |
| Health and completed-order metrics | [MetricCalculator.cs](../src/Sonda.Domain/Metrics/MetricCalculator.cs) |
| Simulation orchestration / provenance | [SimulationRunner.cs](../src/Sonda.Application/Simulation/SimulationRunner.cs) |
| Console interface | [Program.cs](../tools/Sonda.Simulator/Program.cs) |
| Mixed-order and recovery tests | [EngineAcceptanceTests.cs](../tests/Sonda.Phase1.Tests/EngineAcceptanceTests.cs) |
| Priority tests | [PriorityAndValidationTests.cs](../tests/Sonda.Phase1.Tests/PriorityAndValidationTests.cs) |
| Parsing / metric / report tests | [ParsingMetricsAndReportTests.cs](../tests/Sonda.Phase1.Tests/ParsingMetricsAndReportTests.cs) |
| Invalid-input and policy boundaries | [BoundarySafetyTests.cs](../tests/Sonda.Phase1.Tests/BoundarySafetyTests.cs) |

## Verified results

Release simulator build: **0 warnings, 0 errors**. Automated tests: **42 passed, 0 failed, 0 skipped**. Original test evidence: [TRX results](../artifacts/test-results/phase1.trx); latest preservation run: [Phase 1 TRX](../artifacts/phase2/phase1.trx). The original results alone do not claim production persistence or crash safety; Phase 2 has separate evidence.

| Report | Expected and observed outcome |
| --- | --- |
| [Mixed orders](../artifacts/phase1/mixed-orders/report.md) / [JSON](../artifacts/phase1/mixed-orders/report.json) | Parent Success; 3 orders (2 Success, 1 Failure); 1 Active Warning; Logs Today 3; System Health 3/4 = 75% |
| [Repeated recovery](../artifacts/phase1/repeated-recovery/report.md) / [JSON](../artifacts/phase1/repeated-recovery/report.json) | 3 cycles and 3 order attempts; one Incident with 2 failed Occurrences, then one preserved successful recovery; Resolved/Stable; Logs Today 3; System Health 4/6 = 66.67% |
| [Priority override](../artifacts/phase1/priority-ignore/report.md) / [JSON](../artifacts/phase1/priority-ignore/report.json) | Specific Ignore priority 20 beats broad Error priority 5; both matches and suppression/overlap warning shown; no incident; one successful cycle; Logs Today 0 |

The numeric fixture verifies 985 successful / 1006 evaluated = **97.91%** with **1000 completed orders**, excluding the 6 application cycles from volume. Tests also verify Undefined at parent close, EOF remaining Running, application recovery, interleaved identifiers, cross-Profile separation, explicit End, deterministic reports, date/time parsing, source timezone ambiguity, exact key escaping, stale provenance, and incomplete-policy diagnostics.

Equal priorities are allowed for unrelated exact predicates and distinct targets. Proven competing predicates are rejected at validation. Arbitrary regex overlap is not claimed decidable by the validator: unknown intersections receive a warning, and an actual equal-priority collision in a sample stops that lane without selecting a winner.

## Run locally

Requires .NET SDK **10.0.401** (pinned in `global.json`). This workspace has a local SDK at `.tools/dotnet/dotnet.exe`; it was needed because the machine originally had only the .NET runtime. The Phase 1 engine uses no third-party production packages; test dependencies are pinned with lockfiles. Phase 2 adds EF/Npgsql separately.

From the repository root, with an installed SDK available as `dotnet`:

```powershell
dotnet restore Sonda.sln --configfile NuGet.Config --locked-mode
dotnet build Sonda.sln -c Release --no-restore
dotnet test tests/Sonda.Phase1.Tests -c Release --no-build --no-restore --logger 'trx;LogFileName=phase1.trx' --results-directory artifacts/test-results
dotnet tools/Sonda.Simulator/bin/Release/net10.0/Sonda.Simulator.dll --input tests/fixtures/mixed-orders.json --output artifacts/phase1/mixed-orders
```

For the workspace-local SDK, replace `dotnet` with `& '.\.tools\dotnet\dotnet.exe'`. In the restricted Codex environment, restore needed network/user-NuGet-config access; subsequent no-restore builds/tests ran locally. NuGet packages used for this run are under `.tools/packages`. No machine-wide SDK installation was required.

Use `tests/fixtures/repeated-recovery.json` and `tests/fixtures/priority-ignore.json` for the other reports. CLI exit codes: 0 for complete interpretation, 2 for an incomplete report with diagnostics, 1 for malformed input/output failures, 64 for invalid command arguments. Reports are written to the explicitly supplied output directory; existing `report.md`/`report.json` there are replaced.

## Sample contract and interpretation limits

Input is UTF-8 JSON, at most 16 MiB, containing Profiles, explicit ordered sample entries and processing timestamps, a server timezone, an `asOf` instant, seed, and optional sample date. Omit optional properties instead of using explicit null. Unknown properties and integer enum values are rejected. Entries are single logical lines in this phase; production multiline framing is deferred. Profiles define timestamp/message extraction, key-value or named regex identifier extraction, alternatives, case behavior, and priorities.

Limits: 100 Profiles/session, 10000 entries, 128 rules/Profile, 16 alternatives/rule, 2048-character pattern expressions, 65536-character sample lines, 50 ms per regex match and a 250 ms total matching budget per input. Pathological regex or invalid parsing produces diagnostics and blocks dependent interpretation; reports retain original input. Sample generation time and seeded IDs are explicit, so ordinary successful fixtures reproduce the same semantic reports.

`ProblemIdentity` is an exact tuple of team, application, Profile, target scope, stream, identifier namespace/value where applicable, and condition key. Its canonical JSON tuple encoding prevents separator collisions. Cycle IDs belong to Occurrences, allowing the same problem to connect across cycles. Reports show `CreatedIncident`, `AppendedOccurrence`, and `ResolvedBySuccessfulRun` with the exact calculated key. No fuzzy matching or application-name inference is used.

The simulator only uses memory. Its completion-processing timestamp models processing, not an actual database commit. Phase 1 reprojection deduplication tests alone do not claim crash recovery. Simulation zeros describe only supplied evidence, not monitoring coverage.

Profile timing fields are represented and validated, but fallback scheduling is explicitly reported as `TimingNotImplemented`. Sample EOF never becomes a business timeout. Unapproved same-cycle retry, overlapping cycles, conflicting structural outcomes, terminal Success with its own diagnostic health problem, and structural Failure plus winning Ignore yield `PolicyDecisionRequired` rather than guessed business outcomes. Independent Profiles can still run when another lane is blocked; partial results are labeled incomplete.

Publication/activation, the visual sample-upload workflow, durable storage, file monitoring, authentication, the website, and deployment were outside the Phase 1 increment. The Phase 2 review separately documents the now-implemented persistence boundary. Phase 3 remains unauthorized.
