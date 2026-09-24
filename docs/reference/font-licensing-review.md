# Canva Sans standalone webfont review

Reviewed 2026-09-24. The live editor identifies Canva Sans. The preserved design metadata and supplied image do not provide a standalone webfont or a separate embedding license.

Canva's current [Content License Agreement, section 9A](https://www.canva.com/en_in/policies/content-license-agreement/) restricts font software use to Canva and integrated components of exported Canva designs. That does not establish permission to extract the font and serve it as an independently hosted dynamic SONDA webfont. No authorized standalone distribution/license was located. A separately obtained explicit license could change this conclusion; this is not a claim that such a license can never exist.

Do not extract font binaries from Canva, PDF subsets or unofficial font download sites. No Canva font was downloaded or installed. Preserve the approved typography reference; production substitution is blocked pending user approval.

## Review candidate only

Inter Regular is the proposed close available candidate, not an exact glyph/metrics match and not asserted to be the closest font in existence. Its official [project](https://rsms.me/inter/) distributes webfonts under SIL Open Font License 1.1. The comparison uses the official InterVariable.woff2 and includes its license; it does not change application styles.

[Side-by-side comparison](../../artifacts/phase6/font-review/comparison.png) / [HTML](../../artifacts/phase6/font-review/comparison.html). Left: original approved Home raster at native scale; right: Inter at the measured 26.6664px, regular weight and 37px line height. Width and letterforms visibly differ. Await approval before integrating any substitute. Greeting position/container geometry remain the Canva baseline.

## Approved Inter substitution — 2026-09-24

The user approved locally hosted Inter as SONDA's legal/technical webfont substitute. Canva remains visual authority. Tune font size, weight, line height, letter spacing and text placement within the approved geometry. Do not resize or redesign components merely to accommodate glyph widths. This approval supersedes earlier pending-font statements. Inter's official webfont and OFL license are preserved under src/Sonda.Web/public/fonts. No font request to a third-party CDN is needed at runtime.

The user also approved a limited foreground-only contrast adjustment for small Warning/Error/muted text. Exact values are recorded in docs/phase-6-visual-review.md; this does not alter the Inter typography or Canva component geometry.
