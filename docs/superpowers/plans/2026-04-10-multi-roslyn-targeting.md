# Multi-Roslyn Targeting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship three analyzer variants (Roslyn 4.8, 4.12, 5.0) in the ForgeMap NuGet package so consumers on .NET 8, 9, and 10 SDKs each get a compatible source generator.

**Architecture:** The generator project (`netstandard2.0`) is built three times with different `Microsoft.CodeAnalysis.CSharp` versions, controlled by a `ROSLYN_VERSION` MSBuild property. Per-version `.props` files set the Roslyn dependency. A pack script stages all three outputs, and the packable `ForgeMap.csproj` bundles them under `analyzers/roslyn{VERSION}/dotnet/cs/`. The .NET SDK's native `CompilerApiVersion` resolution picks the right variant automatically.

**Tech Stack:** MSBuild, NuGet packaging, bash/PowerShell build scripts

**Spec:** `docs/superpowers/specs/2026-04-10-multi-roslyn-targeting-design.md`

---

### Task 1: Create per-version Roslyn props files

**Files:**
- Create: `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.8.props`
- Create: `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.12.props`
- Create: `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn5.0.props`

- [ ] **Step 1: Create ForgeMap.Generator.Roslyn4.8.props**

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="4.8.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create ForgeMap.Generator.Roslyn4.12.props**

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="4.12.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create ForgeMap.Generator.Roslyn5.0.props**

```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="5.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Commit**

```bash
git add src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.8.props \
        src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.12.props \
        src/ForgeMap.Generator/ForgeMap.Generator.Roslyn5.0.props
git commit -m "feat: add per-Roslyn-version props files for multi-targeting"
```

---

### Task 2: Modify ForgeMap.Generator.csproj to use versioned props

**Files:**
- Modify: `src/ForgeMap.Generator/ForgeMap.Generator.csproj`

The current `.csproj` has this `PackageReference` (line 25):

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
```

We need to: (1) add a `ROSLYN_VERSION` property defaulting to `5.0`, (2) import the version-specific `.props` file, and (3) remove the direct `PackageReference` for `Microsoft.CodeAnalysis.CSharp`.

- [ ] **Step 1: Add ROSLYN_VERSION property to the first PropertyGroup**

In `src/ForgeMap.Generator/ForgeMap.Generator.csproj`, add a new `PropertyGroup` right after the opening `<Project>` tag and before the existing `PropertyGroup`:

```xml
<PropertyGroup>
  <ROSLYN_VERSION Condition="'$(ROSLYN_VERSION)' == ''">5.0</ROSLYN_VERSION>
</PropertyGroup>
```

- [ ] **Step 2: Add Import for version-specific props**

Add this line after the new `PropertyGroup` from step 1, before the existing `PropertyGroup`:

```xml
<Import Project="ForgeMap.Generator.Roslyn$(ROSLYN_VERSION).props" />
```

- [ ] **Step 3: Remove the direct Microsoft.CodeAnalysis.CSharp PackageReference**

Remove this line from the `<ItemGroup>` on line 25:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
```

The `Microsoft.CodeAnalysis.Analyzers` reference stays — it's version-independent.

- [ ] **Step 4: Verify the generator builds with default (5.0) Roslyn**

Run:
```bash
dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj -c Release
```
Expected: Build succeeds with Roslyn 5.0.0.

- [ ] **Step 5: Verify the generator builds with each Roslyn version**

Run:
```bash
dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj -c Release /p:ROSLYN_VERSION=4.8
dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj -c Release /p:ROSLYN_VERSION=4.12
dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj -c Release /p:ROSLYN_VERSION=5.0
```
Expected: All three builds succeed. Each produces `ForgeMap.Generator.dll` under `bin/Release/netstandard2.0/`.

- [ ] **Step 6: Commit**

```bash
git add src/ForgeMap.Generator/ForgeMap.Generator.csproj
git commit -m "feat: wire ROSLYN_VERSION property and import versioned props in generator"
```

---

### Task 3: Modify ForgeMap.csproj packaging target for multi-Roslyn layout

**Files:**
- Modify: `src/ForgeMap/ForgeMap.csproj`

The current `IncludePackageContent` target (lines 40-46) copies a single generator DLL from a hardcoded path. Replace it with one that reads from `$(GeneratorArtifactsDir)` and includes all 3 Roslyn variants.

- [ ] **Step 1: Replace the IncludePackageContent target**

Replace the existing target (lines 40-46):

```xml
  <Target Name="IncludePackageContent">
    <ItemGroup>
      <TfmSpecificPackageFile Include="$(OutDir)ForgeMap.Abstractions.dll" PackagePath="lib/netstandard2.0" />
      <TfmSpecificPackageFile Include="$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '..', 'ForgeMap.Generator', 'bin', '$(Configuration)', 'netstandard2.0', 'ForgeMap.Generator.dll'))"
                              PackagePath="analyzers/dotnet/cs" />
    </ItemGroup>
  </Target>
