using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    /// <summary>
    /// Generates a reverse forging method for a method annotated with [ReverseForge].
    /// Swaps source and destination types, reverses [ForgeProperty] mappings,
    /// and emits warnings for [ForgeFrom] and [ForgeWith] that cannot be auto-reversed.
    /// </summary>
    private string GenerateReverseMethod(
        IMethodSymbol forwardMethod,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var forwardSourceType = forwardMethod.Parameters[0].Type as INamedTypeSymbol;
        var forwardDestType = forwardMethod.ReturnType as INamedTypeSymbol;

        if (forwardSourceType == null || forwardDestType == null)
            return string.Empty;

        // [ReverseForge] only supports class/struct/record object mappings.
        // Enum, collection, primitive, and delegate types use specialized forward code paths
        // that cannot be auto-reversed (would produce invalid code like `return new int { };`).
        if (!IsReversibleObjectType(forwardSourceType) || !IsReversibleObjectType(forwardDestType))
            return string.Empty;

        // In reverse: source is the forward dest, dest is the forward source
        var reverseSourceType = forwardDestType;
        var reverseDestType = forwardSourceType;

        // Get forward attributes
        var forwardIgnored = GetIgnoredProperties(forwardMethod);
        var forwardPropertyMappings = GetPropertyMappings(forwardMethod);
        var forwardResolverMappings = GetResolverMappings(forwardMethod);
        var forwardForgeWithMappings = GetForgeWithMappings(forwardMethod);

        // Build reverseIgnored: translate forward ignored dest names to reverse dest (= forward source) names.
        // forwardIgnored contains forward dest property names. In reverse, the dest is the forward source type.
        // When [ForgeProperty] renames are involved (e.g. ForgeProperty("BookTitle", "DisplayTitle")),
        // the forward ignored name "DisplayTitle" needs to map back to "BookTitle" in the reverse dest.
        var reverseIgnored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ignoredName in forwardIgnored)
        {
            // Check if any forward mapping maps TO this ignored dest name
            var forwardSourceName = forwardPropertyMappings
                .Where(kvp => string.Equals(kvp.Key, ignoredName, StringComparison.Ordinal))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            if (forwardSourceName != null && !forwardSourceName.Contains("."))
            {
                // The forward source name IS the reverse dest property name
                reverseIgnored.Add(forwardSourceName);
            }
            else
            {
                // No rename — same property name in both directions
                reverseIgnored.Add(ignoredName);
            }
        }

        // Build reverse property mappings: swap source/dest names
        var reversePropertyMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in forwardPropertyMappings)
        {
            // Forward: ForgeProperty(sourceA, destB) means destB = source.A
            // Reverse: ForgeProperty(destB, sourceA) means sourceA = source.B
            // But only if the source prop is a simple name (not a nested path)
            var forwardSourceProp = kvp.Value;
            var forwardDestProp = kvp.Key;

            if (!forwardSourceProp.Contains("."))
            {
                reversePropertyMappings[forwardSourceProp] = forwardDestProp;
            }
            // Nested paths can't be reversed (would need to unflatten)
        }

        // Emit FM0012 warnings for [ForgeFrom] properties
        foreach (var kvp in forwardResolverMappings)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ForgeFromCannotBeReversed,
                forwardMethod.Locations.FirstOrDefault(),
                kvp.Key);
        }

        // Build reverse [ForgeWith] mappings and emit FM0015 warnings where nested method lacks [ReverseForge]
        var reverseForgeWithMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in forwardForgeWithMappings)
        {
            var forwardDestPropName = kvp.Key;
            var forgingMethodName = kvp.Value;

            // Find the source property that maps to this dest property
            string? reverseDestPropName = null;
            if (forwardPropertyMappings.TryGetValue(forwardDestPropName, out var mappedSourceProp)
                && !mappedSourceProp.Contains(".")) // Nested paths can't be used as reverse dest properties
            {
                reverseDestPropName = mappedSourceProp;
            }
            else
            {
                reverseDestPropName = forwardDestPropName; // same name by convention
            }

            // Check if the nested forging method has [ReverseForge].
            // Use type-based matching (like FindForgingMethod) to resolve the correct overload,
            // since forgers commonly overload Forge(...) for multiple type pairs.
            // The forward nested method takes forwardSourcePropType and returns forwardDestPropType.
            var forwardDestPropType = GetMappableProperties(forwardDestType)
                .FirstOrDefault(p => p.Name == forwardDestPropName)?.Type;
            var forwardSourcePropType = reverseDestPropName != null
                ? GetMappableProperties(forwardSourceType)
                    .FirstOrDefault(p => p.Name == reverseDestPropName)?.Type
                : null;

            IMethodSymbol? nestedMethod = null;
            if (forwardSourcePropType != null && forwardDestPropType != null)
            {
                // Find the forward nested method by its type signature
                nestedMethod = forger.Symbol.GetMembers(forgingMethodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m =>
                        m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1 &&
                        SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, forwardSourcePropType) &&
                        SymbolEqualityComparer.Default.Equals(m.ReturnType, forwardDestPropType));
            }

            // Fallback: if types couldn't be resolved, try name-only matching
            nestedMethod ??= forger.Symbol.GetMembers(forgingMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1);

            if (nestedMethod != null && HasReverseForgeAttribute(nestedMethod))
            {
                // Also verify the nested method's types are actually reversible —
                // if they fail IsReversibleObjectType, no reverse overload will be generated,
                // and calling it would cause a compile error in the consumer project.
                var nestedSourceType = nestedMethod.Parameters[0].Type as INamedTypeSymbol;
                var nestedDestType = nestedMethod.ReturnType as INamedTypeSymbol;

                if (nestedSourceType != null && nestedDestType != null &&
                    IsReversibleObjectType(nestedSourceType) && IsReversibleObjectType(nestedDestType))
                {
                    reverseForgeWithMappings[reverseDestPropName!] = forgingMethodName;
                }
                else
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ForgeWithLacksReverseForge,
                        forwardMethod.Locations.FirstOrDefault(),
                        forgingMethodName);
                }
            }
            else
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeWithLacksReverseForge,
                    forwardMethod.Locations.FirstOrDefault(),
                    forgingMethodName);
            }
        }

        // FM0026: Check auto-wired properties for missing reverse forge methods
        if (_config.AutoWireNestedMappings)
        {
            // Collect ctor param names to recognize get-only ctor-backed properties
            // Only relevant when there is no parameterless public ctor, because ResolveConstructor
            // prefers parameterless ctors (object initializer pattern), making get-only props unmappable
            var ctorParamNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var forwardDestNamedType = forwardDestType as INamedTypeSymbol;
            if (forwardDestNamedType != null)
            {
                var hasParameterlessCtor = forwardDestNamedType.InstanceConstructors
                    .Any(c => c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
                if (!hasParameterlessCtor)
                {
                    foreach (var ctor in forwardDestNamedType.InstanceConstructors
                        .Where(c => c.DeclaredAccessibility == Accessibility.Public))
                    {
                        foreach (var p in ctor.Parameters)
                            ctorParamNames.Add(p.Name);
                    }
                }
            }

            var forwardDestProps = GetMappableProperties(forwardDestType)
                .Where(p => p.SetMethod != null || ctorParamNames.Contains(p.Name));  // Settable or ctor-backed
            var forwardSourceProps = GetMappableProperties(forwardSourceType);

            foreach (var destProp in forwardDestProps)
            {
                // Skip properties already handled by explicit attributes
                if (forwardIgnored.Contains(destProp.Name)) continue;
                if (forwardResolverMappings.ContainsKey(destProp.Name)) continue;
                if (forwardForgeWithMappings.ContainsKey(destProp.Name)) continue;

                // Find the matching source property by name (convention) or [ForgeProperty]
                ITypeSymbol? forwardSourcePropType = null;
                if (forwardPropertyMappings.TryGetValue(destProp.Name, out var mappedSourcePath))
                {
                    forwardSourcePropType = ResolvePathLeafType(mappedSourcePath, forwardSourceType);
                }
                else
                {
                    var matchingProp = forwardSourceProps.FirstOrDefault(sp =>
                        string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                    if (matchingProp != null)
                        forwardSourcePropType = matchingProp.Type;
                }

                if (forwardSourcePropType == null) continue;

                // Check if forward direction would auto-wire this property
                if (IsScalarType(forwardSourcePropType) || IsScalarType(destProp.Type)) continue;
                if (CanAssign(forwardSourcePropType, destProp.Type)) continue;

                var forwardCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, forwardSourcePropType, destProp.Type);
                if (forwardCandidates.Count == 1)
                {
                    // Property is auto-wired forward — check for reverse forge method
                    var reverseCandidates = FindReverseForgeMethodCandidates(forger.Symbol, destProp.Type, forwardSourcePropType);
                    if (reverseCandidates.Count == 0)
                    {
                        var forwardCandidate = forwardCandidates[0];
                        if (!HasReverseForgeAttribute(forwardCandidate))
                        {
                            ReportDiagnosticIfNotSuppressed(context,
                                DiagnosticDescriptors.AutoWiredPropertyLacksReverseForge,
                                forwardMethod.Locations.FirstOrDefault(),
                                destProp.Name);
                        }
                    }
                }
                else if (forwardCandidates.Count == 0)
                {
                    // Check if this would be resolved by inline collection auto-wiring
                    var srcElemType = GetCollectionElementType(forwardSourcePropType);
                    var destElemType = GetCollectionElementType(destProp.Type);
                    if (srcElemType != null && destElemType != null)
                    {
                        var elemForwardCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
                        if (elemForwardCandidates.Count == 1)
                        {
                            // Collection property is inline-auto-wired forward — check for reverse element method
                            var elemReverseCandidates = FindReverseForgeMethodCandidates(forger.Symbol, destElemType, srcElemType);
                            if (elemReverseCandidates.Count == 0)
                            {
                                var elemForwardCandidate = elemForwardCandidates[0];
                                if (!HasReverseForgeAttribute(elemForwardCandidate))
                                {
                                    ReportDiagnosticIfNotSuppressed(context,
                                        DiagnosticDescriptors.AutoWiredPropertyLacksReverseForge,
                                        forwardMethod.Locations.FirstOrDefault(),
                                        destProp.Name);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Now generate the reverse method body using the same GenerateForgeMethod pattern
        // but with reversed types and mappings
        var sb = new StringBuilder();
        var sourceParam = "source";

        // Method signature
        var accessibility = GetAccessibilityKeyword(forwardMethod.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} {reverseDestType.ToDisplayString()} {forwardMethod.Name}({reverseSourceType.ToDisplayString()} {sourceParam})");
        sb.AppendLine("        {");

        // Null check (for reference types and Nullable<T>)
        if (reverseSourceType.IsReferenceType ||
            reverseSourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var nullReturn = reverseDestType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam, nullReturn));
            sb.AppendLine();
        }

        // Get mappable properties
        var sourceProperties = GetMappableProperties(reverseSourceType);
        var destProperties = GetMappableProperties(reverseDestType);

        // Determine constructor strategy for the reverse destination
        var (chosenCtor, ctorParamMappings) = ResolveConstructor(
            reverseDestType, reverseSourceType, sourceProperties, reversePropertyMappings, context, forwardMethod, forger);

        // Track which destination properties are covered by constructor parameters
        var ctorCoveredDestProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ctorParamMappings != null)
        {
            foreach (var mapping in ctorParamMappings)
                ctorCoveredDestProps.Add(mapping.DestPropertyName);
        }

        // Empty resolver mappings for reverse (ForgeFrom is not reversed)
        var emptyResolverMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        // Don't pass reverseForgeWithMappings to GeneratePropertyAssignment (it can't find the reverse
        // partial method via FindForgingMethod since it's auto-generated). We'll handle them inline.
        var emptyForgeWithMappings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (chosenCtor != null && ctorParamMappings != null && chosenCtor.Parameters.Length > 0)
        {
            sb.AppendLine($"            var result = new {reverseDestType.ToDisplayString()}(");

            for (int i = 0; i < ctorParamMappings.Count; i++)
            {
                var mapping = ctorParamMappings[i];
                var separator = i < ctorParamMappings.Count - 1 ? "," : "";
                var expr = GenerateCtorParamExpression(
                    mapping.SourceExpression, mapping.SourcePropertyType, mapping.DestPropertyType,
                    mapping.DestPropertyName, reverseDestType.ToDisplayString(),
                    new Dictionary<string, int>(StringComparer.Ordinal));
                sb.AppendLine($"                {mapping.CtorParamName}: {expr}{separator}");
            }

            var remainingDestProps = destProperties
                .Where(p => p.SetMethod != null && !ctorCoveredDestProps.Contains(p.Name))
                .ToList();

            var initAssignments = new List<(string Name, string Expr)>();
            foreach (var destProp in remainingDestProps)
            {
                if (reverseIgnored.Contains(destProp.Name))
                    continue;

                var assignment = GenerateReversePropertyAssignment(
                    destProp, sourceParam, reverseSourceType, sourceProperties,
                    reversePropertyMappings, emptyResolverMappings, emptyForgeWithMappings,
                    reverseForgeWithMappings, reverseIgnored, forger, context, forwardMethod);

                if (assignment != null)
                    initAssignments.Add((destProp.Name, assignment));
            }

            if (initAssignments.Count > 0)
            {
                sb.AppendLine("            )");
                sb.AppendLine("            {");
                foreach (var (name, expr) in initAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");
            }
            else
            {
                sb.AppendLine("            );");
            }

            sb.AppendLine("            return result;");
        }
        else
        {
            sb.AppendLine($"            return new {reverseDestType.ToDisplayString()}");
            sb.AppendLine("            {");

            foreach (var destProp in destProperties)
            {
                var assignment = GenerateReversePropertyAssignment(
                    destProp, sourceParam, reverseSourceType, sourceProperties,
                    reversePropertyMappings, emptyResolverMappings, emptyForgeWithMappings,
                    reverseForgeWithMappings, reverseIgnored, forger, context, forwardMethod);

                if (assignment != null)
                    sb.AppendLine($"                {destProp.Name} = {assignment},");
            }

            sb.AppendLine("            };");
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a property assignment for a reverse method. Handles [ForgeWith] inline
    /// since the reverse forging methods aren't user-declared partial methods and can't be found
    /// via FindForgingMethod. For non-ForgeWith properties, delegates to GeneratePropertyAssignment.
    /// </summary>
    private string? GenerateReversePropertyAssignment(
        IPropertySymbol destProp,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        Dictionary<string, string> reverseForgeWithMappings,
        HashSet<string> ignoredProperties,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // [Ignore] takes precedence over all other mappings including [ForgeWith]
        if (ignoredProperties.Contains(destProp.Name))
            return null;

        // Check if this property has a reverse [ForgeWith] mapping
        if (reverseForgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
        {
            // Find the source property manually
            string? sourcePropName = null;
            if (propertyMappings.TryGetValue(destProp.Name, out var mappedSource))
                sourcePropName = mappedSource;
            else
            {
                var match = sourceProperties.FirstOrDefault(sp =>
                    string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                if (match != null)
                    sourcePropName = match.Name;
            }

            if (sourcePropName != null)
            {
                var sourcePropertyType = ResolvePathLeafType(sourcePropName, sourceType);
                if (sourcePropertyType != null)
                {
                    var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceType);
                    if (sourcePropertyType.IsReferenceType)
                    {
                        var localVarName = $"__forgeWith_{destProp.Name}";
                        var nullFallback = destProp.Type.IsValueType ? $"default({destProp.Type.ToDisplayString()})" : "null!";
                        return $"{sourceExpr} is {{ }} {localVarName} ? {forgingMethodName}({localVarName}) : {nullFallback}";
                    }
                    else
                    {
                        return $"{forgingMethodName}({sourceExpr})";
                    }
                }
            }

            return null;
        }

        // Fall through to regular property assignment
        return GeneratePropertyAssignment(
            destProp, sourceParam, sourceType, sourceProperties,
            propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method,
            new Dictionary<string, int>());
    }
}
