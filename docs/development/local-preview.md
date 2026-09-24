# Interactive local preview

`tools/Sonda.LocalPreview` is a developer/review harness, not a production host and not part of the accepted regression solution. It reuses the existing HTTPS server factory and disposable PostgreSQL fixture without modifying any tests or application security behavior.

Build the frontend and the preview project, start the existing local development PostgreSQL cluster, then run `tools/Start-LocalPreview.ps1` from PowerShell. The helper uses the ignored local database credential file; credentials are not printed or committed. A visible separate Chrome window opens and signs in to a temporary sample Admin account. The host binds only to loopback on a random HTTPS port. Certificate trust bypass is confined to that preview browser context; no certificate is installed and production TLS is not changed.

The preview contains CAM, UNITELLER, DIGICEL and TERRAPAY synthetic fixtures, including successful, failed and undefined orders, shared evidence and unmatched evidence. The greeting comes from the sample account, not a frontend constant. The sample Profile rules are ordinary engine configuration. Monitoring worker execution and all sample sources are disabled, so availability qualifiers are expected; this does not establish real source health.

Try Home Info, Incident tabs/status actions, Monitoring rows/fullscreen, Search result/evidence/run dialogs and structured Configuration. Changes are real transactions in the separate disposable database. Closing all pages of the preview Chrome window stops the preview and drops that database. The shared local PostgreSQL development process remains running. To reset the sample state, close the preview and launch it again. After signing out or an idle session expires, restart the preview for a new automatically signed-in session; random credentials are kept only in memory.

The ready URL/process ID is in `.tools/local-preview-status.json`; diagnostic logs are in `.tools/local-preview.*.log`. A force-killed host cannot run cleanup, so its uniquely named disposable database may require subsequent operator cleanup. Normal window closure is the intended stop path.

This preview does not change visual approval, freeze screenshot baselines, close Phase 4 environmental gates or authorize deployment. Do not point this test harness at production data.
