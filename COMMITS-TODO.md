# Commits to make

Pascal commits once per milestone. Plan checkpoints are logged here as a record.

## Checkpoint log

- [x] Task 1 checkpoint: `chore: scaffold v18 line, demo site and CI` (both lines, via the `UmbracoMajor` switch)

## Paused here (2026-11-05)

Milestone: M0 done (Task 1 only, as agreed: the plan defines no spikes or gates). `./scripts/test.sh` passes on lines 17 and 18 (unit 3/3, integration 2/2 each). The M0 qa-reviewer found no Critical. Its Important and cheap Minor items are fixed or recorded in `docs/spikes.md` and `HANDOVER.md`.
Next task: **Task 2** (options, validation and the composer). Task 1's "first CI run with Pascal" is deferred to Task 9. CI has not run.
Note: plan.md has no GOV.UK Notify task yet (only the scope-change note). Add one before Task 11.

## Milestone commit

Check `git status` first: only the scaffold should be listed (no secrets, no `artifacts/`, no user-secrets, no review docs).

```bash
git add -A
git commit -m "chore: scaffold email sender package for Umbraco 17 and 18

One codebase builds both lines through the UmbracoMajor switch (separate lock files,
version prefix and UMBRACO_17 define). Adds the template's CI (four-leg PR build,
gitleaks, osv-scanner, check-vulnerable, manual release checks, trusted-publishing
workflow), a demo site, unit and integration test projects, and the design, plan and
spikes docs."
git log -1 --stat | head -5
```
