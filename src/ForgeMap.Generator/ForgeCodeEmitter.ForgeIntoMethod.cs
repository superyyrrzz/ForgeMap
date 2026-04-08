using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private string GenerateForgeIntoMethod(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        INamedTypeSymbol? destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        if (destinationType == null)
            return string.Empty;

        var sourceNamedType = sourceType as INamedTypeSymbol;
        if (sourceNamedType == null)
            return string.Empty;

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;
        var destParam = GetUseExistingValueParameter(method)!.Name;

        // Resolve all attribute-based configuration
        var cfg = ResolveMethodConfig(method, sourceType, destinationType, forger, context);
        var ignoredProperties = cfg.IgnoredProperties;
        var propertyMappings = cfg.PropertyMappings;
        var resolverMappings = cfg.ResolverMappings;
        var forgeWithMappings = cfg.ForgeWithMappings;
        var beforeForgeHooks = cfg.BeforeForgeHooks;
        var afterForgeHooks = cfg.AfterForgeHooks;
        var nullPropertyHandlingOverrides = cfg.NullPropertyHandlingOverrides;
        var existingTargetProperties = cfg.ExistingTargetProperties;

        // Method signature
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} partial void {method.Name}({sourceType.ToDisplayString()} {sourceParam}, {destinationType.ToDisplayString()} {destParam})");
        sb.AppendLine("        {");

        // Null checks
        sb.AppendLine($"            if ({destParam} == null) throw new global::System.ArgumentNullException(nameof({destParam}));");
        if (sourceType.IsReferenceType || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            sb.AppendLine(GenerateNullCheck(sourceParam, null));
        }
        sb.AppendLine();

        // [BeforeForge] callbacks
        foreach (var hookName in beforeForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam});");
        }
        if (beforeForgeHooks.Count > 0)
            sb.AppendLine();

        // Get mappable properties
        var sourceProperties = GetMappableProperties(sourceNamedType);
        var destProperties = GetMappableProperties(destinationType).Where(p => p.SetMethod != null && !p.SetMethod.IsInitOnly && p.SetMethod.DeclaredAccessibility >= Accessibility.Internal);

        foreach (var destProp in destProperties)
        {
            GenerateForgeIntoPropertyAssignment(
                sb, destProp, sourceParam, destParam, sourceType, sourceNamedType,
                sourceProperties, ignoredProperties, existingTargetProperties,
                propertyMappings, resolverMappings, forgeWithMappings,
                nullPropertyHandlingOverrides, forger, context, method);
        }

        // [AfterForge] callbacks
        if (afterForgeHooks.Count > 0)
            sb.AppendLine();
        foreach (var hookName in afterForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam}, {destParam});");
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates the assignment code for a single destination property in a ForgeInto method.
    /// Handles the full priority chain: ExistingTarget → ForgeFrom → ForgeWith → ForgeProperty → auto-wire.
    /// </summary>
    private void GenerateForgeIntoPropertyAssignment(
        StringBuilder sb,
        IPropertySymbol destProp,
        string sourceParam,
        string destParam,
        ITypeSymbol sourceType,
        INamedTypeSymbol sourceNamedType,
        IEnumerable<IPropertySymbol> sourceProperties,
        HashSet<string> ignoredProperties,
        Dictionary<string, ExistingTargetConfig> existingTargetProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // Skip ignored properties
        if (ignoredProperties.Contains(destProp.Name))
            return;

        // Handle ExistingTarget properties — update nested object in place
        if (existingTargetProperties.TryGetValue(destProp.Name, out var existingTargetCfg))
        {
            var etBlock = GenerateExistingTargetBlock(
                destProp, existingTargetCfg, sourceParam, destParam,
                sourceProperties, sourceNamedType, propertyMappings,
                nullPropertyHandlingOverrides, forger, context, method);
            if (etBlock != null)
            {
                sb.AppendLine(etBlock);
                return;
            }
            // null means scalar or Replace collection — fall through to normal assignment
        }

        // Check if this property has a resolver from [ForgeFrom]
        if (resolverMappings.TryGetValue(destProp.Name, out var resolverMethodName))
        {
            // Find the source property path for this destination (if mapped via [ForgeProperty])
            string? sourcePropPath = null;
            ITypeSymbol? sourcePathLeafType = null;

            if (propertyMappings.TryGetValue(destProp.Name, out sourcePropPath))
            {
                // Resolve the leaf type for the full path (handles nested paths like "Customer.Name")
                sourcePathLeafType = ResolvePathLeafType(sourcePropPath, sourceNamedType);
            }
            else
            {
                // Try to find by same name
                var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                    string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                if (matchingSourceProp != null)
                {
                    sourcePropPath = matchingSourceProp.Name;
                    sourcePathLeafType = matchingSourceProp.Type;
                }
            }

            // Find the resolver method - pass leaf type directly for proper overload selection
            var resolverMethod = FindResolverMethod(forger.Symbol, resolverMethodName, sourceType, sourcePathLeafType);

            if (resolverMethod == null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ResolverMethodNotFound,
                    method.Locations.FirstOrDefault(),
                    resolverMethodName);
                return;
            }

            var resolverParamType = resolverMethod.Parameters[0].Type;
            string resolverCall;

            if (SymbolEqualityComparer.Default.Equals(resolverParamType, sourceType) ||
                CanAssign(sourceType, resolverParamType))
            {
                resolverCall = $"{resolverMethod.Name}({sourceParam})";
            }
            else if (sourcePropPath != null && sourcePathLeafType != null &&
                     CanAssign(sourcePathLeafType, resolverParamType))
            {
                // Pass the source property value - use null-info to handle nullable expressions
                var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceNamedType);

                // Handle Nullable<T> to T conversion with explicit cast
                // Also handle lifted value types from null-conditional (e.g., source.Customer?.Age becomes int?)
                var isLiftedValueType = hasNullConditional && sourcePathLeafType.IsValueType && GetNullableUnderlyingType(sourcePathLeafType) == null;
                if (IsNullableToNonNullableValueType(sourcePathLeafType, resolverParamType) || isLiftedValueType)
                {
                    resolverCall = $"{resolverMethod.Name}(({resolverParamType.ToDisplayString()}){sourceExpr}!)";
                }
                else
                {
                    // Add null-forgiving if expression is nullable (from null-conditional or nullable ref type)
                    // but resolver param is non-nullable
                    var isNullableExpr = hasNullConditional || sourcePathLeafType.NullableAnnotation == NullableAnnotation.Annotated;
                    var nullForgiving = isNullableExpr && resolverParamType.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                    resolverCall = $"{resolverMethod.Name}({sourceExpr}{nullForgiving})";
                }
            }
            else
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.InvalidResolverSignature,
                    method.Locations.FirstOrDefault(),
                    resolverMethodName);
                return;
            }

            sb.AppendLine($"            {destParam}.{destProp.Name} = {resolverCall};");
            return;
        }

        // Check if this property has a [ForgeWith] mapping for nested object forging
        if (forgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
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
                var sourcePropertyType = ResolvePathLeafType(forgeWithSourcePropName, sourceNamedType);
                if (sourcePropertyType != null)
                {
                    var nestedForgeMethod = FindForgingMethod(forger.Symbol, forgingMethodName, sourcePropertyType, destProp.Type);
                    if (nestedForgeMethod != null)
                    {
                        var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, forgeWithSourcePropName, sourceNamedType);
                        if (sourcePropertyType.IsReferenceType)
                        {
                            var localVarName = $"__forgeWith_{destProp.Name}";
                            sb.AppendLine($"            if ({sourceExpr} is {{ }} {localVarName})");
                            sb.AppendLine($"                {destParam}.{destProp.Name} = {nestedForgeMethod.Name}({localVarName});");
                            sb.AppendLine($"            else");
                            string nullAssign;
                            if (destProp.Type.IsValueType)
                            {
                                nullAssign = "default";
                            }
                            else
                            {
                                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                                if (strategy == 4) // CoalesceToNew
                                {
                                    ValidateCoalesceToNew(destProp.Type, context, method);
                                    var newExpr = GenerateCoalesceNewExpression(destProp.Type, method);
                                    nullAssign = newExpr ?? "null!";
                                }
                                else
                                {
                                    nullAssign = "null!";
                                }
                            }
                            sb.AppendLine($"                {destParam}.{destProp.Name} = {nullAssign};");
                        }
                        else
                        {
                            sb.AppendLine($"            {destParam}.{destProp.Name} = {nestedForgeMethod.Name}({sourceExpr});");
                        }
                        return;
                    }
                }
            }

            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ResolverMethodNotFound,
                method.Locations.FirstOrDefault(),
                forgingMethodName);
            return;
        }

        // Check if this property has a mapping from [ForgeProperty]
        if (propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
        {
            var (sourceExpr2, hasNullConditional2) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceNamedType);
            var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceNamedType);
            // When null-conditional lifts a non-nullable value type to Nullable<T>, cast back
            var isLiftedValueType = hasNullConditional2 && sourceLeafType != null && sourceLeafType.IsValueType && GetNullableUnderlyingType(sourceLeafType) == null;
            // Try compatible enum cast first — pass isLifted so it generates correct nullable handling
            if (sourceLeafType != null)
            {
                var enumCast = TryGenerateCompatibleEnumCast(sourceLeafType, destProp.Type, sourceExpr2, isLifted: isLiftedValueType);
                if (enumCast != null)
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {enumCast};");
                    return;
                }
            }
            if (isLiftedValueType && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
            {
                sb.AppendLine($"            {destParam}.{destProp.Name} = ({destProp.Type.ToDisplayString()})({sourceExpr2})!;");
            }
            else if (sourceLeafType != null && IsNullableToNonNullableReferenceType(sourceLeafType, destProp.Type))
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                ReportFM0007(context, method, sourceNamedType.Name, sourcePropName, destProp.ContainingType.Name, destProp.Name);
                if (strategy == 4) ValidateCoalesceToNew(destProp.Type, context, method);
                if (strategy == 1) // SkipNull
                {
                    var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                    sb.AppendLine($"            if ({sourceExpr2} is {{ }} {localVar})");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                {destParam}.{destProp.Name} = {localVar};");
                    sb.AppendLine($"            }}");
                }
                else
                {
                    var handledExpr = ApplyNullPropertyHandlingExpression(
                        sourceExpr2, destProp.Type, destProp.Name,
                        destProp.ContainingType.Name, strategy, method);
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {handledExpr ?? $"{sourceExpr2}!"};");
                }
            }
            else
            {
                // Check if leaf type is assignable first
                if (sourceLeafType != null && !CanAssign(sourceLeafType, destProp.Type)
                    && !IsCompatibleEnumPair(sourceLeafType, destProp.Type)
                    && !(_config.StringToEnum != 2 && IsStringToEnumPair(sourceLeafType, destProp.Type))
                    && !IsEnumToStringPair(sourceLeafType, destProp.Type))
                {
                    // Try auto-wire for non-assignable leaf types
                    if (_config.AutoWireNestedMappings)
                    {
                        // Try inline collection auto-wire first
                        var collBlock = TryAutoWireCollectionInlineStatements(
                            destProp, sourceLeafType, sourceExpr2,
                            destParam, forger, context, method, nullPropertyHandlingOverrides);
                        if (collBlock != null)
                        {
                            sb.AppendLine(collBlock);
                        }
                        else
                        {
                            var autoWireResult = TryAutoWireForgeMethod(
                                destProp, sourceLeafType, sourceExpr2,
                                forger, context, method, nullPropertyHandlingOverrides);
                            if (autoWireResult != null)
                                sb.AppendLine($"            {destParam}.{destProp.Name} = {autoWireResult};");
                        }
                    }
                    // If auto-wire didn't resolve, skip — property stays unmapped
                }
                else
                {
                    // String→enum conversion for explicit [ForgeProperty] path
                    if (sourceLeafType != null && _config.StringToEnum != 2 && IsStringToEnumPair(sourceLeafType, destProp.Type))
                    {
                        // Check if SkipNull applies for nullable source → non-nullable enum
                        if (sourceLeafType.NullableAnnotation == NullableAnnotation.Annotated
                            && GetNullableUnderlyingType(destProp.Type) == null)
                        {
                            var strategy2 = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                            if (strategy2 == 1) // SkipNull — wrap with if-guard
                            {
                                var localVar = "__strVal_" + SanitizeVarName(destProp.Name);
                                sb.AppendLine($"            if ({sourceExpr2} is {{ }} {localVar})");
                                sb.AppendLine($"            {{");
                                // Generate conversion using the non-nullable local var
                                var innerExpr = TryGenerateStringToEnumConversion(
                                    sourceLeafType.WithNullableAnnotation(NullableAnnotation.NotAnnotated), destProp.Type, localVar,
                                    destProp.Name, destProp.ContainingType.Name,
                                    nullPropertyHandlingOverrides, context, method);
                                if (innerExpr != null)
                                    sb.AppendLine($"                {destParam}.{destProp.Name} = {innerExpr};");
                                sb.AppendLine($"            }}");
                                return;
                            }
                        }

                        var enumConvExpr = TryGenerateStringToEnumConversion(
                            sourceLeafType, destProp.Type, sourceExpr2,
                            destProp.Name, destProp.ContainingType.Name,
                            nullPropertyHandlingOverrides, context, method);
                        if (enumConvExpr != null)
                        {
                            sb.AppendLine($"            {destParam}.{destProp.Name} = {enumConvExpr};");
                            return;
                        }
                    }

                    // Enum→string conversion for explicit [ForgeProperty] path
                    if (sourceLeafType != null && IsEnumToStringPair(sourceLeafType, destProp.Type))
                    {
                        var enumStrExpr = GenerateEnumToStringExpression(sourceLeafType, sourceExpr2);
                        // Handle nullable enum → non-nullable string
                        if (GetNullableUnderlyingType(sourceLeafType) != null
                            && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated)
                        {
                            var strategy2 = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                            if (strategy2 == 1) // SkipNull
                            {
                                var localVar = "__enumStr_" + destProp.Name;
                                sb.AppendLine($"            if ({enumStrExpr} is {{ }} {localVar})");
                                sb.AppendLine($"            {{");
                                sb.AppendLine($"                {destParam}.{destProp.Name} = {localVar};");
                                sb.AppendLine($"            }}");
                            }
                            else
                            {
                                var handledExpr = ApplyNullPropertyHandlingExpression(
                                    enumStrExpr, destProp.Type, destProp.Name,
                                    destProp.ContainingType.Name, strategy2, method);
                                sb.AppendLine($"            {destParam}.{destProp.Name} = {handledExpr ?? $"{enumStrExpr}!"};");
                            }
                        }
                        else
                        {
                            sb.AppendLine($"            {destParam}.{destProp.Name} = {enumStrExpr};");
                        }
                        return;
                    }

                    // Add null-forgiving operator if we used null-conditional and dest is non-nullable
                    var nullForgiving = hasNullConditional2 && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {sourceExpr2}{nullForgiving};");
                }
            }
            return;
        }

        var sourceProp = sourceProperties.FirstOrDefault(sp =>
            string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));

        if (sourceProp != null && CanAssign(sourceProp.Type, destProp.Type))
        {
            // Handle Nullable<T> to T conversion using explicit cast which throws if null
            if (IsNullableToNonNullableValueType(sourceProp.Type, destProp.Type))
            {
                sb.AppendLine($"            {destParam}.{destProp.Name} = ({destProp.Type.ToDisplayString()}){sourceParam}.{sourceProp.Name}!;");
            }
            else if (IsNullableToNonNullableReferenceType(sourceProp.Type, destProp.Type))
            {
                var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                ReportFM0007(context, method, sourceNamedType.Name, sourceProp.Name, destProp.ContainingType.Name, destProp.Name);
                var sourceExprConv = $"{sourceParam}.{sourceProp.Name}";
                if (strategy == 4) ValidateCoalesceToNew(destProp.Type, context, method);
                if (strategy == 1) // SkipNull
                {
                    var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                    sb.AppendLine($"            if ({sourceExprConv} is {{ }} {localVar})");
                    sb.AppendLine($"            {{");
                    sb.AppendLine($"                {destParam}.{destProp.Name} = {localVar};");
                    sb.AppendLine($"            }}");
                }
                else
                {
                    var handledExpr = ApplyNullPropertyHandlingExpression(
                        sourceExprConv, destProp.Type, destProp.Name,
                        destProp.ContainingType.Name, strategy, method);
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {handledExpr ?? $"{sourceExprConv}!"};");
                }
            }
            else
            {
                sb.AppendLine($"            {destParam}.{destProp.Name} = {sourceParam}.{sourceProp.Name};");
            }
        }
        else if (sourceProp != null)
        {
            // Compatible enum cast: EnumA -> EnumB (different namespaces, same members)
            var enumCastExpr = TryGenerateCompatibleEnumCast(sourceProp.Type, destProp.Type, $"{sourceParam}.{sourceProp.Name}");
            if (enumCastExpr != null)
                sb.AppendLine($"            {destParam}.{destProp.Name} = {enumCastExpr};");
            // String→enum auto-conversion
            else if (_config.StringToEnum != 2 && IsStringToEnumPair(sourceProp.Type, destProp.Type))
            {
                var srcExpr = $"{sourceParam}.{sourceProp.Name}";
                // Check if SkipNull applies for nullable source → non-nullable enum
                if (sourceProp.Type.NullableAnnotation == NullableAnnotation.Annotated
                    && GetNullableUnderlyingType(destProp.Type) == null)
                {
                    var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                    if (strategy == 1) // SkipNull — wrap with if-guard
                    {
                        var localVar = "__strVal_" + SanitizeVarName(destProp.Name);
                        sb.AppendLine($"            if ({srcExpr} is {{ }} {localVar})");
                        sb.AppendLine($"            {{");
                        var innerExpr = TryGenerateStringToEnumConversion(
                            sourceProp.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated), destProp.Type, localVar,
                            destProp.Name, destProp.ContainingType.Name,
                            nullPropertyHandlingOverrides, context, method);
                        if (innerExpr != null)
                            sb.AppendLine($"                {destParam}.{destProp.Name} = {innerExpr};");
                        sb.AppendLine($"            }}");
                    }
                    else
                    {
                        var enumConvExpr = TryGenerateStringToEnumConversion(
                            sourceProp.Type, destProp.Type, srcExpr,
                            destProp.Name, destProp.ContainingType.Name,
                            nullPropertyHandlingOverrides, context, method);
                        if (enumConvExpr != null)
                            sb.AppendLine($"            {destParam}.{destProp.Name} = {enumConvExpr};");
                    }
                }
                else
                {
                    var enumConvExpr = TryGenerateStringToEnumConversion(
                        sourceProp.Type, destProp.Type, srcExpr,
                        destProp.Name, destProp.ContainingType.Name,
                        nullPropertyHandlingOverrides, context, method);
                    if (enumConvExpr != null)
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {enumConvExpr};");
                }
            }
            // Enum→string auto-conversion
            else if (IsEnumToStringPair(sourceProp.Type, destProp.Type))
            {
                var enumStrExpr = GenerateEnumToStringExpression(sourceProp.Type, $"{sourceParam}.{sourceProp.Name}");
                // Handle nullable enum → non-nullable string
                if (GetNullableUnderlyingType(sourceProp.Type) != null
                    && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated)
                {
                    var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                    if (strategy == 1) // SkipNull
                    {
                        var localVar = "__enumStr_" + destProp.Name;
                        sb.AppendLine($"            if ({enumStrExpr} is {{ }} {localVar})");
                        sb.AppendLine($"            {{");
                        sb.AppendLine($"                {destParam}.{destProp.Name} = {localVar};");
                        sb.AppendLine($"            }}");
                    }
                    else
                    {
                        var handledExpr = ApplyNullPropertyHandlingExpression(
                            enumStrExpr, destProp.Type, destProp.Name,
                            destProp.ContainingType.Name, strategy);
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {handledExpr ?? $"{enumStrExpr}!"};");
                    }
                }
                else
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {enumStrExpr};");
                }
            }
            else if (_config.AutoWireNestedMappings)
            {
                // Try inline collection auto-wire first
                var collBlock = TryAutoWireCollectionInlineStatements(
                    destProp, sourceProp.Type, $"{sourceParam}.{sourceProp.Name}",
                    destParam, forger, context, method, nullPropertyHandlingOverrides);
                if (collBlock != null)
                {
                    sb.AppendLine(collBlock);
                }
                else
                {
                    var autoWireResult = TryAutoWireForgeMethod(
                        destProp, sourceProp.Type, $"{sourceParam}.{sourceProp.Name}",
                        forger, context, method, nullPropertyHandlingOverrides);
                    if (autoWireResult != null)
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {autoWireResult};");
                }
            }
        }
    }

    /// <summary>
    /// Generates code for a property marked with ExistingTarget = true.
    /// Updates the nested object in place rather than replacing it.
    /// </summary>
    private string? GenerateExistingTargetBlock(
        IPropertySymbol destProp,
        ExistingTargetConfig etConfig,
        string sourceParam,
        string destParam,
        IEnumerable<IPropertySymbol> sourceProperties,
        INamedTypeSymbol sourceNamedType,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // Resolve source property path
        string? sourcePropPath;
        if (!propertyMappings.TryGetValue(destProp.Name, out sourcePropPath))
        {
            var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
            sourcePropPath = matchingSourceProp?.Name;
        }

        if (sourcePropPath == null)
            return null;

        var sourceLeafType = ResolvePathLeafType(sourcePropPath, sourceNamedType);
        if (sourceLeafType == null)
            return null;

        // For scalar or value-type properties, ExistingTarget is ignored — always assign directly.
        // In-place updates rely on reference semantics; for value types, pattern-matching locals capture copies.
        if (IsScalarType(sourceLeafType) || IsScalarType(destProp.Type)
            || sourceLeafType.IsValueType || destProp.Type.IsValueType)
            return null; // Will fall through to normal assignment logic

        // Validate destination property has a getter
        if (destProp.GetMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExistingTargetPropertyHasNoGetter,
                method.Locations.FirstOrDefault(),
                destProp.Name);
            return null;
        }

        // Check if this is a collection property
        var destElemType = GetCollectionElementType(destProp.Type);
        var srcElemType = GetCollectionElementType(sourceLeafType);

        if (destElemType != null && srcElemType != null)
        {
            return GenerateExistingTargetCollectionBlock(
                destProp, etConfig, sourceParam, destParam, sourcePropPath,
                sourceNamedType, destElemType, srcElemType,
                nullPropertyHandlingOverrides, forger, context, method);
        }

        // Non-collection reference type: find matching ForgeInto method
        var forgeIntoMethod = FindForgeIntoMethod(forger.Symbol, sourceLeafType, destProp.Type);
        if (forgeIntoMethod == null && _config.AutoWireNestedMappings)
        {
            var candidates = FindAutoWireForgeIntoMethodCandidates(forger.Symbol, sourceLeafType, destProp.Type);
            if (candidates.Count == 1)
                forgeIntoMethod = candidates[0];
        }

        if (forgeIntoMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                method.Locations.FirstOrDefault(),
                destProp.Name);
            return string.Empty; // Return empty (not null) to prevent fallthrough to normal replacement
        }

        var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceNamedType);
        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
        var srcLocal = $"__src_{destProp.Name}";
        var tgtLocal = $"__tgt_{destProp.Name}";

        var sb = new StringBuilder();

        // Generate null-guarded nested ForgeInto call
        sb.AppendLine($"            if ({sourceExpr} is {{ }} {srcLocal} && {destParam}.{destProp.Name} is {{ }} {tgtLocal})");
        sb.AppendLine($"            {{");
        sb.AppendLine($"                {forgeIntoMethod.Name}({srcLocal}, {tgtLocal});");
        sb.Append($"            }}");

        // Handle null target property
        if (strategy == 2 || strategy == 4) // CoalesceToDefault / CoalesceToNew — create new instance and assign
        {
            // Try to find a standard forge method for fallback
            var forgeMethod = FindAutoWireForgeMethodCandidates(forger.Symbol, sourceLeafType, destProp.Type);
            sb.AppendLine();
            sb.AppendLine($"            else if ({sourceExpr} is {{ }} {srcLocal}_new && {destParam}.{destProp.Name} is null)");
            sb.AppendLine($"            {{");
            if (forgeMethod.Count == 1)
            {
                sb.AppendLine($"                {destParam}.{destProp.Name} = {forgeMethod[0].Name}({srcLocal}_new);");
            }
            else if (destProp.Type is INamedTypeSymbol destNamed
                     && destNamed.TypeKind == TypeKind.Class
                     && !destNamed.IsAbstract
                     && destNamed.InstanceConstructors.Any(c => c.Parameters.Length == 0
                         && c.DeclaredAccessibility >= (SymbolEqualityComparer.Default.Equals(destNamed.ContainingAssembly, method.ContainingAssembly) ? Accessibility.Internal : Accessibility.Public)))
            {
                // For CoalesceToNew, validate required members before emitting new T()
                if (strategy == 4)
                    ValidateCoalesceToNew(destProp.Type, context, method);
                sb.AppendLine($"                {destParam}.{destProp.Name} = new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}();");
                sb.AppendLine($"                {forgeIntoMethod.Name}({srcLocal}_new, {destParam}.{destProp.Name});");
            }
            else
            {
                // Non-constructible type (interface, abstract, no parameterless ctor) — skip coalesce
                if (strategy == 4) // CoalesceToNew — emit FM0038 instead of silently skipping
                    ValidateCoalesceToNew(destProp.Type, context, method);
                sb.AppendLine($"                // Cannot coalesce: {destProp.Type.ToDisplayString()} has no accessible parameterless constructor");
            }
            sb.Append($"            }}");
        }
        else if (strategy == 3) // ThrowException
        {
            sb.AppendLine();
            sb.AppendLine($"            else if ({sourceExpr} is not null && {destParam}.{destProp.Name} is null)");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                throw new global::System.InvalidOperationException(\"Cannot update null target property '{destProp.Name}' in place\");");
            sb.Append($"            }}");
        }
        // NullForgiving (0) and SkipNull (1): just skip if target is null (the if block already handles this)

        return sb.ToString();
    }

    /// <summary>
    /// Generates code for a collection property marked with ExistingTarget = true.
    /// </summary>
    private string? GenerateExistingTargetCollectionBlock(
        IPropertySymbol destProp,
        ExistingTargetConfig etConfig,
        string sourceParam,
        string destParam,
        string sourcePropPath,
        INamedTypeSymbol sourceNamedType,
        ITypeSymbol destElemType,
        ITypeSymbol srcElemType,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceNamedType);
        var srcLocal = $"__src_{destProp.Name}";
        var tgtLocal = $"__tgt_{destProp.Name}";

        if (etConfig.CollectionUpdate == 0) // Replace — fall through to normal assignment
            return null;

        // Add and Sync require a mutable collection (List<T> or ICollection<T>).
        // Reject arrays, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>.
        if (destProp.Type is IArrayTypeSymbol)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                method.Locations.FirstOrDefault(),
                $"{destProp.Name} (Add/Sync requires a mutable collection, not an array)");
            return string.Empty;
        }
        if (destProp.Type is INamedTypeSymbol destCollType)
        {
            var origDef = destCollType.OriginalDefinition.ToDisplayString();
            if (origDef == "System.Collections.Generic.IEnumerable<T>" ||
                origDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                origDef == "System.Collections.Generic.IReadOnlyCollection<T>" ||
                origDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>" ||
                origDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>" ||
                origDef == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                    method.Locations.FirstOrDefault(),
                    $"{destProp.Name} (Add/Sync requires a mutable collection, not '{origDef}')");
                return string.Empty;
            }
        }

        if (etConfig.CollectionUpdate == 1) // Add
        {
            // Find element forge method for type conversion
            var elemForgeMethod = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
            var typesMatch = SymbolEqualityComparer.Default.Equals(srcElemType, destElemType) || CanAssign(srcElemType, destElemType);
            if (!typesMatch && elemForgeMethod.Count == 0)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                    method.Locations.FirstOrDefault(),
                    $"{destProp.Name} (no forge method for element type conversion)");
                return string.Empty;
            }
            if (!typesMatch && elemForgeMethod.Count > 1)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.AmbiguousAutoWire,
                    method.Locations.FirstOrDefault(),
                    destProp.Name, destProp.ContainingType.Name);
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"            if ({sourceExpr} is {{ }} {srcLocal} && {destParam}.{destProp.Name} is {{ }} {tgtLocal})");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                foreach (var __srcItem in {srcLocal})");
            sb.AppendLine($"                {{");
            if (elemForgeMethod.Count == 1 && !typesMatch)
            {
                sb.AppendLine($"                    {tgtLocal}.Add({elemForgeMethod[0].Name}(__srcItem));");
            }
            else
            {
                sb.AppendLine($"                    {tgtLocal}.Add(__srcItem);");
            }
            sb.AppendLine($"                }}");
            sb.Append($"            }}");

            // Handle null target collection per NullPropertyHandling
            var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
            if (strategy == 2 || strategy == 4) // CoalesceToDefault / CoalesceToNew — create collection and populate
            {
                sb.AppendLine();
                sb.AppendLine($"            else if ({sourceExpr} is {{ }} {srcLocal}_new && {destParam}.{destProp.Name} is null)");
                sb.AppendLine($"            {{");
                var emptyExpr = GenerateEmptyCollectionExpression(destProp.Type);
                sb.AppendLine($"                {destParam}.{destProp.Name} = {emptyExpr ?? $"new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"};");
                sb.AppendLine($"                foreach (var __srcItem in {srcLocal}_new)");
                sb.AppendLine($"                {{");
                if (elemForgeMethod.Count == 1 && !typesMatch)
                {
                    sb.AppendLine($"                    {destParam}.{destProp.Name}.Add({elemForgeMethod[0].Name}(__srcItem));");
                }
                else
                {
                    sb.AppendLine($"                    {destParam}.{destProp.Name}.Add(__srcItem);");
                }
                sb.AppendLine($"                }}");
                sb.Append($"            }}");
            }
            else if (strategy == 3) // ThrowException
            {
                sb.AppendLine();
                sb.AppendLine($"            else if ({sourceExpr} is not null && {destParam}.{destProp.Name} is null)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                throw new global::System.InvalidOperationException(\"Cannot update null target collection '{destProp.Name}' in place\");");
                sb.Append($"            }}");
            }

            return sb.ToString();
        }

        if (etConfig.CollectionUpdate == 2) // Sync
        {
            // Validate KeyProperty
            if (string.IsNullOrEmpty(etConfig.KeyProperty))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.SyncRequiresKeyProperty,
                    method.Locations.FirstOrDefault(),
                    destProp.Name);
                return string.Empty;
            }

            var keyPropName = etConfig.KeyProperty!;

            // Validate key property exists on both element types
            var srcKeyProp = GetMappableProperties(srcElemType as INamedTypeSymbol)
                .FirstOrDefault(p => string.Equals(p.Name, keyPropName, StringComparison.Ordinal));
            var destKeyProp = GetMappableProperties(destElemType as INamedTypeSymbol)
                .FirstOrDefault(p => string.Equals(p.Name, keyPropName, StringComparison.Ordinal));

            if (srcKeyProp == null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.KeyPropertyNotFound,
                    method.Locations.FirstOrDefault(),
                    keyPropName, srcElemType.ToDisplayString());
                return string.Empty;
            }
            if (destKeyProp == null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.KeyPropertyNotFound,
                    method.Locations.FirstOrDefault(),
                    keyPropName, destElemType.ToDisplayString());
                return string.Empty;
            }

            // Validate key property types are compatible
            if (!CanAssign(srcKeyProp.Type, destKeyProp.Type))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.KeyPropertyNotFound,
                    method.Locations.FirstOrDefault(),
                    keyPropName, $"{srcElemType.ToDisplayString()} (key type '{srcKeyProp.Type.ToDisplayString()}' is not compatible with '{destKeyProp.Type.ToDisplayString()}')");
                return string.Empty;
            }

            // Sync requires List<T> destination (RemoveAll is List<T>-specific)
            if (destProp.Type is INamedTypeSymbol destCollNamedType)
            {
                var originalDef = destCollNamedType.OriginalDefinition.ToDisplayString();
                if (originalDef != "System.Collections.Generic.List<T>")
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                        method.Locations.FirstOrDefault(),
                        $"{destProp.Name} (CollectionUpdateStrategy.Sync requires List<T> destination)");
                    return string.Empty;
                }
            }

            var keyTypeDisplay = destKeyProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Find ForgeInto method for element-level updates
            var elemForgeIntoMethod = FindForgeIntoMethod(forger.Symbol, srcElemType, destElemType);
            if (elemForgeIntoMethod == null && _config.AutoWireNestedMappings)
            {
                var candidates = FindAutoWireForgeIntoMethodCandidates(forger.Symbol, srcElemType, destElemType);
                if (candidates.Count == 1)
                    elemForgeIntoMethod = candidates[0];
            }

            // Find standard forge method for adding new items
            var elemForgeMethod = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);

            var sb = new StringBuilder();
            sb.AppendLine($"            if ({sourceExpr} is {{ }} {srcLocal} && {destParam}.{destProp.Name} is {{ }} {tgtLocal})");
            sb.AppendLine($"            {{");
            sb.AppendLine($"                var __existing = new global::System.Collections.Generic.Dictionary<{keyTypeDisplay}, {destElemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>();");
            sb.AppendLine($"                foreach (var __item in {tgtLocal})");
            sb.AppendLine($"                    __existing[__item.{keyPropName}] = __item;");
            sb.AppendLine();
            sb.AppendLine($"                var __matched = new global::System.Collections.Generic.HashSet<{keyTypeDisplay}>();");
            sb.AppendLine($"                foreach (var __srcItem in {srcLocal})");
            sb.AppendLine($"                {{");
            sb.AppendLine($"                    if (__existing.TryGetValue(__srcItem.{keyPropName}, out var __tgtItem))");
            sb.AppendLine($"                    {{");

            if (elemForgeIntoMethod != null)
            {
                sb.AppendLine($"                        {elemForgeIntoMethod.Name}(__srcItem, __tgtItem);");
            }
            else
            {
                // No ForgeInto method: matched items will be kept but not updated
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                    method.Locations.FirstOrDefault(),
                    $"{destProp.Name} (no element ForgeInto method; matched items will not be updated)");
            }

            sb.AppendLine($"                    }}");
            sb.AppendLine($"                    else");
            sb.AppendLine($"                    {{");

            var syncTypesMatch = SymbolEqualityComparer.Default.Equals(srcElemType, destElemType) || CanAssign(srcElemType, destElemType);
            if (elemForgeMethod.Count == 1 && !syncTypesMatch)
            {
                sb.AppendLine($"                        {tgtLocal}.Add({elemForgeMethod[0].Name}(__srcItem));");
            }
            else if (syncTypesMatch)
            {
                sb.AppendLine($"                        {tgtLocal}.Add(__srcItem);");
            }
            else if (elemForgeMethod.Count > 1)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.AmbiguousAutoWire,
                    method.Locations.FirstOrDefault(),
                    destProp.Name, destProp.ContainingType.Name);
                sb.AppendLine($"                        // Cannot add: ambiguous forge method for element type conversion");
            }
            else
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                    method.Locations.FirstOrDefault(),
                    $"{destProp.Name} (Sync cannot add new items: no forge method for element type conversion)");
                sb.AppendLine($"                        // Cannot add: no forge method for element type conversion");
            }

            sb.AppendLine($"                    }}");
            sb.AppendLine($"                    __matched.Add(__srcItem.{keyPropName});");
            sb.AppendLine($"                }}");
            sb.AppendLine();
            sb.AppendLine($"                {tgtLocal}.RemoveAll(__item => !__matched.Contains(__item.{keyPropName}));");
            sb.Append($"            }}");

            // Handle null target collection per NullPropertyHandling
            var syncStrategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
            if (syncStrategy == 2 || syncStrategy == 4) // CoalesceToDefault / CoalesceToNew — create and populate from source
            {
                var syncTypesMatchCoalesce = SymbolEqualityComparer.Default.Equals(srcElemType, destElemType) || CanAssign(srcElemType, destElemType);
                sb.AppendLine();
                sb.AppendLine($"            else if ({sourceExpr} is {{ }} {srcLocal}_new && {destParam}.{destProp.Name} is null)");
                sb.AppendLine($"            {{");
                var emptyExpr = GenerateEmptyCollectionExpression(destProp.Type);
                sb.AppendLine($"                {destParam}.{destProp.Name} = {emptyExpr ?? $"new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"};");
                sb.AppendLine($"                foreach (var __srcItem in {srcLocal}_new)");
                sb.AppendLine($"                {{");
                if (elemForgeMethod.Count == 1 && !syncTypesMatchCoalesce)
                {
                    sb.AppendLine($"                    {destParam}.{destProp.Name}.Add({elemForgeMethod[0].Name}(__srcItem));");
                }
                else if (syncTypesMatchCoalesce)
                {
                    sb.AppendLine($"                    {destParam}.{destProp.Name}.Add(__srcItem);");
                }
                else
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ExistingTargetNoMatchingForgeInto,
                        method.Locations.FirstOrDefault(),
                        $"{destProp.Name} (Sync CoalesceToDefault cannot add items: no forge method for element type conversion)");
                    sb.AppendLine($"                    // Cannot add: no forge method for element type conversion");
                }
                sb.AppendLine($"                }}");
                sb.Append($"            }}");
            }
            else if (syncStrategy == 3) // ThrowException
            {
                sb.AppendLine();
                sb.AppendLine($"            else if ({sourceExpr} is not null && {destParam}.{destProp.Name} is null)");
                sb.AppendLine($"            {{");
                sb.AppendLine($"                throw new global::System.InvalidOperationException(\"Cannot update null target collection '{destProp.Name}' in place\");");
                sb.Append($"            }}");
            }

            return sb.ToString();
        }

        return null;
    }

    /// <summary>
    /// Generates a collection forging method (List&lt;T&gt;, T[], IEnumerable&lt;T&gt;, etc.).
    /// </summary>
    private string GenerateCollectionForgeMethod(
        IMethodSymbol method,
        ITypeSymbol sourceCollectionType,
        ITypeSymbol destCollectionType,
        ITypeSymbol destElementType,
        string elementMethodName)
    {
        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;

        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var destTypeDisplay = destCollectionType.ToDisplayString();
        var sourceTypeDisplay = sourceCollectionType.ToDisplayString();

        sb.AppendLine($"        {accessibility} partial {destTypeDisplay} {method.Name}({sourceTypeDisplay} {sourceParam})");
        sb.AppendLine("        {");
        sb.AppendLine(GenerateNullCheck(sourceParam, "null!"));
        sb.AppendLine();

        // Determine the destination collection kind
        if (destCollectionType is IArrayTypeSymbol)
        {
            var destElemDisplay = destElementType.ToDisplayString();

            // For sources with a cheap Count/Length, pre-size the array and fill it.
            if (HasCheapCount(sourceCollectionType))
            {
                var lengthExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
                sb.AppendLine($"            var result = new {destElemDisplay}[{lengthExpr}];");
                sb.AppendLine($"            var i = 0;");
                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                result[i++] = {elementMethodName}(item);");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
            }
            else
            {
                // For general IEnumerable<T> sources, avoid double-enumeration by using a single-pass Select+ToArray.
                sb.AppendLine($"            return {sourceParam}.Select(item => {elementMethodName}(item)).ToArray();");
            }
        }
        else if (destCollectionType is INamedTypeSymbol destNamedType)
        {
            var originalDef = destNamedType.OriginalDefinition.ToDisplayString();

            if (originalDef == "System.Collections.Generic.IEnumerable<T>")
            {
                // IEnumerable<T> - lazy Select projection
                sb.AppendLine($"            return {sourceParam}.Select(item => {elementMethodName}(item));");
            }
            else if (originalDef == "System.Collections.Generic.HashSet<T>")
            {
                // HashSet<T> - foreach + Add
                var destElemDisplay = destElementType.ToDisplayString();
                sb.AppendLine($"            var result = new global::System.Collections.Generic.HashSet<{destElemDisplay}>();");
                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                result.Add({elementMethodName}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
            }
            else if (originalDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
            {
                // ReadOnlyCollection<T> - build list + AsReadOnly
                var destElemDisplay = destElementType.ToDisplayString();
                if (HasCheapCount(sourceCollectionType))
                {
                    var countExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
                    sb.AppendLine($"            var list = new global::System.Collections.Generic.List<{destElemDisplay}>({countExpr});");
                }
                else
                {
                    sb.AppendLine($"            var list = new global::System.Collections.Generic.List<{destElemDisplay}>();");
                }
                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                list.Add({elementMethodName}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return list.AsReadOnly();");
            }
            else
            {
                // List<T>, IList<T>, ICollection<T>, IReadOnlyList<T>, IReadOnlyCollection<T> -> return List<T>
                var destElemDisplay = destElementType.ToDisplayString();

                // Avoid double-enumeration: only pre-size when source has a cheap Count property.
                // For IEnumerable<T> sources, allocate without capacity to avoid calling Count().
                if (HasCheapCount(sourceCollectionType))
                {
                    var countExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
                    sb.AppendLine($"            var result = new global::System.Collections.Generic.List<{destElemDisplay}>({countExpr});");
                }
                else
                {
                    sb.AppendLine($"            var result = new global::System.Collections.Generic.List<{destElemDisplay}>();");
                }

                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                result.Add({elementMethodName}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
            }
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }
}
