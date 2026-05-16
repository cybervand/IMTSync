---
name: build-and-deploy
description: Single-command build + dual-install auto-deploy, target framework, and reference resolution paths
metadata:
  type: reference
---

# Build & deploy

**Build:**
```powershell
cd M:\develop\IMT-MP
dotnet build CSM.IMTSync.csproj -c Release
```

**Target framework:** `net35` (CS uses Mono 2.x with .NET 3.5 profile — see [[runtime_gotchas]]).
We use NuGet `Microsoft.NETFramework.ReferenceAssemblies.Net35` so the .NET 3.5 reference
assemblies don't need to be installed system-wide.

**Auto-deploy:** the csproj's `DeployToCS` MSBuild target copies `bin\Release\CSM.IMTSync.dll` to
**both** of the user's CS installs after every build:
- `M:\Games\Cities Skylines\Files\Mods\IMTSync\` (host)
- `M:\Games\C_S2\Cities Skylines\Files\Mods\IMTSync\` (client)

Restart CS to pick up the new DLL.

**External references** (all `<Private>False</Private>` — game ships them):
- `CSM.API.dll`, `protobuf-net.dll` from `D:\SteamLibrary\steamapps\workshop\content\255710\1558438291\`
- `CitiesHarmony.API.dll` from `D:\SteamLibrary\steamapps\workshop\content\255710\2140418403\`
  (each mod ships its own API stub; we use IMT's copy)
- `CitiesHarmony.Harmony.dll` from `M:\Games\Cities Skylines\Files\Mods\2040656402\`
- `IntersectionMarkingTool.dll`, `IntersectionMarkingTool.API.dll` from
  `D:\SteamLibrary\steamapps\workshop\content\255710\2140418403\`
- `Assembly-CSharp.dll`, `ColossalManaged.dll`, `ICities.dll`, `UnityEngine.dll` from
  `M:\Games\Cities Skylines\Cities_Data\Managed\`

**How to apply:** Never bump target framework. Never add NuGet packages that bundle .NET 4.0+
mscorlib types. If a new reference is needed, prefer DLLs already shipping in the user's
mods folder so we don't ship duplicates.
