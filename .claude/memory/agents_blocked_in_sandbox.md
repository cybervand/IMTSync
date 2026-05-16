---
name: agents-blocked-in-sandbox
description: Background subagents can't run shell (PowerShell/Bash/dotnet) but CAN use WebFetch/Read/Grep/Glob. Use them for source-level surveys via GitHub raw URLs; keep Cecil and builds in foreground.
metadata:
  type: feedback
---

# Agents in sandbox — what works, what doesn't

**Refined rule (2026-05-16):** The earlier "BG agents can't do anything" framing was too broad.
Background agents in this user's harness:

- **CAN do:** `WebFetch`, `Read`, `Grep`, `Glob`. They WORK for source-level research via GitHub
  raw URLs, file analysis, code reviews, structured cataloging from web-fetched docs.
- **CAN'T do:** shell commands. No `dotnet build`, no PowerShell, no Bash, no Cecil DLL inspection.
  Worktree isolation also unavailable (harness CWD is outside the repo).

**Proof of CAN-DO:** A 6-agent parallel survey of [MacSergey/NodeMarkup](https://github.com/MacSergey/NodeMarkup) on 2026-05-16 ran to completion in ~3-5 minutes each, returning structured catalogs covering IMT.API, IMT/Manager, IMT/MarkingItems, IMT/Tools, IMT/UI, and IMT/Utilities+root. Synthesis written to `docs/IMT-INTERNALS.md`.

**Why the original error happened:** An earlier attempt asked agents to "verify the build" or run Cecil — both required shell. The agents reported sandbox blockage and consumed 40-90k tokens producing only "I'm blocked" responses. The fix is **task scoping**, not avoiding agents.

**How to apply:**

- For Cecil DLL inspection, `dotnet build`, or any shell call → **do in foreground**.
- For source-level investigation from GitHub raw files, structured cataloging, code analysis → **delegate to parallel BG agents**. Self-contained scope, clear output format, ~400-600 line output cap per agent.
- For pure file-writing where inputs are pre-extracted (e.g., "write these 5 files with this exact content") → BG agent works.
- Always rebuild yourself in foreground after merging BG agent output — they can't verify the build.

**Worktree isolation:** still blocked. `Agent` tool with `isolation: "worktree"` requires the harness CWD to be inside a git repo. The harness starts in `c:\Users\Brewing Storm Lite\AppData\Local\...\255710` (CS assets folder), NOT `M:\develop\IMT-MP`. Workaround: have each parallel agent write to NEW files only (no overlap).

**Briefing pattern that works (from 2026-05-16 survey):**

- Include project context (what IMT-MP is, why we're surveying)
- Include what we've already mapped (so agents don't duplicate)
- Give a specific folder/URL scope
- Specify desired output format (structured catalog with sections)
- Cap output length (~400-600 lines)
- Tell them not to spawn sub-agents
- Tell them to be honest about uncertainty
