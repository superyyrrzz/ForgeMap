using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace ForgeMap.Generator;

/// <summary>
/// Resolved configuration for a forger class, combining assembly-level defaults
/// and class-level [ForgeMap] attribute overrides.
/// </summary>
internal sealed class ForgerConfig
{
    /// <summary>0 = ReturnNull (default), 1 = ThrowException</summary>
    public int NullHandling { get; set; }

    /// <summary>0 = ByName (default, case-sensitive), 1 = ByNameCaseInsensitive</summary>
    public int PropertyMatching { get; set; }

    /// <summary>Whether to generate collection mapping methods. Default true.</summary>
    public bool GenerateCollectionMappings { get; set; } = true;

    /// <summary>Diagnostic IDs to suppress for this forger (e.g. "FM0005").</summary>
    public HashSet<string> SuppressDiagnostics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>0 = NullForgiving (default), 1 = SkipNull, 2 = CoalesceToDefault, 3 = ThrowException, 4 = CoalesceToNew</summary>
    public int NullPropertyHandling { get; set; }

    /// <summary>Whether to auto-discover matching forge methods for nested complex properties. Default true.</summary>
    public bool AutoWireNestedMappings { get; set; } = true;

    /// <summary>0 = Parse (default, null-safe), 1 = TryParse, 2 = None, 3 = StrictParse</summary>
    public int StringToEnum { get; set; }

    /// <summary>0 = Auto (default), 1 = PreferParameterless</summary>
    public int ConstructorPreference { get; set; }

    public StringComparison PropertyNameComparison =>
        PropertyMatching == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

/// <summary>
/// Configuration for an ExistingTarget property from [ForgeProperty].
/// </summary>
internal readonly struct ExistingTargetConfig
{
    public ExistingTargetConfig(int collectionUpdate, string? keyProperty)
    {
        CollectionUpdate = collectionUpdate;
        KeyProperty = keyProperty;
    }

    /// <summary>0 = Replace, 1 = Add, 2 = Sync</summary>
    public int CollectionUpdate { get; }

    /// <summary>Key property name for Sync strategy. Null otherwise.</summary>
    public string? KeyProperty { get; }
}

/// <summary>
/// Resolved attribute configuration for a forge method.
/// </summary>
internal readonly struct ResolvedMethodConfig
{
    public ResolvedMethodConfig(
        HashSet<string> ignoredProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        List<string> beforeForgeHooks,
        List<string> afterForgeHooks,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        Dictionary<string, ExistingTargetConfig> existingTargetProperties,
        Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)>? propertyConvertWithMappings = null,
        Dictionary<string, string>? selectPropertyMappings = null,
        Dictionary<string, string>? conditionMappings = null,
        Dictionary<string, string>? skipWhenMappings = null)
    {
        IgnoredProperties = ignoredProperties;
        PropertyMappings = propertyMappings;
        ResolverMappings = resolverMappings;
        ForgeWithMappings = forgeWithMappings;
        BeforeForgeHooks = beforeForgeHooks;
        AfterForgeHooks = afterForgeHooks;
        NullPropertyHandlingOverrides = nullPropertyHandlingOverrides;
        ExistingTargetProperties = existingTargetProperties;
        PropertyConvertWithMappings = propertyConvertWithMappings ?? new Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)>(StringComparer.Ordinal);
        SelectPropertyMappings = selectPropertyMappings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        ConditionMappings = conditionMappings ?? new Dictionary<string, string>(StringComparer.Ordinal);
        SkipWhenMappings = skipWhenMappings ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public HashSet<string> IgnoredProperties { get; }
    public Dictionary<string, string> PropertyMappings { get; }
    public Dictionary<string, string> ResolverMappings { get; }
    public Dictionary<string, string> ForgeWithMappings { get; }
    public List<string> BeforeForgeHooks { get; }
    public List<string> AfterForgeHooks { get; }
    /// <summary>Per-property NullPropertyHandling overrides. Key = dest property name, Value = enum int value (0-4). Only explicitly set overrides are included.</summary>
    public Dictionary<string, int> NullPropertyHandlingOverrides { get; }
    /// <summary>Properties marked with ExistingTarget = true. Key = dest property name.</summary>
    public Dictionary<string, ExistingTargetConfig> ExistingTargetProperties { get; }
    /// <summary>Per-property ConvertWith mappings. Key = dest property name, Value = (MethodName?, ConverterTypeName?).</summary>
    public Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)> PropertyConvertWithMappings { get; }
    /// <summary>Per-property SelectProperty (v1.7) mappings. Key = dest property name, Value = element member name to project.</summary>
    public Dictionary<string, string> SelectPropertyMappings { get; }
    /// <summary>Per-property Condition (v1.7) predicate mappings. Key = dest property name, Value = predicate method name (called with source-property value).</summary>
    public Dictionary<string, string> ConditionMappings { get; }
    /// <summary>Per-property SkipWhen (v1.7) predicate mappings. Key = dest property name, Value = predicate method name (called with source object).</summary>
    public Dictionary<string, string> SkipWhenMappings { get; }
}
