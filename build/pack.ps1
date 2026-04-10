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

    # Clean obj/bin to prevent artifact contamination between Roslyn versions
    if (Test-Path "src/ForgeMap.Generator/obj") { Remove-Item -Recurse -Force "src/ForgeMap.Generator/obj" }
    if (Test-Path "src/ForgeMap.Generator/bin") { Remove-Item -Recurse -Force "src/ForgeMap.Generator/bin" }
    if (Test-Path "src/ForgeMap.Abstractions/obj") { Remove-Item -Recurse -Force "src/ForgeMap.Abstractions/obj" }
    if (Test-Path "src/ForgeMap.Abstractions/bin") { Remove-Item -Recurse -Force "src/ForgeMap.Abstractions/bin" }

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
