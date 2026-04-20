using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private System.Collections.Immutable.ImmutableArray<AttributeData> GetCachedAttributes(IMethodSymbol method)
    {
        if (!_methodAttributesCache.TryGetValue(method, out var attrs))
        {
            attrs = method.GetAttributes();
            _methodAttributesCache[method] = attrs;
        }
        return attrs;
    }

    private IEnumerable<AttributeData> GetMethodAttributes(IMethodSymbol method, INamedTypeSymbol? attributeSymbol)
    {
        if (attributeSymbol == null)
            yield break;
        foreach (var attr in GetCachedAttributes(method))
        {
            if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attributeSymbol))
                yield return attr;
        }
    }

    private HashSet<string> GetIgnoredProperties(IMethodSymbol method)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _ignoreAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length > 0)
            {
                var arg = attr.ConstructorArguments[0];
                if (arg.Kind == TypedConstantKind.Array)
                {
                    foreach (var item in arg.Values)
                    {
                        if (item.Value is string propName)
                            ignored.Add(propName);
                    }
                }
            }
        }
        return ignored;
    }

    /// <summary>
    /// Gets property mappings from [ForgeProperty] attributes.
    /// Returns a dictionary mapping destination property name to source property path.
    /// </summary>
    private Dictionary<string, string> GetPropertyMappings(IMethodSymbol method)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgePropertyAttributeSymbol))
        {
            // [ForgeProperty(sourceProperty, destinationProperty)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var sourceProperty = attr.ConstructorArguments[0].Value as string;
                var destinationProperty = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(sourceProperty) && !string.IsNullOrEmpty(destinationProperty))
                {
                    mappings[destinationProperty!] = sourceProperty!;
                }
            }
        }
        return mappings;
    }

    /// <summary>
    /// Gets per-property NullPropertyHandling overrides from [ForgeProperty] attributes.
    /// Returns a dictionary mapping destination property name to the NullPropertyHandling int value.
    /// Only includes properties where the override was explicitly set (not the -1 sentinel).
    /// </summary>
    private Dictionary<string, int> GetNullPropertyHandlingOverrides(IMethodSymbol method)
    {
        var overrides = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgePropertyAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(destinationProperty))
                    continue;

                foreach (var named in attr.NamedArguments)
                {
                    if (named.Key == "NullPropertyHandling" && named.Value.Value is int value && value != -1)
                    {
                        overrides[destinationProperty!] = value;
                    }
                }
            }
        }
        return overrides;
    }

    /// <summary>
    /// Gets properties marked with ExistingTarget = true from [ForgeProperty] attributes.
    /// Returns a dictionary mapping destination property name to its ExistingTarget configuration.
    /// </summary>
    private Dictionary<string, ExistingTargetConfig> GetExistingTargetProperties(IMethodSymbol method)
    {
        var result = new Dictionary<string, ExistingTargetConfig>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgePropertyAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(destinationProperty))
                    continue;

                bool existingTarget = false;
                int collectionUpdate = 0; // Replace
                string? keyProperty = null;

                foreach (var named in attr.NamedArguments)
                {
                    switch (named.Key)
                    {
                        case "ExistingTarget":
                            existingTarget = named.Value.Value is true;
                            break;
                        case "CollectionUpdate":
                            if (named.Value.Value is int cu)
                                collectionUpdate = cu;
                            break;
                        case "KeyProperty":
                            keyProperty = named.Value.Value as string;
                            break;
                    }
                }

                if (existingTarget)
                {
                    result[destinationProperty!] = new ExistingTargetConfig(collectionUpdate, keyProperty);
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Gets per-property ConvertWith mappings from [ForgeProperty(ConvertWith=...)] attributes.
    /// Returns a dictionary mapping destination property name to (MethodName, ConverterTypeName).
    /// </summary>
    private Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)> GetPropertyConvertWithMappings(IMethodSymbol method)
    {
        var result = new Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)>(StringComparer.Ordinal);

        // From [ForgeProperty(..., ConvertWith = "...", ConvertWithType = typeof(...))]
        foreach (var attr in GetMethodAttributes(method, _forgePropertyAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[1].Value as string;
                if (string.IsNullOrEmpty(destinationProperty))
                    continue;

                string? methodName = null;
                string? converterTypeName = null;
                INamedTypeSymbol? converterTypeSymbol = null;

                foreach (var named in attr.NamedArguments)
                {
                    switch (named.Key)
                    {
                        case "ConvertWith":
                            methodName = named.Value.Value as string;
                            break;
                        case "ConvertWithType":
                            if (named.Value.Value is INamedTypeSymbol typeSymbol)
                            {
                                converterTypeName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                                converterTypeSymbol = typeSymbol;
                            }
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(methodName) || !string.IsNullOrEmpty(converterTypeName))
                {
                    result[destinationProperty!] = (methodName, converterTypeName, converterTypeSymbol);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets per-property SelectProperty mappings from [ForgeProperty(SelectProperty=...)] attributes.
    /// Returns a dictionary mapping destination property name to the projected element-member name.
    /// </summary>
    private Dictionary<string, string> GetSelectPropertyMappings(IMethodSymbol method)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgePropertyAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length < 2)
                continue;
            var destinationProperty = attr.ConstructorArguments[1].Value as string;
            if (string.IsNullOrEmpty(destinationProperty))
                continue;

            foreach (var named in attr.NamedArguments)
            {
                if (named.Key == "SelectProperty" && named.Value.Value is string memberName && !string.IsNullOrEmpty(memberName))
                {
                    result[destinationProperty!] = memberName;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Gets resolver mappings from [ForgeFrom] attributes.
    /// Returns a dictionary mapping destination property name to resolver method name.
    /// </summary>
    private Dictionary<string, string> GetResolverMappings(IMethodSymbol method)
    {
        var resolvers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgeFromAttributeSymbol))
        {
            // [ForgeFrom(destinationProperty, resolverMethodName)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[0].Value as string;
                var resolverMethodName = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(destinationProperty) && !string.IsNullOrEmpty(resolverMethodName))
                {
                    resolvers[destinationProperty!] = resolverMethodName!;
                }
            }
        }
        return resolvers;
    }

    /// <summary>
    /// Gets [ForgeWith] mappings.
    /// Returns a dictionary mapping destination property name to forging method name.
    /// </summary>
    private Dictionary<string, string> GetForgeWithMappings(IMethodSymbol method)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in GetMethodAttributes(method, _forgeWithAttributeSymbol))
        {
            // [ForgeWith(destinationProperty, forgingMethodName)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[0].Value as string;
                var forgingMethodName = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(destinationProperty) && !string.IsNullOrEmpty(forgingMethodName))
                {
                    mappings[destinationProperty!] = forgingMethodName!;
                }
            }
        }
        return mappings;
    }

    /// <summary>
    /// Gets [IncludeBaseForge] attribute data from a method.
    /// Returns a list of (BaseSourceType, BaseDestinationType, AttributeData) tuples.
    /// </summary>
    private List<(INamedTypeSymbol BaseSourceType, INamedTypeSymbol BaseDestType, AttributeData Attribute)> GetIncludeBaseForgeAttributes(IMethodSymbol method)
    {
        var result = new List<(INamedTypeSymbol, INamedTypeSymbol, AttributeData)>();
        foreach (var attr in GetMethodAttributes(method, _includeBaseForgeAttributeSymbol))
        {
            // [IncludeBaseForge(typeof(BaseSource), typeof(BaseDest))]
            if (attr.ConstructorArguments.Length >= 2 &&
                attr.ConstructorArguments[0].Value is INamedTypeSymbol baseSourceType &&
                attr.ConstructorArguments[1].Value is INamedTypeSymbol baseDestType)
            {
                result.Add((baseSourceType, baseDestType, attr));
            }
        }
        return result;
    }

    /// <summary>
    /// Finds a forge method in the forger class that maps the given source type to the given destination type.
    /// Used for [IncludeBaseForge] resolution.
    /// </summary>
    private IMethodSymbol? FindBaseForgeMethod(INamedTypeSymbol forgerType, INamedTypeSymbol baseSourceType, INamedTypeSymbol baseDestType)
    {
        var partialMethods = GetPartialMethods(forgerType);

        // Prefer return-style: non-void return, single parameter (source), return type = dest
        // Use stable ordering by name for deterministic resolution when multiple candidates exist
        IMethodSymbol? returnStyle = null;
        string? returnStyleName = null;
        foreach (var m in partialMethods)
        {
            if (!m.ReturnsVoid &&
                m.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, baseSourceType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, baseDestType))
            {
                if (returnStyle == null || string.Compare(m.Name, returnStyleName, StringComparison.Ordinal) < 0)
                {
                    returnStyle = m;
                    returnStyleName = m.Name;
                }
            }
        }

        if (returnStyle != null)
            return returnStyle;

        // Fall back to ForgeInto-style: void return, two parameters where the destination
        // parameter is marked with [UseExistingValue] and matches baseDestType
        IMethodSymbol? intoStyle = null;
        string? intoStyleName = null;
        foreach (var m in partialMethods)
        {
            if (m.ReturnsVoid &&
                m.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, baseSourceType) &&
                m.Parameters.Any(p =>
                    SymbolEqualityComparer.Default.Equals(p.Type, baseDestType) &&
                    HasUseExistingValueAttribute(p)))
            {
                if (intoStyle == null || string.Compare(m.Name, intoStyleName, StringComparison.Ordinal) < 0)
                {
                    intoStyle = m;
                    intoStyleName = m.Name;
                }
            }
        }

        return intoStyle;
    }

    /// <summary>
    /// Checks whether <paramref name="derived"/> is assignable to <paramref name="baseType"/>
    /// (i.e. is the same type, a subclass, or implements the interface).
    /// </summary>
    private static bool DerivesFrom(INamedTypeSymbol derived, INamedTypeSymbol baseType)
    {
        INamedTypeSymbol? current = derived;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }

        // Also check interface implementation
        if (baseType.TypeKind == TypeKind.Interface)
        {
            foreach (var iface in derived.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, baseType))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves inherited configuration from [IncludeBaseForge] attributes.
    /// Merges base config into the given collections; explicit attributes on the derived method take precedence.
    /// Supports chaining through multiple inheritance levels.
    /// </summary>
    private void ResolveInheritedConfig(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context,
        HashSet<string> ignoredProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        Dictionary<string, ExistingTargetConfig>? existingTargetProperties,
        Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)> propertyConvertWithMappings,
        Dictionary<string, string> selectPropertyMappings,
        HashSet<string> visited)
    {
        var includeBaseForges = GetIncludeBaseForgeAttributes(method);
        if (includeBaseForges.Count == 0)
            return;

        var sourceType = method.Parameters[0].Type as INamedTypeSymbol;

        // For ForgeInto methods (void return with [UseExistingValue] param), derive destType
        // from the [UseExistingValue] parameter instead of ReturnType.
        INamedTypeSymbol? destType;
        if (method.ReturnsVoid)
        {
            var useExistingParam = GetUseExistingValueParameter(method);
            destType = useExistingParam?.Type as INamedTypeSymbol;
        }
        else
        {
            destType = method.ReturnType as INamedTypeSymbol;
        }

        if (sourceType == null || destType == null)
            return;

        // Collect the set of properties explicitly configured on the derived method
        // so we can detect and report FM0021 overrides
        var explicitIgnored = new HashSet<string>(ignoredProperties, StringComparer.Ordinal);
        var explicitPropertyMappings = new HashSet<string>(propertyMappings.Keys, StringComparer.Ordinal);
        var explicitResolverMappings = new HashSet<string>(resolverMappings.Keys, StringComparer.Ordinal);
        var explicitForgeWithMappings = new HashSet<string>(forgeWithMappings.Keys, StringComparer.Ordinal);

        foreach (var (baseSourceType, baseDestType, attrData) in includeBaseForges)
        {
            // Cycle detection: build a key from (source, dest) type pair
            var pairKey = $"{baseSourceType.ToDisplayString()}->{baseDestType.ToDisplayString()}";
            if (!visited.Add(pairKey))
                continue; // Already visited this pair — skip to avoid infinite recursion

            // Validate: source type must derive from base source type
            if (!DerivesFrom(sourceType, baseSourceType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeTypeMismatch,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    sourceType.ToDisplayString(), baseSourceType.ToDisplayString());
                continue;
            }

            // Validate: dest type must derive from base dest type
            if (!DerivesFrom(destType, baseDestType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeTypeMismatch,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    destType.ToDisplayString(), baseDestType.ToDisplayString());
                continue;
            }

            // Find the base forge method
            var baseMethod = FindBaseForgeMethod(forger.Symbol, baseSourceType, baseDestType);
            if (baseMethod == null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeMethodNotFound,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    baseSourceType.ToDisplayString(), baseDestType.ToDisplayString());
                continue;
            }

            // Recursively resolve the base method's inherited config first (chaining support)
            var baseIgnored = GetIgnoredProperties(baseMethod);
            var basePropertyMappings = GetPropertyMappings(baseMethod);
            var baseResolverMappings = GetResolverMappings(baseMethod);
            var baseForgeWithMappings = GetForgeWithMappings(baseMethod);
            var baseNullPropertyHandlingOverrides = GetNullPropertyHandlingOverrides(baseMethod);
            var baseExistingTargetProperties = GetExistingTargetProperties(baseMethod);
            var basePropertyConvertWithMappings = GetPropertyConvertWithMappings(baseMethod);
            var baseSelectPropertyMappings = GetSelectPropertyMappings(baseMethod);
            ResolveInheritedConfig(baseMethod, forger, context, baseIgnored, basePropertyMappings, baseResolverMappings, baseForgeWithMappings, baseNullPropertyHandlingOverrides, existingTargetProperties != null ? baseExistingTargetProperties : null, basePropertyConvertWithMappings, baseSelectPropertyMappings, visited);

            // Merge all base config into derived using first-wins semantics + FM0021 override reporting
            var diagLocation = attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault();

            bool IsExplicitlyConfigured(string propName) =>
                explicitIgnored.Contains(propName) || explicitPropertyMappings.Contains(propName) ||
                explicitResolverMappings.Contains(propName) || explicitForgeWithMappings.Contains(propName);

            bool IsAlreadyConfigured(string propName) =>
                ignoredProperties.Contains(propName) || propertyMappings.ContainsKey(propName) ||
                resolverMappings.ContainsKey(propName) || forgeWithMappings.ContainsKey(propName);

            foreach (var propName in baseIgnored)
            {
                if (IsExplicitlyConfigured(propName))
                {
                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.IncludeBaseForgeOverridden, diagLocation, propName);
                    continue;
                }
                if (!IsAlreadyConfigured(propName))
                    ignoredProperties.Add(propName);
            }

            foreach (var kvp in basePropertyMappings)
            {
                if (IsExplicitlyConfigured(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.IncludeBaseForgeOverridden, diagLocation, kvp.Key);
                    continue;
                }
                if (!IsAlreadyConfigured(kvp.Key))
                    propertyMappings[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in baseResolverMappings)
            {
                if (IsExplicitlyConfigured(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.IncludeBaseForgeOverridden, diagLocation, kvp.Key);
                    continue;
                }
                if (!IsAlreadyConfigured(kvp.Key))
                    resolverMappings[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in baseForgeWithMappings)
            {
                if (IsExplicitlyConfigured(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.IncludeBaseForgeOverridden, diagLocation, kvp.Key);
                    continue;
                }
                if (!IsAlreadyConfigured(kvp.Key))
                    forgeWithMappings[kvp.Key] = kvp.Value;
            }

            // Merge base NullPropertyHandling overrides using first-wins semantics
            foreach (var kvp in baseNullPropertyHandlingOverrides)
            {
                if (!nullPropertyHandlingOverrides.ContainsKey(kvp.Key))
                    nullPropertyHandlingOverrides[kvp.Key] = kvp.Value;
            }

            // Merge base ExistingTarget properties using first-wins semantics
            if (existingTargetProperties != null)
            {
                foreach (var kvp in baseExistingTargetProperties)
                {
                    if (!existingTargetProperties.ContainsKey(kvp.Key))
                        existingTargetProperties[kvp.Key] = kvp.Value;
                }
            }

            // Merge base PropertyConvertWith mappings using first-wins semantics
            foreach (var kvp in basePropertyConvertWithMappings)
            {
                if (!propertyConvertWithMappings.ContainsKey(kvp.Key))
                    propertyConvertWithMappings[kvp.Key] = kvp.Value;
            }

            // Merge base SelectProperty mappings using first-wins semantics.
            // Skip when the derived method explicitly overrides this dest property — otherwise
            // a base projection can leak past the override and either apply the wrong projection
            // or trigger a bogus FM0072 conflict against the derived [ForgeFrom]/[ForgeWith].
            foreach (var kvp in baseSelectPropertyMappings)
            {
                if (IsExplicitlyConfigured(kvp.Key))
                    continue;
                if (!selectPropertyMappings.ContainsKey(kvp.Key))
                    selectPropertyMappings[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Resolves all attribute-based configuration for a forge method (ignores, mappings, resolvers, forgeWith, hooks).
    /// </summary>
    private ResolvedMethodConfig ResolveMethodConfig(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        INamedTypeSymbol destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var ignoredProperties = GetIgnoredProperties(method);
        var propertyMappings = GetPropertyMappings(method);
        var resolverMappings = GetResolverMappings(method);
        var forgeWithMappings = GetForgeWithMappings(method);
        var nullPropertyHandlingOverrides = GetNullPropertyHandlingOverrides(method);
        var existingTargetProperties = GetExistingTargetProperties(method);
        var propertyConvertWithMappings = GetPropertyConvertWithMappings(method);
        var selectPropertyMappings = GetSelectPropertyMappings(method);

        ResolveInheritedConfig(method, forger, context, ignoredProperties, propertyMappings, resolverMappings, forgeWithMappings, nullPropertyHandlingOverrides, existingTargetProperties, propertyConvertWithMappings, selectPropertyMappings, new HashSet<string>());

        var beforeForgeHooks = GetBeforeForgeHooks(method)
            .Select(h => ValidateBeforeForgeHook(h, sourceType, forger, context, method))
            .OfType<string>()
            .ToList();
        var afterForgeHooks = GetAfterForgeHooks(method)
            .Select(h => ValidateAfterForgeHook(h, sourceType, destinationType, forger, context, method))
            .OfType<string>()
            .ToList();

        return new ResolvedMethodConfig(
            ignoredProperties,
            propertyMappings,
            resolverMappings,
            forgeWithMappings,
            beforeForgeHooks,
            afterForgeHooks,
            nullPropertyHandlingOverrides,
            existingTargetProperties,
            propertyConvertWithMappings,
            selectPropertyMappings);
    }

    /// <summary>
    /// Gets [BeforeForge] hook method names in declaration order.
    /// </summary>
    private List<string> GetBeforeForgeHooks(IMethodSymbol method)
    {
        var hooks = new List<string>();
        foreach (var attr in GetMethodAttributes(method, _beforeForgeAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string methodName)
            {
                hooks.Add(methodName);
            }
        }
        return hooks;
    }

    /// <summary>
    /// Gets [AfterForge] hook method names in declaration order.
    /// </summary>
    private List<string> GetAfterForgeHooks(IMethodSymbol method)
    {
        var hooks = new List<string>();
        foreach (var attr in GetMethodAttributes(method, _afterForgeAttributeSymbol))
        {
            if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string methodName)
            {
                hooks.Add(methodName);
            }
        }
        return hooks;
    }
}
