---
name: user-preferences
description: How this user collaborates - narration style, testing cadence, decision-making approach, distribution preferences
metadata:
  type: feedback
---

**Narration style:** "give me a 1 sentence of what you are doing" before each action when running multiple tool calls. The user explicitly preferred Option 2 from the visibility-vs-speed prompt: silent work with one-sentence preface per step, brief milestone summaries.

**Why:** When PowerShell commands fly by without context, the user can't tell what's happening or whether to redirect. Single-sentence narration lets them stay engaged without slowing things down.

**How to apply:** Before any meaningful tool call (build, Cecil dump, multi-file edit), write one sentence saying what's about to happen. After a milestone (build succeeds, feature lands, agent returns), 1-2 sentences summarizing. No long status reports unless asked.

---

**Testing cadence:** the user wants to test in-game between major changes — they have a dual-CS-install setup ([[testing_setup]]) and run two-PC validation themselves. After each new patched action lands and builds clean, hand off to them with explicit "what to do, what log lines to look for, what visual outcome to expect."

**Don't claim "should work" without functional validation.** Type checking and `dotnet build` say the code COMPILES; only in-game play proves the network sync round-trips correctly.

---

**Decision style:** asks open-ended "what should we do next" questions, then picks from 2-4 options. Lay out trade-offs briefly with a recommended option marked. They've consistently picked the recommended option but want to see alternatives.

---

**Plan-first for big work:** for non-trivial implementations, use plan mode (`EnterPlanMode` → write plan to file → `ExitPlanMode`). The user approved the original implementation plan in plan mode (`mutable-chasing-falcon.md`) and explicitly said "lets make a plan for it first." Don't skip planning for new substantial features.

---

**Distribution intent:** "decide later" — for now we ship locally to `Files\Mods\IMTSync\`. License is GPL-3 placeholder (matches CSM upstream). No GitHub publishing, no Steam Workshop yet. Don't push for these decisions — they'll choose when ready.

---

**Username on the multiplayer side:** `dune` — visible in CSM connection screens and `client-config.json`. Not relevant to code, but useful context when reading screenshots.
