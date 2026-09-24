# Phase 6 frontend development

Phase 6 is in progress; this is a local development/test procedure, not deployment approval. The approved visual mapping is Canva Page 1 Home, Page 2 Monitoring, Page 3 Search. Inter is the explicitly approved standalone webfont substitute. The local font includes its OFL license; no runtime font CDN is used.

## Build and verify

From `src/Sonda.Web`, use the pinned lockfile with `npm ci`, then `npm run build`, `npm test`, and `npm run test:browser`. Chrome is required for the configured browser tests. The browser suite starts a loopback Vite server and uses synthetic API fixtures; those tests do not prove persistence or authorization. Its accessibility audit records actual findings separately and does not assert that the design passes accessibility.

The existing ASP.NET Core host accepts the operator-only `Frontend:Root` setting pointing to the built `src/Sonda.Web/dist` absolute directory. It serves static assets and explicit client routes on the API origin. This is optional; it does not change the acquisition worker or introduce a second production service. API requests continue to use secure cookies, CSRF protection, authenticated team scope and the accepted backend contracts. Do not serve real account sessions over the Vite HTTP development server.

Build the frontend before running `Sonda.Api.Tests.FrontendBrowserTests`. That test starts a disposable real PostgreSQL database and the existing server on an ephemeral loopback HTTPS port. Its short-lived certificate is used only in that browser context, which explicitly ignores certificate trust errors. No certificate is installed or production TLS setting weakened. Test credentials are passed privately to the child process and are not written to browser artifacts.

Run the existing .NET solution test command using the local development database configuration, then `tools/Verify-Phase6Regression.ps1 -ResultsDirectory <fresh TRX directory>`. Preserve all 382 accepted tests and the 139 protected file hashes. Do not regenerate accepted baselines to make a changed contract pass.

## Review artifacts

- `artifacts/phase6/visual-comparison.html`: unchanged official Canva exports beside Inter captures, plus adjustable overlays for all three pages.
- `artifacts/phase6/real-browser-results.json`: real HTTPS/PostgreSQL workflow checks.
- `artifacts/phase6/accessibility-findings.json`: actual automated findings, including unresolved color contrast.
- `artifacts/phase6/contrast-review.html`: proposed text-only contrast adjustment; not applied without explicit approval.
- `docs/phase-6-progress.md`: verified results and remaining acceptance work.

No local browser result closes the five pending Phase 4 environmental gates. No production deployment, real GiroSol data, notifications, remote collectors or Phase 7 work is authorized.
