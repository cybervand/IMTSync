---
name: agents-blocked-in-sandbox
description: Background subagents in this user's harness are sandboxed to read-only - cannot run PowerShell, Bash, or dotnet build. Don't delegate shell-requiring work to them.
metadata:
  type: feedback
---

**Rule:** Background agents (Plan agent AND general-purpose agent) launched in this user's environment **cannot execute shell commands**. Any task requiring `dotnet build`, PowerShell, Bash, Cecil reflection, or other shell-mediated work must be done in foreground.

**Why:** Both BG agents we spawned in the parallel-work attempt failed identically — they reported "I cannot run PowerShell or Bash commands in this READ-ONLY mode (sandboxed)." The Plan agent additionally couldn't write files. Each consumed 40-90k tokens producing only "I'm blocked, what should I do?" responses.

**How to apply:**
- For research that needs Cecil DLL inspection, build verification, or any shell call → do it in foreground.
- BG agents are still useful for **pure file-writing work** where all inputs are pre-extracted and given to them in the prompt (e.g., "write these 5 files with this exact content"). But they can't verify the build, so always rebuild yourself after merging their output.
- Better plan-mode pattern: I do shell-requiring research in foreground first → produce a self-contained "implementation packet" with Cecil-verified signatures → THEN delegate the file writing to a BG agent.

**Worktree isolation also blocked:** `Agent` tool with `isolation: "worktree"` requires the harness CWD to be inside a git repo. The harness in this user's env starts in the CS assets folder (`c:\Users\Brewing Storm Lite\AppData\Local\...\255710`), NOT in `M:\develop\IMT-MP`. So worktree isolation cannot be used. Without it, parallel BG agents writing to overlapping files would conflict — must use file isolation (each agent writes to NEW files only).

**Practical tactic:** for medium tasks, sequential foreground is faster than parallel + merge-coordination + verify. Parallel BG agents are only worth the overhead if the agent task is truly self-contained AND large.
