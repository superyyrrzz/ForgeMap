<#
.SYNOPSIS
    Generates model classes and mapper declarations for compile-time benchmarks.
.DESCRIPTION
    Creates N source/destination model pairs in Shared/Models/ and generates
    mapper declaration files for ForgeMap, Mapperly, and AutoMapper projects.
.PARAMETER Count
    Number of mapping class pairs to generate. Default: 10.
.PARAMETER Scenario
    Model complexity scenario: Flat, Nested, Collection, Mixed. Default: Flat.
#>
param(
    [int]$Count = 10,
    [ValidateSet('Flat', 'Nested', 'Collection', 'Mixed')]
    [string]$Scenario = 'Flat'
)

$ErrorActionPreference = 'Stop'
$scriptDir = $PSScriptRoot

# Clean and recreate shared models directory
$modelsDir = Join-Path $scriptDir 'Shared' 'Models'
if (Test-Path $modelsDir) {
    Remove-Item $modelsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $modelsDir -Force | Out-Null

Write-Host "Generating $Count mapping class pairs (scenario: $Scenario)..."

# Shared child types used by Nested/Collection/Mixed scenarios
if ($Scenario -in 'Nested', 'Collection', 'Mixed') {
    $childContent = @"
namespace ForgeMap.CompileBenchmarks.Models;

public class AddressSource
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class AddressDest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class TagSource
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

public class TagDest
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}
"@
    Set-Content -Path (Join-Path $modelsDir "SharedChildren.cs") -Value $childContent -Encoding utf8NoBOM
}

# Generate model files
for ($i = 1; $i -le $Count; $i++) {
    switch ($Scenario) {
        'Flat' {
            $content = @"
namespace ForgeMap.CompileBenchmarks.Models;

public class Source$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public int Count { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}

public class Dest$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public int Count { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
}
"@
        }
        'Nested' {
            $content = @"
namespace ForgeMap.CompileBenchmarks.Models;

public class Source$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public AddressSource? PrimaryAddress { get; set; }
    public AddressSource? BillingAddress { get; set; }
}

public class Dest$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public AddressDest? PrimaryAddress { get; set; }
    public AddressDest? BillingAddress { get; set; }
}
"@
        }
        'Collection' {
            $content = @"
using System.Collections.Generic;

namespace ForgeMap.CompileBenchmarks.Models;

public class Source$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<TagSource> Tags { get; set; } = new();
    public List<AddressSource> Addresses { get; set; } = new();
}

public class Dest$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public List<TagDest> Tags { get; set; } = new();
    public List<AddressDest> Addresses { get; set; } = new();
}
"@
        }
        'Mixed' {
            $content = @"
using System.Collections.Generic;

namespace ForgeMap.CompileBenchmarks.Models;

public class Source$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public AddressSource? PrimaryAddress { get; set; }
    public AddressSource? BillingAddress { get; set; }
    public List<TagSource> Tags { get; set; } = new();
}

public class Dest$i
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public AddressDest? PrimaryAddress { get; set; }
    public AddressDest? BillingAddress { get; set; }
    public List<TagDest> Tags { get; set; } = new();
}
"@
        }
    }
    Set-Content -Path (Join-Path $modelsDir "Mapping$i.cs") -Value $content -Encoding utf8NoBOM
}

# Build mapper method declarations based on scenario
$forgeMethods = @()
$mapperlyMethods = @()
$autoMapperMaps = @()

# Child type methods (needed for Nested/Collection/Mixed)
if ($Scenario -in 'Nested', 'Collection', 'Mixed') {
    $forgeMethods += "    public partial AddressDest Forge(AddressSource source);"
    $mapperlyMethods += "    public partial AddressDest Map(AddressSource source);"
    $autoMapperMaps += "        cfg.CreateMap<AddressSource, AddressDest>();"
}
if ($Scenario -in 'Collection', 'Mixed') {
    $forgeMethods += "    public partial TagDest Forge(TagSource source);"
    $mapperlyMethods += "    public partial TagDest Map(TagSource source);"
    $autoMapperMaps += "        cfg.CreateMap<TagSource, TagDest>();"
}

# Per-class methods
for ($i = 1; $i -le $Count; $i++) {
    $forgeMethods += "    public partial Dest$i Forge(Source$i source);"
    $mapperlyMethods += "    public partial Dest$i Map(Source$i source);"
    $autoMapperMaps += "        cfg.CreateMap<Source$i, Dest$i>();"
}

$forgeMethodsStr = $forgeMethods -join "`n"
$forgeContent = @"
using ForgeMap;
using ForgeMap.CompileBenchmarks.Models;

namespace ForgeMap.CompileBenchmarks;

[ForgeMap]
public partial class CompileBenchForger
{
$forgeMethodsStr
}
"@
Set-Content -Path (Join-Path $scriptDir 'ForgeMap' 'Forger.cs') -Value $forgeContent -Encoding utf8NoBOM

$mapperlyMethodsStr = $mapperlyMethods -join "`n"
$mapperlyContent = @"
using ForgeMap.CompileBenchmarks.Models;
using Riok.Mapperly.Abstractions;

namespace ForgeMap.CompileBenchmarks;

[Mapper]
public partial class CompileBenchMapper
{
$mapperlyMethodsStr
}
"@
Set-Content -Path (Join-Path $scriptDir 'Mapperly' 'Mapper.cs') -Value $mapperlyContent -Encoding utf8NoBOM

$autoMapperMapsStr = $autoMapperMaps -join "`n"
$autoMapperContent = @"
using AutoMapper;
using ForgeMap.CompileBenchmarks.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForgeMap.CompileBenchmarks;

public static class CompileBenchAutoMapperConfig
{
    public static MapperConfiguration Create()
    {
        return new MapperConfiguration(cfg =>
        {
$autoMapperMapsStr
        }, NullLoggerFactory.Instance);
    }
}
"@
Set-Content -Path (Join-Path $scriptDir 'AutoMapper' 'MapperConfig.cs') -Value $autoMapperContent -Encoding utf8NoBOM

Write-Host "Generated $Count model pairs and mapper declarations ($Scenario scenario)."
