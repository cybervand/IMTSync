# MEMORY index

- [Project overview](project_overview.md) — what IMT-MP is, why it exists, the underlying motivation
- [Build & deploy](build_and_deploy.md) — single command, dual-install auto-deploy
- [Runtime gotchas](runtime_gotchas.md) — Mono 2.x APIs that compile but throw at runtime
- [Two-PC testing setup](testing_setup.md) — host vs client install paths, in-game overlay hotkey
- [IMT internals notes](imt_internals.md) — quick reference; **canonical doc is now `docs/IMT-INTERNALS.md`** (6-agent survey synthesis)
- [CSM API notes](csm_api_notes.md) — Connection contract, send/receive APIs, IgnoreHelper, ToolSimulatorCursorManager
- [Cecil inspection pattern](cecil_pattern.md) — copy/paste-able PowerShell harness for IMT/CSM DLL inspection
- [User collaboration preferences](user_preferences.md) — narration style, testing cadence, decision style
- [Agents in sandbox — what works, what doesn't](agents_blocked_in_sandbox.md) — shell blocked, WebFetch/Read/Grep works
- [Verify after every edit](verify_after_edit.md) — run lint/compile and self-fix before reporting done

External docs (in repo, not in memory):

- `docs/IMT-INTERNALS.md` — consolidated IMT architecture reference (folder/namespace map, patch targets, style hierarchy, persistence)
- `docs/TODO.md` — unsynced-action backlog with source-confirmed exact patch targets
