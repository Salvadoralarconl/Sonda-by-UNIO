# User direction recorded 2026-09-24

Phase 4 implementation is accepted provisionally, with final capability sign-off pending explicitly blocked environmental verification. Preserve all 309 passing tests and all 237 accepted regression tests. Do not modify core acquisition architecture, checkpoint model, interpretation engine, persistence model or Canva visual reference.

The original plan remains: shared SONDA server application; monitoring worker directly reads configured local/UNC sources; PostgreSQL is durable truth; the existing engine is authoritative. No remote collector architecture now.

Keep these Phase 4 gates explicitly pending: controlled SMB/UNC under a dedicated identity; disconnect/reconnect/share identity; installed Windows Service/SCM lifecycle; boot/recovery; dedicated service-account ACL verification. Do not fake or replace them with mocks.

Prepare a bounded Phase 5 proposal for the shared backend/API, team accounts, Admin/Member roles, authentication/authorization, dashboard/query endpoints, Profile management, Incident workflow, Search API, monitoring diagnostics and secure exposure of existing SONDA data/engine. **Do not begin Phase 5 implementation.**

No Canva frontend in Phase 5. @Canva → Sonda Home Page Design remains the Phase 6 visual authority. Stop after presenting the proposal for approval.

This is a concise record of the latest instruction, not approval of any policy newly proposed in the Phase 5 documents.
