<#
.SYNOPSIS
    Runs compile-time benchmarks comparing ForgeMap, Mapperly, and AutoMapper build times.
.DESCRIPTION
    Generates mapping classes at configurable scale tiers and scenarios, then times dotnet build
    for each mapper project across multiple iterations. Produces a Markdown results table.
.PARAMETER Scales
    Array of mapping class counts to test. Default: 10, 50, 100.
.PARAMETER Scenarios
    Array of model complexity scenarios to test: Flat, Nested, Collection, Mixed. Default: all.
.PARAMETER Iterations
    Number of build iterations per mapper per scale. Default: 5.
.PARAMETER Configuration
    Build configuration. Default: Release.
.PARAMETER OutputFile
    Path to write Markdown results. Default: COMPILE_BENCHMARK_RESULTS.md in script directory.
#>
param(
    [int[]]$Scales = @(10, 50, 100),
    [string[]]$Scenarios = @('Flat', 'Nested', 'Collection', 'Mixed'),
    [int]$Iterations = 5,
    [string]$Configuration = 'Release',
    [string]$OutputFile = ''
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $scriptDir '..\..') | Select-Object -ExpandProperty Path

if (-not $OutputFile) {
    $OutputFile = Join-Path $scriptDir 'COMPILE_BENCHMARK_RESULTS.md'
}

# Pack ForgeMap as a local NuGet package so all three projects reference
# pre-compiled packages — making build times directly comparable.
# Use a unique pre-release version to avoid conflicts with published packages
# or stale entries in the global NuGet cache.
$localPkgDir = Join-Path $scriptDir 'LocalPackages'
if (Test-Path $localPkgDir) {
    Remove-Item $localPkgDir -Recurse -Force
}
New-Item -ItemType Directory -Path $localPkgDir -Force | Out-Null

$benchVersion = "99.0.0-bench.$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host "Packing ForgeMap as local NuGet package (version $benchVersion)..."
dotnet pack (Join-Path $repoRoot 'src' 'ForgeMap' 'ForgeMap.csproj') `
    -c $Configuration --verbosity quiet -o $localPkgDir -p:Version=$benchVersion
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Failed to pack ForgeMap'
    exit 1
}

# Generate NuGet.config at runtime so CI dependency scanners don't fail
# trying to restore from a non-existent LocalPackages directory.
$nugetConfig = Join-Path $scriptDir 'NuGet.config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="./LocalPackages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="ForgeMap" />
      <package pattern="ForgeMap.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
"@ | Set-Content $nugetConfig -Encoding utf8NoBOM

# Update the ForgeMap benchmark project to pin this exact version
$fmCsproj = Join-Path $scriptDir 'ForgeMap' 'ForgeMap.CompileBench.csproj'
$originalCsprojContent = Get-Content $fmCsproj -Raw
$csprojContent = $originalCsprojContent -replace 'VersionOverride="[^"]*"', "VersionOverride=""$benchVersion"""
Set-Content $fmCsproj $csprojContent -Encoding utf8NoBOM

try {

Write-Host "  Package written to $localPkgDir"

$projects = @(
    @{ Name = 'ForgeMap';   Path = Join-Path $scriptDir 'ForgeMap'   'ForgeMap.CompileBench.csproj' }
    @{ Name = 'Mapperly';   Path = Join-Path $scriptDir 'Mapperly'   'Mapperly.CompileBench.csproj' }
    @{ Name = 'AutoMapper'; Path = Join-Path $scriptDir 'AutoMapper' 'AutoMapper.CompileBench.csproj' }
)

# Generate a minimal set of models so restore can resolve all projects (source files
# referenced via Compile Include must exist at restore time for MSBuild evaluation).
& (Join-Path $scriptDir 'Generate-Models.ps1') -Count 1 -Scenario 'Flat'

# Restore all projects once — dependencies don't change between scenarios/scales,
# so there's no need to restore again inside the benchmark loop.
foreach ($proj in $projects) {
    Write-Host "Restoring $($proj.Name)..."
    dotnet restore $proj.Path --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Restore failed for $($proj.Name)"
        exit 1
    }
}

# Shut down build servers to ensure clean measurements
Write-Host 'Shutting down build servers...'
dotnet build-server shutdown 2>$null

$results = @()

foreach ($scenario in $Scenarios) {
    foreach ($scale in $Scales) {
        Write-Host "`n=== $scenario / $scale mapping classes ===" -ForegroundColor Cyan

        # Generate models for this scale and scenario
        & (Join-Path $scriptDir 'Generate-Models.ps1') -Count $scale -Scenario $scenario

        # Randomize project order to avoid systematic cache-warming bias
        $shuffled = $projects | Get-Random -Count $projects.Count

        foreach ($proj in $shuffled) {
            Write-Host "  Benchmarking $($proj.Name) ($Iterations iterations)..."
            $timings = @()

            for ($i = 1; $i -le $Iterations; $i++) {
                # Clean to force full rebuild
                dotnet clean $proj.Path -c $Configuration --verbosity quiet 2>$null
                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Clean failed for $($proj.Name) at $scenario/$scale, iteration $i"
                    exit 1
                }

                # Shut down build servers between iterations
                dotnet build-server shutdown 2>$null

                # Time the build
                $sw = [System.Diagnostics.Stopwatch]::StartNew()
                dotnet build $proj.Path -c $Configuration --no-restore --verbosity quiet
                $sw.Stop()

                if ($LASTEXITCODE -ne 0) {
                    Write-Error "Build failed for $($proj.Name) at $scenario/$scale, iteration $i"
                    exit 1
                }

                $ms = [math]::Round($sw.Elapsed.TotalMilliseconds)
                $timings += $ms
                Write-Host "    Iteration $i : ${ms}ms"
            }

            $sorted = $timings | Sort-Object
            $mid = [math]::Floor($sorted.Count / 2)
            if ($sorted.Count % 2 -eq 0) {
                $median = [math]::Round(($sorted[$mid - 1] + $sorted[$mid]) / 2)
            } else {
                $median = $sorted[$mid]
            }
            $min = $sorted[0]
            $max = $sorted[-1]

            $results += [PSCustomObject]@{
                Scenario = $scenario
                Scale    = $scale
                Mapper   = $proj.Name
                Median   = $median
                Min      = $min
                Max      = $max
            }
        }
    }
}

