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
        var destProperties = GetMappableProperties(destinationType).Where(p => p.SetMethod != null && !p.SetMethod.IsInitOnly);

        foreach (var destProp in destProperties)
        {
            // Skip ignored properties
            if (ignoredProperties.Contains(destProp.Name))
                continue;

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
                    continue;
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
                    continue;
                }

                sb.AppendLine($"            {destParam}.{destProp.Name} = {resolverCall};");
                continue;
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
                                var nullAssign = destProp.Type.IsValueType ? "default" : "null!";
                                sb.AppendLine($"                {destParam}.{destProp.Name} = {nullAssign};");
                            }
                            else
                            {
                                sb.AppendLine($"            {destParam}.{destProp.Name} = {nestedForgeMethod.Name}({sourceExpr});");
                            }
                            continue;
                        }
                    }
                }

                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ResolverMethodNotFound,
                    method.Locations.FirstOrDefault(),
                    forgingMethodName);
                continue;
            }

            // Check if this property has a mapping from [ForgeProperty]
            if (propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
            {
                var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceNamedType);
                var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceNamedType);
                // When null-conditional lifts a non-nullable value type to Nullable<T>, cast back
                var isLiftedValueType = hasNullConditional && sourceLeafType != null && sourceLeafType.IsValueType && GetNullableUnderlyingType(sourceLeafType) == null;
                // Try compatible enum cast first — pass isLifted so it generates correct nullable handling
                if (sourceLeafType != null)
                {
                    var enumCast = TryGenerateCompatibleEnumCast(sourceLeafType, destProp.Type, sourceExpr, isLifted: isLiftedValueType);
                    if (enumCast != null)
                    {
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {enumCast};");
                        continue;
                    }
                }
                if (isLiftedValueType && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = ({destProp.Type.ToDisplayString()})({sourceExpr})!;");
                }
                else if (sourceLeafType != null && IsNullableToNonNullableReferenceType(sourceLeafType, destProp.Type))
                {
                    var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                    ReportFM0007(context, method, sourceNamedType.Name, sourcePropName, destProp.ContainingType.Name, destProp.Name);
                    if (strategy == 1) // SkipNull
                    {
                        var localVar = GenerateSafeVariableName(destProp.Type) + "_" + destProp.Name;
                        sb.AppendLine($"            if ({sourceExpr} is {{ }} {localVar})");
                        sb.AppendLine($"            {{");
                        sb.AppendLine($"                {destParam}.{destProp.Name} = {localVar};");
                        sb.AppendLine($"            }}");
                    }
                    else
                    {
                        var handledExpr = ApplyNullPropertyHandlingExpression(
                            sourceExpr, destProp.Type, destProp.Name,
                            destProp.ContainingType.Name, strategy);
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {handledExpr ?? $"{sourceExpr}!"};");
                    }
                }
                else
                {
                    // Check if leaf type is assignable first
                    if (sourceLeafType != null && !CanAssign(sourceLeafType, destProp.Type)
                        && !IsCompatibleEnumPair(sourceLeafType, destProp.Type))
                    {
                        // Try auto-wire for non-assignable leaf types
                        if (_config.AutoWireNestedMappings)
                        {
                            // Try inline collection auto-wire first
                            var collBlock = TryAutoWireCollectionInlineStatements(
                                destProp, sourceLeafType, sourceExpr,
                                destParam, forger, context, method, nullPropertyHandlingOverrides);
                            if (collBlock != null)
                            {
                                sb.AppendLine(collBlock);
                            }
                            else
                            {
                                var autoWireResult = TryAutoWireForgeMethod(
                                    destProp, sourceLeafType, sourceExpr,
                                    forger, context, method);
                                if (autoWireResult != null)
                                    sb.AppendLine($"            {destParam}.{destProp.Name} = {autoWireResult};");
                            }
                        }
                        // If auto-wire didn't resolve, skip — property stays unmapped
                    }
                    else
                    {
                        // Add null-forgiving operator if we used null-conditional and dest is non-nullable
                        var nullForgiving = hasNullConditional && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {sourceExpr}{nullForgiving};");
                    }
                }
                continue;
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
                            destProp.ContainingType.Name, strategy);
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
                            forger, context, method);
                        if (autoWireResult != null)
                            sb.AppendLine($"            {destParam}.{destProp.Name} = {autoWireResult};");
                    }
                }
            }
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
    /// Generates a collection forging method (List&lt;T&gt;, T[], IEnumerable&lt;T&gt;, etc.).
    /// </summary>
    private string GenerateCollectionForgeMethod(
        IMethodSymbol method,
        ITypeSymbol sourceCollectionType,
        ITypeSymbol destCollectionType,
        ITypeSymbol sourceElementType,
        ITypeSymbol destElementType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        // Validate that an element forging method exists
        var elementForgeMethod = FindForgingMethod(forger.Symbol, method.Name, sourceElementType, destElementType);
        if (elementForgeMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ResolverMethodNotFound,
                method.Locations.FirstOrDefault(),
                $"{method.Name}({sourceElementType.ToDisplayString()})");
            return string.Empty;
        }

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
            // T[] target — arrays need count up front; safe because array sources have .Length
            // and list sources have .Count; IEnumerable sources use .Count() (LINQ)
            var destElemDisplay = destElementType.ToDisplayString();
            var lengthExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
            sb.AppendLine($"            var result = new {destElemDisplay}[{lengthExpr}];");
            sb.AppendLine($"            var i = 0;");
            sb.AppendLine($"            foreach (var item in {sourceParam})");
            sb.AppendLine("            {");
            sb.AppendLine($"                result[i++] = {method.Name}(item);");
            sb.AppendLine("            }");
            sb.AppendLine("            return result;");
        }
        else if (destCollectionType is INamedTypeSymbol destNamedType)
        {
            var originalDef = destNamedType.OriginalDefinition.ToDisplayString();

            if (originalDef == "System.Collections.Generic.IEnumerable<T>")
            {
                // IEnumerable<T> - lazy Select projection
                sb.AppendLine($"            return {sourceParam}.Select(item => {method.Name}(item));");
            }
            else if (originalDef == "System.Collections.Generic.HashSet<T>")
            {
                // HashSet<T> - foreach + Add (no capacity ctor per spec)
                var destElemDisplay = destElementType.ToDisplayString();
                sb.AppendLine($"            var result = new global::System.Collections.Generic.HashSet<{destElemDisplay}>();");
                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                result.Add({method.Name}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
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
                sb.AppendLine($"                result.Add({method.Name}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
            }
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }
}
