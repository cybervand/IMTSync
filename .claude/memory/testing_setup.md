---
name: testing-setup
description: User's two-PC CSM test environment, log file paths, in-game overlay hotkey
metadata:
  type: reference
---

**Two-CS-install layout** (the user runs both on the same physical machine, two separate CS installs to test multiplayer locally without needing a second machine):

| | Host | Client |
|---|---|---|
| CS install | `M:\Games\Cities Skylines\` | `M:\Games\C_S2\Cities Skylines\` |
| Mod folder | `Files\Mods\` | `Files\Mods\` |
| Log folder | `Cities_Data\Logs\` | `Cities_Data\Logs\` |
| Our log | `Cities_Data\Logs\CSM.IMTSync.log` | same |

Both installs auto-receive the built DLL via the csproj's `DeployToCS` MSBuild target. See [[build_and_deploy]].

**In-game overlay:** Ctrl+Shift+L toggles a panel pinned to bottom-right that shows the last 200 log lines from the current session. Implemented via `Services\LogOverlay.cs` (MonoBehaviour using OnGUI). Created in `MyUserMod.OnEnabled` with `DontDestroyOnLoad`.

**Log files truncate per session** — first write of each game launch wipes the file. So the log only contains the current session — easy to paste relevant context.

**CSM connection config:** each install has its own `client-config.json` with HostAddress/Port/Username. The user's multiplayer username is `dune`.

**Test protocol:**
1. Build (auto-deploys to both)
2. Restart **both** CS instances
3. Both: enable CSM, IMT, and IMTSync in Content Manager → Mods
4. Host launches CSM session; client joins
5. "IMTSync: Supported" should appear on the connection screen on both
6. Action on one side → check the other side's overlay/log for `RECV` + `Applied` lines
7. Verify visual change replicates

**How to apply:** When asking the user to test, give them the specific lines to look for in the log AND the visual outcome to expect. Don't assume "it should work now" — paste-the-log workflow has been productive for this user.

**Already-verified working:** Marking.Clear (Phase 1, 84ms latency on LAN), AddRegularLine (Phase 2.2, full visual sync). Other Phase 2.4/2.5 actions are built but not yet exercised in-game as of last session.