# Build Markdown output
$md = @()
$md += '# Compile-Time Benchmark Results'
$md += ''
$md += "Measures ``dotnet build`` wall-clock time (clean rebuild, $Configuration configuration) for projects"
$md += 'using ForgeMap, Mapperly, and AutoMapper at varying numbers of mapping classes.'
$md += ''
$md += 'AutoMapper is reflection-based (no source generator) and serves as a baseline showing'
$md += 'pure compilation cost without generator overhead.'
$md += ''
$md += '## Environment'
$md += ''
$md += '```'
$md += "OS:        $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)"
$md += "CPU:       $([System.Environment]::ProcessorCount) logical cores"
$md += ".NET SDK:  $(dotnet --version)"
$md += "Date:      $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$md += "Iterations per cell: $Iterations (median reported)"
$md += '```'
$md += ''

foreach ($scenario in $Scenarios) {
    $md += "## $scenario Scenario"
    $md += ''

    switch ($scenario) {
        'Flat'       { $md += 'Each class has 10 flat properties (int, string, decimal, bool, DateTime).' }
        'Nested'     { $md += 'Each class has 4 flat properties + 2 nested object properties (auto-wired).' }
        'Collection' { $md += 'Each class has 3 flat properties + 2 collection properties (List<T>, auto-wired).' }
        'Mixed'      { $md += 'Each class has 6 flat properties + 2 nested objects + 1 collection property.' }
    }

    $md += ''
    $md += '| Scale (classes) | ForgeMap (ms) | Mapperly (ms) | AutoMapper (ms) | ForgeMap vs Mapperly |'
    $md += '|----------------:|--------------:|--------------:|----------------:|---------------------:|'

    foreach ($scale in $Scales) {
        $fm = $results | Where-Object { $_.Scenario -eq $scenario -and $_.Scale -eq $scale -and $_.Mapper -eq 'ForgeMap' }
        $mp = $results | Where-Object { $_.Scenario -eq $scenario -and $_.Scale -eq $scale -and $_.Mapper -eq 'Mapperly' }
        $am = $results | Where-Object { $_.Scenario -eq $scenario -and $_.Scale -eq $scale -and $_.Mapper -eq 'AutoMapper' }

        $ratio = if ($mp.Median -gt 0) { '{0:F2}x' -f ($fm.Median / $mp.Median) } else { 'N/A' }

        $md += "| $scale | $($fm.Median) | $($mp.Median) | $($am.Median) | $ratio |"
    }

    $md += ''
}

$md += '## Detailed Timings (min / median / max)'
$md += ''
$md += '| Scenario | Scale | Mapper | Min (ms) | Median (ms) | Max (ms) |'
$md += '|---------:|------:|-------:|---------:|------------:|---------:|'

foreach ($r in $results) {
    $md += "| $($r.Scenario) | $($r.Scale) | $($r.Mapper) | $($r.Min) | $($r.Median) | $($r.Max) |"
}

$md += ''

$mdText = $md -join "`n"
Set-Content -Path $OutputFile -Value $mdText -Encoding utf8NoBOM

Write-Host "`n$mdText"
Write-Host "`nResults written to: $OutputFile" -ForegroundColor Green

} finally {
    # Restore the ForgeMap csproj to its original content to avoid dirtying the worktree
    [System.IO.File]::WriteAllText($fmCsproj, $originalCsprojContent)
}