```

With:

```xml
  <Target Name="IncludePackageContent">
    <ItemGroup>
      <TfmSpecificPackageFile Include="$(OutDir)ForgeMap.Abstractions.dll" PackagePath="lib/netstandard2.0" />

      <!-- Roslyn 4.8 (for .NET 8 SDK) -->
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn4.8/ForgeMap.Generator.dll"
                              PackagePath="analyzers/roslyn4.8/dotnet/cs" />
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn4.8/ForgeMap.Abstractions.dll"
                              PackagePath="analyzers/roslyn4.8/dotnet/cs" />

      <!-- Roslyn 4.12 (for .NET 9 SDK) -->
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn4.12/ForgeMap.Generator.dll"
                              PackagePath="analyzers/roslyn4.12/dotnet/cs" />
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn4.12/ForgeMap.Abstractions.dll"
                              PackagePath="analyzers/roslyn4.12/dotnet/cs" />

      <!-- Roslyn 5.0 (for .NET 10 SDK) -->
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn5.0/ForgeMap.Generator.dll"
                              PackagePath="analyzers/roslyn5.0/dotnet/cs" />
      <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)/roslyn5.0/ForgeMap.Abstractions.dll"
                              PackagePath="analyzers/roslyn5.0/dotnet/cs" />
    </ItemGroup>
  </Target>
```

Note: Use forward slashes (`/`) in paths — MSBuild normalizes these cross-platform.

- [ ] **Step 2: Commit**

```bash
git add src/ForgeMap/ForgeMap.csproj
git commit -m "feat: update IncludePackageContent for multi-Roslyn analyzer layout"
```

---

### Task 4: Create pack scripts

**Files:**
- Create: `build/pack.sh`
- Create: `build/pack.ps1`

- [ ] **Step 1: Create build/pack.sh**

```bash
#!/usr/bin/env bash
set -euo pipefail

roslyn_versions=("4.8" "4.12" "5.0")
artifacts_dir="artifacts/generator"
config="${1:-Release}"

echo "=== Multi-Roslyn Pack ==="
echo "Configuration: $config"

rm -rf "$artifacts_dir"

for version in "${roslyn_versions[@]}"; do
  echo "--- Building generator for Roslyn $version ---"
  dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj \
    -c "$config" \
    /p:ROSLYN_VERSION="$version"

  mkdir -p "$artifacts_dir/roslyn$version"
  cp "src/ForgeMap.Generator/bin/$config/netstandard2.0/ForgeMap.Generator.dll" \
     "$artifacts_dir/roslyn$version/"
  cp "src/ForgeMap.Generator/bin/$config/netstandard2.0/ForgeMap.Abstractions.dll" \
     "$artifacts_dir/roslyn$version/"
done

echo "--- Building ForgeMap wrapper ---"
dotnet build src/ForgeMap/ForgeMap.csproj -c "$config"

echo "--- Packing ---"
dotnet pack src/ForgeMap/ForgeMap.csproj -c "$config" --no-build \
  /p:GeneratorArtifactsDir="$(pwd)/$artifacts_dir"

