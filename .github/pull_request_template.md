<!--
Thanks for the PR. A quick summary + how you verified it goes a long way.
See CONTRIBUTING.md and RELEASING.md for the full workflow.
-->

## Summary
<!-- 1–3 sentences: what changes and why? -->

## Changes
<!-- Optional bullet list if the summary doesn't cover details -->

## Test plan
- [ ] `dotnet build NexusSyncServer.sln -c Release` is green
- [ ] `dotnet test NexusSyncServer.sln -c Release` is green
- [ ] (if it touches request handling or storage) `docker compose up` and the change exercised end to end

## Impact checklist
<!-- A server holds other people's data and issues credentials, so a few things deserve a
     deliberate answer rather than a glance. -->
- [ ] **Wire protocol** — unchanged. Anything altering endpoints, envelopes or the contract model belongs in `NexusKit.Sync` first and affects every client, including ones we did not write.
- [ ] **Validation** — no path made more permissive. A contract constraint that stops being enforced is a security change, not a convenience change.
- [ ] **Auth** — no endpoint lost its scope check; no key material reaches a log, a response body, or an error message.
- [ ] **Migration** — any schema change is additive, or the destructive step is called out below.

## Notes for reviewer
<!-- Optional: anything that needs special attention -->

## Operator impact
<!-- Does an existing deployment need action on upgrade — a config value, an env var, a manual
     migration, a restart order? Write "none" if not. -->
