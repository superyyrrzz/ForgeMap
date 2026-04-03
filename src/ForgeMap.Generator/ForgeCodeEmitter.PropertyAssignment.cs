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
        List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>? skipNullAssignments = null,
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
                    skipNullAssignments.Add((destProp.Name, sourceExpr, localVar, null));
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
                // String→enum auto-conversion for [ForgeProperty] mapped properties
                if (_config.StringToEnum != 2 && IsStringToEnumPair(sourceLeafType, destProp.Type))
                {
                    var enumExpr = TryGenerateStringToEnumConversion(
                        sourceLeafType, destProp.Type, sourceExpr,
                        destProp.Name, destProp.ContainingType.Name,
                        nullPropertyHandlingOverrides,
                        context, method, skipNullAssignments, destProp);
                    if (enumExpr != null)
                        return enumExpr;
                    // null means either not applicable or SkipNull added to skipNullAssignments
                    if (skipNullAssignments != null && skipNullAssignments.Any(s => s.DestPropName == destProp.Name))
                        return null;
                }

                // Enum→string auto-conversion for [ForgeProperty] mapped properties
                if (IsEnumToStringPair(sourceLeafType, destProp.Type))
                {
                    var expr = GenerateEnumToStringExpression(sourceLeafType, sourceExpr);
                    // Handle nullable enum → non-nullable string
                    if (GetNullableUnderlyingType(sourceLeafType) != null
                        && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated)
                    {
                        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                        if (strategy == 1 && skipNullAssignments != null) // SkipNull
                        {
                            if (destProp.SetMethod?.IsInitOnly == true)
                                return $"{expr}!";
                            var localVar = "__enumVal_" + SanitizeVarName(destProp.Name);
                            skipNullAssignments.Add((destProp.Name, sourceExpr, localVar, $"{localVar}.ToString()"));
                            return null;
                        }
                        var handledExpr = ApplyNullPropertyHandlingExpression(
                            expr, destProp.Type, destProp.Name,
                            destProp.ContainingType.Name, strategy);
                        return handledExpr ?? $"{expr}!";
                    }
                    return expr;
                }

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
                    skipNullAssignments.Add((destProp.Name, sourceExprConv, localVar, null));
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

        // String→enum auto-conversion (convention path)
        if (sourceProp != null && _config.StringToEnum != 2 && IsStringToEnumPair(sourceProp.Type, destProp.Type))
        {
            var enumExpr = TryGenerateStringToEnumConversion(
                sourceProp.Type, destProp.Type, $"{sourceParam}.{sourceProp.Name}",
                destProp.Name, destProp.ContainingType.Name,
                nullPropertyHandlingOverrides,
                context, method, skipNullAssignments, destProp);
            if (enumExpr != null)
                return enumExpr;
            // null with SkipNull entry means property should be skipped
            if (skipNullAssignments != null && skipNullAssignments.Any(s => s.DestPropName == destProp.Name))
                return null;
        }

        // Enum→string auto-conversion (convention path)
        if (sourceProp != null && IsEnumToStringPair(sourceProp.Type, destProp.Type))
        {
            var expr = GenerateEnumToStringExpression(sourceProp.Type, $"{sourceParam}.{sourceProp.Name}");
            // Handle nullable enum → non-nullable string
            if (GetNullableUnderlyingType(sourceProp.Type) != null
                && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated)
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                if (strategy == 1 && skipNullAssignments != null) // SkipNull
                {
                    if (destProp.SetMethod?.IsInitOnly == true)
                        return $"{expr}!";
                    var localVar = "__enumVal_" + SanitizeVarName(destProp.Name);
                    skipNullAssignments.Add((destProp.Name, $"{sourceParam}.{sourceProp.Name}", localVar, $"{localVar}.ToString()"));
                    return null;
                }
                var handledExpr = ApplyNullPropertyHandlingExpression(
                    expr, destProp.Type, destProp.Name,
                    destProp.ContainingType.Name, strategy);
                return handledExpr ?? $"{expr}!";
            }
            return expr;
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
                    skipNullAssignments.Add((destProp.Name, flattenResult, localVar, null));
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

        // String→enum auto-conversion for constructor parameters
        if (sourcePropertyType != null && _config.StringToEnum != 2 && IsStringToEnumPair(sourcePropertyType, destPropertyType))
        {
            var isSourceNullable = sourcePropertyType.NullableAnnotation == NullableAnnotation.Annotated;
            var destEnumUnderlying = GetNullableUnderlyingType(destPropertyType) ?? destPropertyType;
            var isDestNullable = GetNullableUnderlyingType(destPropertyType) != null;

            // Nullable string → non-nullable enum: apply NullPropertyHandling before parsing
            if (isSourceNullable && !isDestNullable)
            {
                var strategy = ResolveNullPropertyHandling(destPropertyName, nullPropertyHandlingOverrides);
                if (strategy == 1) strategy = 0; // SkipNull → NullForgiving for ctor params
                var enumFqn = $"global::{destEnumUnderlying.ToDisplayString()}";
                switch (strategy)
                {
                    case 3: // ThrowException — null-check then parse
                        var nullChecked = $"({sourceExpression} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpression}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\"))";
                        if (_config.StringToEnum == 1) // TryParse
                            return $"(global::System.Enum.TryParse<{enumFqn}>({nullChecked}, true, out var __enumVal_{SanitizeVarName(destPropertyName)}) ? __enumVal_{SanitizeVarName(destPropertyName)} : default({enumFqn}))";
                        return $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {nullChecked}, true)";
                    case 2: // CoalesceToDefault — return default enum when source is null
                        if (_config.StringToEnum == 1) // TryParse
                            return $"({sourceExpression} is {{ }} __enumStr_{SanitizeVarName(destPropertyName)} && global::System.Enum.TryParse<{enumFqn}>(__enumStr_{SanitizeVarName(destPropertyName)}, true, out var __enumVal_{SanitizeVarName(destPropertyName)}) ? __enumVal_{SanitizeVarName(destPropertyName)} : default({enumFqn}))";
                        return $"({sourceExpression} is {{ }} __enumStr_{SanitizeVarName(destPropertyName)} ? ({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), __enumStr_{SanitizeVarName(destPropertyName)}, true) : default({enumFqn}))";
                    default: // NullForgiving (0) — fall through to default handler
                        break;
                }
            }
            return GenerateStringToEnumParseExpression(sourcePropertyType, destPropertyType, sourceExpression, destPropertyName);
        }

        // Enum→string auto-conversion for constructor parameters
        if (sourcePropertyType != null && IsEnumToStringPair(sourcePropertyType, destPropertyType))
        {
            var srcUnderlying = GetNullableUnderlyingType(sourcePropertyType);
            var isDestNullableStr = destPropertyType.NullableAnnotation == NullableAnnotation.Annotated;

            // Nullable enum → non-nullable string: apply NullPropertyHandling
            if (srcUnderlying != null && !isDestNullableStr)
            {
                var strategy = ResolveNullPropertyHandling(destPropertyName, nullPropertyHandlingOverrides);
                if (strategy == 1) strategy = 0; // SkipNull → NullForgiving for ctor params
                var toStrExpr = $"{sourceExpression}?.ToString()";
                switch (strategy)
                {
                    case 3: // ThrowException
                        return $"{toStrExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpression}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\")";
                    case 2: // CoalesceToDefault
                        return $"{toStrExpr} ?? \"\"";
                    default: // NullForgiving (0)
                        return $"{toStrExpr}!";
                }
            }
            return GenerateEnumToStringExpression(sourcePropertyType, sourceExpression);
        }

        return sourceExpression;
    }

    /// <summary>
    /// Tries to generate a string→enum conversion expression for a property assignment.
    /// Handles string/string? source → enum/enum? destination with NullPropertyHandling integration.
    /// Returns null if the types are not a string→enum pair, or if SkipNull applies (adds to skipNullAssignments).
    /// </summary>
    private string? TryGenerateStringToEnumConversion(
        ITypeSymbol sourceType,
        ITypeSymbol destType,
        string sourceExpr,
        string destPropertyName,
        string destTypeName,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        SourceProductionContext context,
        IMethodSymbol method,
        List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>? skipNullAssignments = null,
        IPropertySymbol? destProp = null)
    {
        if (!IsStringToEnumPair(sourceType, destType))
            return null;

        // Report FM0033 (informational, disabled by default)
        var destEnumUnderlying = GetNullableUnderlyingType(destType) ?? destType;
        var strategyName = _config.StringToEnum == 1 ? "TryParse" : "Parse";
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.StringToEnumAutoConverted,
            method.Locations.FirstOrDefault(),
            destPropertyName,
            destEnumUnderlying.ToDisplayString(),
            strategyName);

        var isSourceNullable = sourceType.NullableAnnotation == NullableAnnotation.Annotated;
        var isDestNullable = GetNullableUnderlyingType(destType) != null;
        var enumFqn = $"global::{destEnumUnderlying.ToDisplayString()}";

        // TryParse strategy (1): generates multi-statement for non-nullable dest
        string? result;
        if (_config.StringToEnum == 1) // TryParse
        {
            result = GenerateStringToEnumTryParseExpression(
                sourceExpr, enumFqn, isSourceNullable, isDestNullable,
                destPropertyName, nullPropertyHandlingOverrides);
        }
        else
        {
            // Parse strategy (0): generates inline expression
            result = GenerateStringToEnumParseExpressionWithNullHandling(
                sourceExpr, enumFqn, isSourceNullable, isDestNullable,
                destPropertyName, destTypeName, nullPropertyHandlingOverrides);
        }

        // null means SkipNull was selected — handle via skipNullAssignments for Forge methods
        if (result == null && skipNullAssignments != null && destProp != null)
        {
            if (destProp.SetMethod?.IsInitOnly == true)
            {
                if (_config.StringToEnum == 1) // TryParse
                    return $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}!, true, out var __enumVal_{SanitizeVarName(destPropertyName)}) ? __enumVal_{SanitizeVarName(destPropertyName)} : default({enumFqn}))";
                return $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}!, true)"; // init-only: fall back to NullForgiving
            }
            var localVar = "__strVal_" + SanitizeVarName(destPropertyName);
            string assignExpr;
            if (_config.StringToEnum == 1) // TryParse
                assignExpr = $"(global::System.Enum.TryParse<{enumFqn}>({localVar}, true, out var __enumParsed_{SanitizeVarName(destPropertyName)}) ? __enumParsed_{SanitizeVarName(destPropertyName)} : default({enumFqn}))";
            else // Parse
                assignExpr = $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {localVar}, true)";
            skipNullAssignments.Add((destPropertyName, sourceExpr, localVar, assignExpr));
        }

        return result;
    }

    /// <summary>
    /// Generates a string→enum Parse expression (inline, no null handling; used for ctor params).
    /// </summary>
    private string GenerateStringToEnumParseExpression(
        ITypeSymbol sourceType,
        ITypeSymbol destType,
        string sourceExpr,
        string destPropertyName)
    {
        var destEnumUnderlying = GetNullableUnderlyingType(destType) ?? destType;
        var isDestNullable = GetNullableUnderlyingType(destType) != null;
        var enumFqn = $"global::{destEnumUnderlying.ToDisplayString()}";
        var isSourceNullable = sourceType.NullableAnnotation == NullableAnnotation.Annotated;
        var varSuffix = SanitizeVarName(destPropertyName);

        if (_config.StringToEnum == 1) // TryParse — for ctor params, use inline ternary
        {
            if (isSourceNullable)
            {
                if (isDestNullable)
                    return $"({sourceExpr} is {{ }} __enumStr_{varSuffix} && global::System.Enum.TryParse<{enumFqn}>(__enumStr_{varSuffix}, true, out var __enumVal_{varSuffix}) ? ({enumFqn}?)__enumVal_{varSuffix} : null)";
                else
                    return $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}!, true, out var __enumVal_{varSuffix}) ? __enumVal_{varSuffix} : default({enumFqn}))";
            }
            else
            {
                var parseExpr = $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}, true, out var __enumVal_{varSuffix}) ? __enumVal_{varSuffix} : default({enumFqn}))";
                return isDestNullable ? $"({enumFqn}?){parseExpr}" : parseExpr;
            }
        }

        // Parse strategy
        var parseBase = $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}{(isSourceNullable ? "!" : "")}, true)";
        if (isDestNullable)
            return $"({enumFqn}?)({parseBase})";
        return parseBase;
    }

    /// <summary>
    /// Generates a Parse-strategy expression with NullPropertyHandling integration.
    /// </summary>
    private string? GenerateStringToEnumParseExpressionWithNullHandling(
        string sourceExpr,
        string enumFqn,
        bool isSourceNullable,
        bool isDestNullable,
        string destPropertyName,
        string destTypeName,
        Dictionary<string, int> nullPropertyHandlingOverrides)
    {
        if (!isSourceNullable)
        {
            // Non-nullable source: straightforward parse
            var parseExpr = $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}, true)";
            if (isDestNullable)
                return $"({enumFqn}?)({parseExpr})";
            return parseExpr;
        }

        // Nullable source: apply NullPropertyHandling
        var strategy = ResolveNullPropertyHandling(destPropertyName, nullPropertyHandlingOverrides);

        // When dest is nullable and source is nullable, null source should always map to null dest
        // regardless of NullPropertyHandling strategy (the strategy only matters for nullable→non-nullable)
        if (isDestNullable)
        {
            return $"{sourceExpr} is {{ }} __strVal_{SanitizeVarName(destPropertyName)} ? ({enumFqn}?)(({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), __strVal_{SanitizeVarName(destPropertyName)}, true)) : null";
        }

        switch (strategy)
        {
            case 0: // NullForgiving
                return $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}!, true)";

            case 1: // SkipNull — return null to signal the caller to skip the assignment
                return null;

            case 2: // CoalesceToDefault
                return $"{sourceExpr} is null ? default({enumFqn}) : ({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}, true)";

            case 3: // ThrowException
                return $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\"), true)";

            default:
                return $"({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {sourceExpr}!, true)";
        }
    }

    /// <summary>
    /// Generates a TryParse-strategy expression with NullPropertyHandling integration.
    /// </summary>
    private string? GenerateStringToEnumTryParseExpression(
        string sourceExpr,
        string enumFqn,
        bool isSourceNullable,
        bool isDestNullable,
        string destPropertyName,
        Dictionary<string, int> nullPropertyHandlingOverrides)
    {
        // TryParse uses generic Enum.TryParse<T>(string, bool, out T) available from netstandard2.0
        // Result: parsed value on success, default(T) on failure
        var varSuffix = SanitizeVarName(destPropertyName);

        if (!isSourceNullable)
        {
            // Non-nullable source: simple TryParse
            var tryExpr = $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}, true, out var __enum_{varSuffix}) ? __enum_{varSuffix} : default({enumFqn}))";
            if (isDestNullable)
                return $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}, true, out var __enum_{varSuffix}) ? ({enumFqn}?)__enum_{varSuffix} : null)";
            return tryExpr;
        }

        // Nullable source
        if (isDestNullable)
        {
            return $"({sourceExpr} is {{ }} __strVal_{varSuffix} && global::System.Enum.TryParse<{enumFqn}>(__strVal_{varSuffix}, true, out var __enum_{varSuffix}) ? ({enumFqn}?)__enum_{varSuffix} : null)";
        }

        // Nullable source → non-nullable dest: use NullPropertyHandling
        var strategy = ResolveNullPropertyHandling(destPropertyName, nullPropertyHandlingOverrides);
        switch (strategy)
        {
            case 0: // NullForgiving — try parse the potentially-null string
                return $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}!, true, out var __enum_{varSuffix}) ? __enum_{varSuffix} : default({enumFqn}))";

            case 1: // SkipNull — return null to signal the caller to skip the assignment
                return null;

            case 2: // CoalesceToDefault
                return $"({sourceExpr} is {{ }} __strVal_{varSuffix} && global::System.Enum.TryParse<{enumFqn}>(__strVal_{varSuffix}, true, out var __enum_{varSuffix}) ? __enum_{varSuffix} : default({enumFqn}))";

            case 3: // ThrowException
                return $"({sourceExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\")) is {{ }} __strVal_{varSuffix} && global::System.Enum.TryParse<{enumFqn}>(__strVal_{varSuffix}, true, out var __enum_{varSuffix}) ? __enum_{varSuffix} : default({enumFqn})";

            default:
                return $"(global::System.Enum.TryParse<{enumFqn}>({sourceExpr}!, true, out var __enum_{varSuffix}) ? __enum_{varSuffix} : default({enumFqn}))";
        }
    }

    /// <summary>
    /// Generates an enum→string conversion expression (source.Prop.ToString()).
    /// Handles Nullable&lt;enum&gt; source by using ?.ToString().
    /// </summary>
    private static string GenerateEnumToStringExpression(ITypeSymbol sourceType, string sourceExpr)
    {
        var srcUnderlying = GetNullableUnderlyingType(sourceType);
        if (srcUnderlying != null)
        {
            // Nullable<Enum> → string: use ?.ToString()
            return $"{sourceExpr}?.ToString()";
        }
        return $"{sourceExpr}.ToString()";
    }

    /// <summary>
    /// Sanitizes an expression for use as a C# variable name suffix.
    /// </summary>
    private static string SanitizeVarName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        return sb.ToString();
    }
}
