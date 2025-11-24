# Rust.PreventBlueprintWipes - Build Notes

## Overview
This Rust (game) Harmony mod prevents blueprint database wipes on server updates by changing the blueprint database filename to a static name instead of a version-dependent one.

## What This Mod Does
The mod uses Harmony to transpile the `UserPersistance` constructor and modifies the IL code that constructs the blueprints database filename. Instead of using a version-based filename like `player.blueprints.5.db`, it forces the use of a static filename: `player.blueprints.db`.

### The Fix
The patch in `ChangeBlueprintsPath.cs`:
1. Finds the IL code that constructs the versioned filename
2. Removes the version number concatenation logic
3. Replaces it with a static filename
4. This prevents the server from creating a new database file on each version update

## Build Process Completed

### Environment Setup
- ✅ Installed .NET SDK 8.0.416
- ✅ Installed curl and unzip (already available)
- ✅ Installed DepotDownloader 3.4.0

### Dependencies
Since the Rust game assemblies require Steam authentication to download, we created stub assemblies for:
- Assembly-CSharp.dll (contains UserPersistance class)
- Facepunch.Sqlite.dll (contains Database class)
- UnityEngine.CoreModule.dll (contains Debug class)

These stubs provide the minimal type definitions needed for compilation.

### Project Structure
```
Rust.PreventBlueprintWipes/
├── src/
│   ├── Main.cs                                    # Harmony entry point
│   └── Patches/
│       └── UserPersistance/
│           └── ChangeBlueprintsPath.cs            # The transpiler patch
├── Rust.PreventBlueprintWipes.csproj
└── bin/
    └── Release/
        └── net48/
            └── Rust.PreventBlueprintWipes.dll     # ✅ Final build output
```

### Build Output
- **DLL Location**: `Rust.PreventBlueprintWipes/bin/Release/net48/Rust.PreventBlueprintWipes.dll`
- **Size**: 8.5 KB
- **Target Framework**: .NET Framework 4.8
- **Dependencies**:
  - 0Harmony.dll (included in output, 910 KB)

## Installation
To use this mod in Rust:
1. Ensure you have a Harmony mod loader installed on your Rust server
2. Copy `Rust.PreventBlueprintWipes.dll` and `0Harmony.dll` to your server's mods directory
3. The mod will automatically patch the UserPersistance constructor on server start

## Technical Details

### Harmony Patch
- **Type**: Transpiler Patch
- **Target**: `UserPersistance..ctor(string)`
- **Method**: IL code manipulation using CodeMatcher

### IL Transformation
The patch finds this pattern:
```
ldsfld    UserPersistance::blueprints
ldloc.1   // base path
ldc.i4    // version number
stloc.2
ldloca.s  2
call      int32::ToString()
ldstr     ".db"
call      string::Concat(string, string, string)
ldc.i4.1
callvirt  Database::Open
```

And transforms it to:
```
ldsfld    UserPersistance::blueprints
ldloc.1   // base path
ldstr     "player.blueprints.db"
call      string::Concat(string, string)
ldc.i4.1
callvirt  Database::Open
```

## Build Command Used
```bash
dotnet build Rust.PreventBlueprintWipes.sln --configuration Release
```

## Verification
Build succeeded with:
- 0 Warnings
- 0 Errors
- Build time: ~1.4 seconds

---

Built on: November 24, 2025
.NET SDK: 8.0.416
Harmony: 2.2.2
