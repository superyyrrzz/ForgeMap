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
    /// Generates a property assignment expression string, or null if the property should be skipped.
    /// When NullPropertyHandling.SkipNull applies, returns null and adds the property to <paramref name="skipNullAssignments"/>.
    /// When a multi-statement collection auto-wire applies, returns null and adds to <paramref name="postConstructionCollections"/>.
    /// </summary>
    private string? GeneratePropertyAssignment(
        IPropertySymbol destProp,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        HashSet<string> ignoredProperties,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        List<(string DestPropName, string SourceExpr, string LocalVarName)>? skipNullAssignments = null,
        List<(string DestPropName, string Block)>? postConstructionCollections = null,
        List<string>? preConstructionBlocks = null)
    {
        // Skip ignored properties
        if (ignoredProperties.Contains(destProp.Name))
            return null;

        // Check if this property has a resolver from [ForgeFrom]
        if (resolverMappings.TryGetValue(destProp.Name, out var resolverMethodName))
        {
            return GenerateResolverExpression(
                destProp, resolverMethodName, sourceParam, sourceType,
                sourceProperties, propertyMappings, forger, context, method);
        }

        // Check if this property has a [ForgeWith] mapping for nested object forging
        if (forgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
        {
            return GenerateForgeWithExpression(
                destProp, forgingMethodName, sourceParam, sourceType,
                sourceProperties, propertyMappings, forger, context, method);
        }

        // Check if this property has a mapping from [ForgeProperty]
        if (propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
        {
            var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceType);
            var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceType);
            // When null-conditional lifts a non-nullable value type to Nullable<T>, cast back
            var isLiftedValueType = hasNullConditional && sourceLeafType != null && sourceLeafType.IsValueType && GetNullableUnderlyingType(sourceLeafType) == null;
            // Try compatible enum cast first — pass isLifted so it generates correct nullable handling
            if (sourceLeafType != null)
            {
                var enumCast = TryGenerateCompatibleEnumCast(sourceLeafType, destProp.Type, sourceExpr, isLifted: isLiftedValueType);
                if (enumCast != null)
                    return enumCast;
            }
            if (isLiftedValueType && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
                return $"({destProp.Type.ToDisplayString()})({sourceExpr})!";

            // Check for nullable ref → non-nullable ref mismatch
            if (sourceLeafType != null && IsNullableToNonNullableReferenceType(sourceLeafType, destProp.Type))
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                ReportFM0007(context, method, sourceType.Name, sourcePropName, destProp.ContainingType.Name, destProp.Name);
                if (strategy == 1 && skipNullAssignments != null) // SkipNull
                {
                    // Init-only properties cannot be assigned after initialization; fall back to NullForgiving
                    if (destProp.SetMethod?.IsInitOnly == true)
                        return $"{sourceExpr}!";
                    var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                    skipNullAssignments.Add((destProp.Name, sourceExpr, localVar));
                    return null;
                }
                var handledExpr = ApplyNullPropertyHandlingExpression(
                    sourceExpr, destProp.Type, destProp.Name,
                    destProp.ContainingType.Name, strategy);
                if (handledExpr != null) return handledExpr;
                return $"{sourceExpr}!"; // Final fallback
            }

            // Handle null-conditional on reference types (non-nullable ref mismatch not detected — e.g., oblivious types)
            var nullForgiving = hasNullConditional && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";

            // If the leaf type is not directly assignable, try auto-wiring via forge method
            if (sourceLeafType != null && !CanAssign(sourceLeafType, destProp.Type)
                && !IsCompatibleEnumPair(sourceLeafType, destProp.Type))
            {
                if (_config.AutoWireNestedMappings)
                {
                    // Try inline collection auto-wire first
                    var collResult = TryAutoWireCollectionInline(
                        destProp, sourceLeafType, sourceExpr,
                        forger, context, method,
                        nullPropertyHandlingOverrides,
                        postConstructionCollections, preConstructionBlocks);
                    if (collResult != null)
                        return collResult;

                    var autoWireResult = TryAutoWireForgeMethod(
                        destProp, sourceLeafType, sourceExpr,
                        forger, context, method);
                    if (autoWireResult != null)
                        return autoWireResult;
                }
                // Types aren't assignable and auto-wire didn't resolve — fall through to unmapped (FM0006)
                return null;
            }

            return $"{sourceExpr}{nullForgiving}";
        }

        // Try to find matching source property by name (convention)
        var sourceProp = sourceProperties.FirstOrDefault(sp =>
            string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));

        if (sourceProp != null && CanAssign(sourceProp.Type, destProp.Type))
        {
            if (IsNullableToNonNullableValueType(sourceProp.Type, destProp.Type))
                return $"({destProp.Type.ToDisplayString()}){sourceParam}.{sourceProp.Name}!";

            // Check for nullable ref → non-nullable ref mismatch
            if (IsNullableToNonNullableReferenceType(sourceProp.Type, destProp.Type))
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                ReportFM0007(context, method, sourceType.Name, sourceProp.Name, destProp.ContainingType.Name, destProp.Name);
                var sourceExprConv = $"{sourceParam}.{sourceProp.Name}";
                if (strategy == 1 && skipNullAssignments != null) // SkipNull
                {
                    // Init-only properties cannot be assigned after initialization; fall back to NullForgiving
                    if (destProp.SetMethod?.IsInitOnly == true)
                        return $"{sourceExprConv}!";
                    var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                    skipNullAssignments.Add((destProp.Name, sourceExprConv, localVar));
                    return null;
                }
                var handledExpr = ApplyNullPropertyHandlingExpression(
                    sourceExprConv, destProp.Type, destProp.Name,
                    destProp.ContainingType.Name, strategy);
                if (handledExpr != null) return handledExpr;
                return $"{sourceExprConv}!"; // Final fallback
            }

            return $"{sourceParam}.{sourceProp.Name}";
        }

        // Compatible enum cast: EnumA -> EnumB (different namespaces, same members)
        if (sourceProp != null)
        {
            var enumCastExpr = TryGenerateCompatibleEnumCast(sourceProp.Type, destProp.Type, $"{sourceParam}.{sourceProp.Name}");
            if (enumCastExpr != null)
                return enumCastExpr;
        }

        // Try automatic flattening: destProp "CustomerName" → source.Customer.Name
        var flattenResult = TryAutoFlatten(destProp, sourceParam, sourceType, out var flattenLeafType);
        if (flattenResult != null)
        {
            // Check for nullable ref → non-nullable ref mismatch on auto-flattened result
            if (flattenLeafType != null && IsNullableToNonNullableReferenceType(flattenLeafType, destProp.Type))
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                // Extract source property path from flattened expression (e.g., "source.Customer?.Name" → "Customer?.Name")
                var flattenSourcePropName = flattenResult.StartsWith(sourceParam + ".")
                    ? flattenResult.Substring(sourceParam.Length + 1)
                    : flattenResult.StartsWith(sourceParam + "?.")
                        ? flattenResult.Substring(sourceParam.Length + 2)
                        : destProp.Name;
                ReportFM0007(context, method, sourceType.Name, flattenSourcePropName, destProp.ContainingType.Name, destProp.Name);
                if (strategy == 1 && skipNullAssignments != null) // SkipNull
                {
                    if (destProp.SetMethod?.IsInitOnly == true)
                        return $"{flattenResult}!";
                    var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                    skipNullAssignments.Add((destProp.Name, flattenResult, localVar));
                    return null;
                }
                var handledExpr = ApplyNullPropertyHandlingExpression(
                    flattenResult, destProp.Type, destProp.Name,
                    destProp.ContainingType.Name, strategy);
                if (handledExpr != null) return handledExpr;
                return $"{flattenResult}!";
            }
            return flattenResult;
        }

        // Auto-wire: search for matching forge methods for nested complex properties
        if (_config.AutoWireNestedMappings && sourceProp != null)
        {
            // Try inline collection auto-wire first
            var collResult = TryAutoWireCollectionInline(
                destProp, sourceProp.Type, $"{sourceParam}.{sourceProp.Name}",
                forger, context, method,
                nullPropertyHandlingOverrides,
                postConstructionCollections, preConstructionBlocks);
            if (collResult != null)
                return collResult;

            var autoWireResult = TryAutoWireForgeMethod(
                destProp, sourceProp.Type, $"{sourceParam}.{sourceProp.Name}",
                forger, context, method);
            if (autoWireResult != null)
                return autoWireResult;
        }

        return null;
    }

    /// <summary>
    /// Generates a resolver expression for [ForgeFrom].
    /// </summary>
    private string? GenerateResolverExpression(
        IPropertySymbol destProp,
        string resolverMethodName,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        string? sourcePropPath = null;
        ITypeSymbol? sourcePathLeafType = null;

        if (propertyMappings.TryGetValue(destProp.Name, out sourcePropPath))
        {
            sourcePathLeafType = ResolvePathLeafType(sourcePropPath, sourceType);
        }
        else
        {
            var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
            if (matchingSourceProp != null)
            {
                sourcePropPath = matchingSourceProp.Name;
                sourcePathLeafType = matchingSourceProp.Type;
            }
        }

        var resolverMethod = FindResolverMethod(forger.Symbol, resolverMethodName, sourceType, sourcePathLeafType);

        if (resolverMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ResolverMethodNotFound,
                method.Locations.FirstOrDefault(),
                resolverMethodName);
            return null;
        }

        var resolverParamType = resolverMethod.Parameters[0].Type;

        if (SymbolEqualityComparer.Default.Equals(resolverParamType, sourceType) ||
            CanAssign(sourceType, resolverParamType))
        {
            return $"{resolverMethod.Name}({sourceParam})";
        }
        else if (sourcePropPath != null && sourcePathLeafType != null &&
                 CanAssign(sourcePathLeafType, resolverParamType))
        {
            var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceType);
            var isLiftedValueType = hasNullConditional && sourcePathLeafType.IsValueType && GetNullableUnderlyingType(sourcePathLeafType) == null;
            if (IsNullableToNonNullableValueType(sourcePathLeafType, resolverParamType) || isLiftedValueType)
            {
                return $"{resolverMethod.Name}(({resolverParamType.ToDisplayString()}){sourceExpr}!)";
            }
            else
            {
                var isNullableExpr = hasNullConditional || sourcePathLeafType.NullableAnnotation == NullableAnnotation.Annotated;
                var nullForgiving = isNullableExpr && resolverParamType.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                return $"{resolverMethod.Name}({sourceExpr}{nullForgiving})";
            }
        }
        else
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.InvalidResolverSignature,
                method.Locations.FirstOrDefault(),
                resolverMethodName);
            return null;
        }
    }

    /// <summary>
    /// Generates a [ForgeWith] expression for nested object forging.
    /// </summary>
    private string? GenerateForgeWithExpression(
        IPropertySymbol destProp,
        string forgingMethodName,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        string? forgeWithSourcePropName = null;
        if (propertyMappings.TryGetValue(destProp.Name, out var mappedSource))
        {
            forgeWithSourcePropName = mappedSource;
        }
        else
        {
            var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
            if (matchingSourceProp != null)
                forgeWithSourcePropName = matchingSourceProp.Name;
        }

        if (forgeWithSourcePropName != null)
        {
            var sourcePropertyType = ResolvePathLeafType(forgeWithSourcePropName, sourceType);
            if (sourcePropertyType != null)
            {
                var nestedForgeMethod = FindForgingMethod(forger.Symbol, forgingMethodName, sourcePropertyType, destProp.Type);
                if (nestedForgeMethod != null)
                {
                    var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, forgeWithSourcePropName, sourceType);
                    if (sourcePropertyType.IsReferenceType)
                    {
                        var localVarName = $"__forgeWith_{destProp.Name}";
                        var nullFallback = destProp.Type.IsValueType ? "default" : "null!";
                        return $"{sourceExpr} is {{ }} {localVarName} ? {nestedForgeMethod.Name}({localVarName}) : {nullFallback}";
                    }
                    else
                    {
                        return $"{nestedForgeMethod.Name}({sourceExpr})";
                    }
                }
            }
        }

        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.ResolverMethodNotFound,
            method.Locations.FirstOrDefault(),
            forgingMethodName);
        return null;
    }

    /// <summary>
    /// Generates a mapping expression for a constructor parameter, handling Nullable&lt;T&gt; to T.
    /// </summary>
    private string GenerateCtorParamExpression(
        string sourceExpression,
        ITypeSymbol? sourcePropertyType,
        ITypeSymbol destPropertyType,
        string destPropertyName,
        string destTypeName,
        Dictionary<string, int> nullPropertyHandlingOverrides)
    {
        if (sourcePropertyType != null && IsNullableToNonNullableValueType(sourcePropertyType, destPropertyType))
            return sourceExpression.Contains("?.") ? $"({destPropertyType.ToDisplayString()})({sourceExpression})!" : $"({destPropertyType.ToDisplayString()}){sourceExpression}!";

        // Handle lifted value type from null-conditional: source.Customer?.Age is int?
        // even though Age is int — cast back to the destination type
        var isLifted = sourceExpression.Contains("?.") && sourcePropertyType != null
            && sourcePropertyType.IsValueType && GetNullableUnderlyingType(sourcePropertyType) == null;

        if (isLifted && destPropertyType.IsValueType && GetNullableUnderlyingType(destPropertyType) == null)
        {
            // Try compatible enum cast first — it needs the isLifted flag for correct codegen
            var enumCast = TryGenerateCompatibleEnumCast(sourcePropertyType!, destPropertyType, sourceExpression, isLifted: true);
            if (enumCast != null)
                return enumCast;
            return $"({destPropertyType.ToDisplayString()})({sourceExpression})!";
        }

        // Compatible enum cast for constructor parameters
        if (sourcePropertyType != null)
        {
            var enumCastExpr = TryGenerateCompatibleEnumCast(sourcePropertyType, destPropertyType, sourceExpression, isLifted: isLifted);
            if (enumCastExpr != null)
                return enumCastExpr;
        }

        // Handle nullable ref → non-nullable ref for constructor parameters
        // Apply strategy: SkipNull falls back to NullForgiving (ctor params can't be skipped)
        if (sourcePropertyType != null && IsNullableToNonNullableReferenceType(sourcePropertyType, destPropertyType))
        {
            var strategy = ResolveNullPropertyHandling(destPropertyName, nullPropertyHandlingOverrides);
            // SkipNull (1) is not applicable for ctor params — fall back to NullForgiving
            if (strategy == 1)
                strategy = 0;
            return ApplyNullPropertyHandlingExpression(sourceExpression, destPropertyType, destPropertyName, destTypeName, strategy)
                   ?? $"{sourceExpression}!"; // fallback if ApplyNullPropertyHandlingExpression returns null (SkipNull)
        }

        return sourceExpression;
    }
}
