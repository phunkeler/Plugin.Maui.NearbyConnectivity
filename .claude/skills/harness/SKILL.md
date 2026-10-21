---
name: harness
description: Make a governed change with the harness CLI (plan, start, gate, commit). Use whenever editing code in this repository, when a task names a checkpoint, plan, gate or work item, or when a harness command printed an error.
---

# Harness checkpoint loop

The rules are in `AGENTS.md` at the repository root. This is the procedure.

1. Run `harness status`. If there is no plan on this branch, run `harness plan <workflow> --target "<what>" --reference <kind:locator>` and ask a human to run `harness approve plan`.
2. Run `harness start <checkpoint> --evidence <class>`. The class is `logic` for code only, `static-visual` when a screen looks different, `temporal` for animation or timing, `mixed` for more than one.
3. Make the smallest change that completes the checkpoint title. A bug fix needs a reproduction at `.harness/repro/<plan>/<checkpoint>.sh` that fails before the fix and passes after.
4. Run `harness gate`. Fix each reported failure and run it again until it prints "awaiting approval". Logs are under `.harness/evidence/<plan>/<checkpoint>/attempt-<n>/`.
5. Run `harness commit -m "<one line>"`. If it refuses for lack of approval, ask a human to run `harness approve <checkpoint>`, then commit again.

Every harness error ends with `Try: <command>`; run that command. "blocked" means stop: a human must resume.