echo "=== Pack complete ==="
ls -la artifacts/*.nupkg 2>/dev/null || ls -la src/ForgeMap/bin/"$config"/*.nupkg 2>/dev/null || echo "NuGet package location may vary — check bin/$config/"
```

- [ ] **Step 2: Make pack.sh executable**

Run:
```bash
chmod +x build/pack.sh
```

- [ ] **Step 3: Create build/pack.ps1**

```powershell
#Requires -Version 5.1
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$roslynVersions = @("4.8", "4.12", "5.0")
$artifactsDir = "artifacts/generator"

Write-Host "=== Multi-Roslyn Pack ==="
Write-Host "Configuration: $Configuration"

if (Test-Path $artifactsDir) {
    Remove-Item -Recurse -Force $artifactsDir
}

foreach ($version in $roslynVersions) {
    Write-Host "--- Building generator for Roslyn $version ---"
    dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj `
        -c $Configuration `
        /p:ROSLYN_VERSION="$version"
    if ($LASTEXITCODE -ne 0) { throw "Build failed for Roslyn $version" }

    $targetDir = "$artifactsDir/roslyn$version"
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item "src/ForgeMap.Generator/bin/$Configuration/netstandard2.0/ForgeMap.Generator.dll" $targetDir
    Copy-Item "src/ForgeMap.Generator/bin/$Configuration/netstandard2.0/ForgeMap.Abstractions.dll" $targetDir
}

Write-Host "--- Building ForgeMap wrapper ---"
dotnet build src/ForgeMap/ForgeMap.csproj -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "ForgeMap wrapper build failed" }

Write-Host "--- Packing ---"
$fullArtifactsDir = (Resolve-Path $artifactsDir).Path
dotnet pack src/ForgeMap/ForgeMap.csproj -c $Configuration --no-build `
    /p:GeneratorArtifactsDir="$fullArtifactsDir"
if ($LASTEXITCODE -ne 0) { throw "Pack failed" }

Write-Host "=== Pack complete ==="
```

- [ ] **Step 4: Test the pack script**

Run:
```bash
bash build/pack.sh Release
```
Expected: All three generator builds succeed, wrapper builds, pack succeeds. A `.nupkg` file is produced.

- [ ] **Step 5: Verify the NuGet package layout**

Run:
```bash
nupkg=$(find . -name "ForgeMap.*.nupkg" -path "*/Release/*" | head -1)
unzip -l "$nupkg" | grep -E "analyzers|lib"
```

Expected output should show:
```
analyzers/roslyn4.8/dotnet/cs/ForgeMap.Generator.dll
analyzers/roslyn4.8/dotnet/cs/ForgeMap.Abstractions.dll
analyzers/roslyn4.12/dotnet/cs/ForgeMap.Generator.dll
analyzers/roslyn4.12/dotnet/cs/ForgeMap.Abstractions.dll
analyzers/roslyn5.0/dotnet/cs/ForgeMap.Generator.dll
analyzers/roslyn5.0/dotnet/cs/ForgeMap.Abstractions.dll
lib/netstandard2.0/ForgeMap.dll
lib/netstandard2.0/ForgeMap.Abstractions.dll
```

- [ ] **Step 6: Commit**

```bash
git add build/pack.sh build/pack.ps1
git commit -m "feat: add multi-Roslyn pack scripts (bash + PowerShell)"
```

---

### Task 5: Update global.json for .NET 10 SDK support

**Files:**
- Modify: `global.json`

The current `global.json` pins to `9.0.100` with `rollForward: latestFeature`, which only rolls forward within `9.0.xxx`. Building `net10.0` test targets requires the .NET 10 SDK. Change the roll-forward policy to `latestMajor` so the highest installed SDK is used.

- [ ] **Step 1: Update global.json**

Replace the current content:

```json
{
  "sdk": {
    "version": "9.0.100",
    "rollForward": "latestFeature"
  }
}
```

With:

```json
{
  "sdk": {
    "version": "8.0.100",
    "rollForward": "latestMajor"
  }
}
```

This says: "use the highest SDK installed, as long as it is >= 8.0.100." Since the repo supports .NET 8, 9, and 10, this is the correct minimum.

- [ ] **Step 2: Verify the correct SDK is resolved**

Run:
```bash
dotnet --version
```
Expected: Should show `10.0.200` (or whatever the highest installed SDK is).

- [ ] **Step 3: Commit**

```bash
git add global.json
git commit -m "chore: update global.json to latestMajor for .NET 10 SDK support"
```

---

### Task 6: Add net10.0 to test project TargetFrameworks

**Files:**
- Modify: `tests/ForgeMap.Tests/ForgeMap.Tests.csproj`

- [ ] **Step 1: Update TargetFrameworks**

In `tests/ForgeMap.Tests/ForgeMap.Tests.csproj`, change line 4:

```xml
<TargetFrameworks>net8.0;net9.0</TargetFrameworks>
```

To:

```xml
<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
```

- [ ] **Step 2: Verify tests build for all three TFMs**

Run:
```bash
dotnet build tests/ForgeMap.Tests/ForgeMap.Tests.csproj
```
Expected: Build succeeds for `net8.0`, `net9.0`, and `net10.0`.

- [ ] **Step 3: Run tests for all three TFMs**

Run:
```bash
dotnet test tests/ForgeMap.Tests/ForgeMap.Tests.csproj
```
Expected: All tests pass on all three TFMs.

- [ ] **Step 4: Commit**

```bash
git add tests/ForgeMap.Tests/ForgeMap.Tests.csproj
git commit -m "test: add net10.0 to test TargetFrameworks"
```

---

### Task 7: End-to-end verification

No files to modify — this is a final validation task.

- [ ] **Step 1: Clean build from scratch**

Run:
```bash
dotnet clean
rm -rf artifacts/
```

- [ ] **Step 2: Run the full pack script**

Run:
```bash
bash build/pack.sh Release
```
Expected: All three Roslyn builds succeed. NuGet package is produced.

- [ ] **Step 3: Inspect the package contents**

Run:
```bash
nupkg=$(find . -name "ForgeMap.*.nupkg" -path "*/Release/*" | head -1)
unzip -l "$nupkg" | grep -E "analyzers|lib"
```

Expected: Six analyzer DLLs (2 per Roslyn version) + 2 lib DLLs.

- [ ] **Step 4: Verify all three analyzer variants exist in the package**

Run:
```bash
ls -la artifacts/generator/roslyn*/ForgeMap.Generator.dll
ls -la artifacts/generator/roslyn*/ForgeMap.Abstractions.dll
```

Expected: Six DLLs total — `ForgeMap.Generator.dll` and `ForgeMap.Abstractions.dll` in each of `roslyn4.8/`, `roslyn4.12/`, `roslyn5.0/`.

- [ ] **Step 5: Run all tests one final time**

Run:
```bash
dotnet test tests/ForgeMap.Tests/ForgeMap.Tests.csproj
```
Expected: All tests pass across `net8.0`, `net9.0`, and `net10.0`.
