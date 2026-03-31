using System;
using System.Collections.Generic;

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

    /// <summary>0 = NullForgiving (default), 1 = SkipNull, 2 = CoalesceToDefault, 3 = ThrowException</summary>
    public int NullPropertyHandling { get; set; }

    /// <summary>Whether to auto-discover matching forge methods for nested complex properties. Default true.</summary>
    public bool AutoWireNestedMappings { get; set; } = true;

    public StringComparison PropertyNameComparison =>
        PropertyMatching == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
        Dictionary<string, int> nullPropertyHandlingOverrides)
    {
        IgnoredProperties = ignoredProperties;
        PropertyMappings = propertyMappings;
        ResolverMappings = resolverMappings;
        ForgeWithMappings = forgeWithMappings;
        BeforeForgeHooks = beforeForgeHooks;
        AfterForgeHooks = afterForgeHooks;
        NullPropertyHandlingOverrides = nullPropertyHandlingOverrides;
    }

    public HashSet<string> IgnoredProperties { get; }
    public Dictionary<string, string> PropertyMappings { get; }
    public Dictionary<string, string> ResolverMappings { get; }
    public Dictionary<string, string> ForgeWithMappings { get; }
    public List<string> BeforeForgeHooks { get; }
    public List<string> AfterForgeHooks { get; }
    /// <summary>Per-property NullPropertyHandling overrides. Key = dest property name, Value = enum int value (0-3). Only explicitly set overrides are included.</summary>
    public Dictionary<string, int> NullPropertyHandlingOverrides { get; }
}
