---
name: cecil-pattern
description: Copy-pasteable PowerShell harness for Mono.Cecil inspection of IMT/CSM DLLs (no .NET 4.x reflection issues, works inside CS-pinned assembly versions)
metadata:
  type: reference
---

**Why Cecil instead of `Reflection.Assembly.LoadFrom`:** the .NET reflection LoadFrom path
needs to resolve all assembly dependencies (UnityEngine, ColossalManaged, etc.). It also fails
on Windows-1252-encoded blocked files from network paths (HRESULT 0x80131515). Cecil reads
metadata directly without resolving deps — much more reliable.

**One-time setup:**
```powershell
$tmp = "$env:TEMP\imt-sync-analysis"
if (-not (Test-Path $tmp)) { New-Item -ItemType Directory $tmp | Out-Null }
if (-not (Test-Path "$tmp\Mono.Cecil.dll")) {
  Copy-Item "M:\Games\Cities Skylines\Files\Mods\2881031511\Mono.Cecil.dll" "$tmp\Mono.Cecil.dll"
  Unblock-File "$tmp\Mono.Cecil.dll"
}
Add-Type -Path "$tmp\Mono.Cecil.dll"
```

**Inspecting IMT:**
```powershell
$module = [Mono.Cecil.ModuleDefinition]::ReadModule("M:\Games\Cities Skylines\Files\Mods\2140418403\IntersectionMarkingTool.dll")
# or for the API stub:
$apiModule = [Mono.Cecil.ModuleDefinition]::ReadModule("M:\Games\Cities Skylines\Files\Mods\2140418403\IntersectionMarkingTool.API.dll")
```

**Inspecting CSM:**
```powershell
$csmApi = [Mono.Cecil.ModuleDefinition]::ReadModule("D:\SteamLibrary\steamapps\workshop\content\255710\1558438291\CSM.API.dll")
$csm    = [Mono.Cecil.ModuleDefinition]::ReadModule("D:\SteamLibrary\steamapps\workshop\content\255710\1558438291\CSM.dll")
# Files\Mods copies are identical (same SHA256)
```

**Inspecting other extensions for patterns:**
```powershell
# MoveItSync - cleanest reference; closest analogue to what we're building
$moveItSync = [Mono.Cecil.ModuleDefinition]::ReadModule("M:\Games\Cities Skylines\Files\Mods\3693412367\CSM.MoveItSync.dll")
# May need Unblock-File first:
# Copy-Item "M:\Games\Cities Skylines\Files\Mods\3693412367\CSM.MoveItSync.dll" "$tmp\"; Unblock-File "$tmp\CSM.MoveItSync.dll"
```

**Common queries:**
```powershell
# All public methods of a type
$t = $module.Types | Where-Object { $_.Name -eq 'Marking' } | Select-Object -First 1
$t.Methods | Where-Object { $_.IsPublic -and -not $_.IsConstructor } | ForEach-Object {
  $params = ($_.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
  "$($_.ReturnType.Name) $($_.Name)($params)"
}

# Find all overloads of a method (detects ambiguity for Harmony patches)
$t.Methods | Where-Object { $_.Name -eq 'RemoveLine' } | ForEach-Object { ... }

# Find all callers of a method (IL trace - useful for "what tool mode invokes this?")
foreach ($t in $module.Types) {
  foreach ($meth in $t.Methods) {
    if (-not $meth.HasBody) { continue }
    foreach ($ins in $meth.Body.Instructions) {
      if ($ins.OpCode.Code -in @([Mono.Cecil.Cil.Code]::Call, [Mono.Cecil.Cil.Code]::Callvirt)) {
        $op = "$($ins.Operand)"
        if ($op -match 'Marking::AddRegularLine') {
          "  $($t.FullName).$($meth.Name)  -> $op"
        }
      }
    }
  }
}

# Concrete implementors of an interface
$module.Types | Where-Object {
  $_.Interfaces | Where-Object { $_.InterfaceType.Name -eq 'IFillerVertex' }
} | ForEach-Object { $_.FullName }
```

**Cecil quirks worth knowing:**
- `t.Interfaces` shows ONLY directly-declared interfaces, not inherited. To see inherited ones,
  walk the BaseType chain.
- `param.ParameterType.IsByReference` and `param.IsOut` distinguish `ref` vs `out` (Cecil shows
  both as `&` in the type name)
- For generic types, `BaseType.FullName` includes the generic instantiation in IL syntax —
  e.g., `IMT.Manager.MarkingManager``1<IMT.Manager.NodeMarking>`
- Don't pre-load the original mods directory's DLL twice — Cecil locks the file. Always copy
  to `$tmp` first.
