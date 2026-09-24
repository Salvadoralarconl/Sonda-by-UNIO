# Phase 4 handoff checkpoint

Implementation is approved by `docs/reference/phase-4-approval.txt`. Local implementation and automated verification are ready for review, **not full production capability sign-off**. Read `docs/phase-4-review.md` and `docs/development/file-acquisition.md` first. No real GiroSol files were accessed and Phase 5 remains unauthorized.

Final uninterrupted run: **309/309 passing**, zero skipped/failing; **237/237 accepted regressions**, **180/180 PostgreSQL tests**, 11 new framing/Windows tests. There are 72 new Phase 4 cases (61 PostgreSQL +11 acquisition). Authoritative TRX: `artifacts/phase4/verified-tests`; summary: `artifacts/phase4/test-summary.json`. `tools/Verify-Phase4Evidence.ps1` reproduces these counts. Release build has zero warnings/errors.

The older `delivery-tests` run took six hours during a host pause and failed two new cases on correctly expired fences. No assertions were changed for that interruption. The final rerun passes unchanged. Earlier intermediate TRX directories are retained as history and are not the final gate.

Delivered: strict byte framing; Windows handle identity/read-only pump; bounded reconciliation/backlog; rename/truncate/explicit copy-truncate lineage; pending command and shared engine transaction; contiguous checkpoint and COMMIT fencing; source configuration/enablement; operational availability; producer proofs and existing AdvanceTime dispatcher; generic/Windows service host; manifest validation and diagnose CLI; real child crash/replay/race, old-schema upgrade, dump/restore with retained-file continuation and simulator parity.

Schema: additive `20260924044645_FileAcquisition`. Fresh and upgrade SQL, unchanged original migration hashes, accepted-source audit, EF model check, traces/parity/restore/worker/environment evidence are under `artifacts/phase4`. Migration files 001/002 and accepted business assertions are unchanged. Narrow migration-chain expectation is three exact IDs; historical v1-to-v2 test setup explicitly targets migration 002 with every original assertion intact. `source-integrity.json` verifies reverse-edit hashes.

Remaining Phase 4 environmental gates: no controlled SMB share/dedicated identity (Get-SmbShare Access denied), and no elevated disposable host for installed SCM/service-account/reboot/blocked-IO recovery tests. Real console worker start/stop/restart and actual local Windows files are verified. Do not label SMB or installed service tests passed. Follow the documented procedure only when an appropriate authorized host/share exists. Real GiroSol pilot is separately approved, still not authorized.

Known limitations are explicit in the review: provider identity/retention assumptions, undetectable overwrite histories, synchronous SMB opens, no unsafe source-framing live changes, sticky quarantine/invalid-proof recovery requires operator review, fixed source date context is not daily rollover, and accepted full-history engine reconstruction still needs capacity measurement. No automatic purge/session reset/skip-to-EOF.

Stop for review. Do not begin Phase 5 or claim full Phase 4 capability completion while environmental gates remain open.

Latest user direction: Phase 4 is provisionally accepted; all 309 tests are future regression gates. Phase 5 documentation-only proposal is prepared at docs/architecture/phase-5-scope.md (with security/API/acceptance companions). No implementation approval. All five environmental gates remain pending; no remote collectors. Canva frontend belongs to Phase 6.

