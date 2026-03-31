using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private enum CollectionInlineKind { SingleExpression, MultiStatement }

    /// <summary>
    /// Tries to auto-wire a destination property by finding a matching forge method on the forger class.
    /// Returns the generated expression, or null if no unique match was found.
    /// </summary>
    private string? TryAutoWireForgeMethod(
        IPropertySymbol destProp,
        ITypeSymbol sourcePropertyType,
        string sourceExpr,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // Don't auto-wire scalar types (primitives, enums, strings)
        if (IsScalarType(sourcePropertyType) || IsScalarType(destProp.Type))
            return null;

        // Don't auto-wire when types are directly assignable
        if (CanAssign(sourcePropertyType, destProp.Type))
            return null;

        var candidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropertyType, destProp.Type);

        if (candidates.Count == 1)
        {
            var matchedMethod = candidates[0];

            // Report FM0027 (info, off by default) for visibility
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.PropertyAutoWired,
                method.Locations.FirstOrDefault(),
                destProp.Name, matchedMethod.Name);

            if (sourcePropertyType.IsReferenceType)
            {
                var localVarName = $"__autoWire_{destProp.Name}";
                var nullFallback = destProp.Type.IsValueType ? "default" : "null!";
                return $"{sourceExpr} is {{ }} {localVarName} ? {matchedMethod.Name}({localVarName}) : {nullFallback}";
            }
            else
            {
                return $"{matchedMethod.Name}({sourceExpr})";
            }
        }
        else if (candidates.Count > 1)
        {
            // FM0025: ambiguous — multiple forge methods match
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousAutoWire,
                method.Locations.FirstOrDefault(),
                destProp.Name, destProp.ContainingType.Name);
        }

        return null;
    }

    /// <summary>
    /// Generates the raw inline collection iteration code for a given source→dest collection pair.
    /// Returns the kind (single expression vs multi-statement) and the code string.
    /// For SingleExpression: the code is a complete expression (e.g., "Array.ConvertAll(...)").
    /// For MultiStatement: the code is a block of statements that build the result into a local variable
    /// named __collResult_{destPropName} (without property assignment — caller handles that).
    /// </summary>
    private (CollectionInlineKind Kind, string Code)? GenerateInlineCollectionCode(
        ITypeSymbol sourceCollType, ITypeSymbol destCollType,
        ITypeSymbol destElemType,
        string sourceLocalName, string destPropName,
        IMethodSymbol elementForgeMethod)
    {
        var destElemDisplay = destElemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var methodName = elementForgeMethod.Name;
        var resultVar = $"__collResult_{destPropName}";

        // Array destination
        if (destCollType is IArrayTypeSymbol)
        {
            if (sourceCollType is IArrayTypeSymbol)
            {
                // T[] → U[]: Array.ConvertAll (single expression)
                return (CollectionInlineKind.SingleExpression,
                    $"global::System.Array.ConvertAll({sourceLocalName}, __collItem => {methodName}(__collItem))");
            }
            else if (HasCheapCount(sourceCollType))
            {
                // Non-array with cheap .Count → U[]: indexed loop (multi-statement)
                var lengthExpr = GetCollectionLengthExpression(sourceCollType, sourceLocalName);
                var sb = new StringBuilder();
                sb.AppendLine($"                var {resultVar} = new {destElemDisplay}[{lengthExpr}];");
                sb.AppendLine($"                var __collIdx_{destPropName} = 0;");
                sb.AppendLine($"                foreach (var __collItem in {sourceLocalName})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {resultVar}[__collIdx_{destPropName}++] = {methodName}(__collItem);");
                sb.Append("                }");
                return (CollectionInlineKind.MultiStatement, sb.ToString());
            }
            else
            {
                // IEnumerable<T> or other without cheap count → U[]: Select + ToArray (single expression, avoids double enumeration)
                return (CollectionInlineKind.SingleExpression,
                    $"global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select({sourceLocalName}, __collItem => {methodName}(__collItem)))");
            }
        }

        if (destCollType is INamedTypeSymbol destNamedType)
        {
            var originalDef = destNamedType.OriginalDefinition.ToDisplayString();

            // IEnumerable<U>: lazy Select (single expression)
            if (originalDef == "System.Collections.Generic.IEnumerable<T>")
            {
                return (CollectionInlineKind.SingleExpression,
                    $"{sourceLocalName}.Select(__collItem => {methodName}(__collItem))");
            }

            // HashSet<U>: foreach + Add (no capacity ctor per spec)
            if (originalDef == "System.Collections.Generic.HashSet<T>")
            {
                var sb = new StringBuilder();
                sb.AppendLine($"                var {resultVar} = new global::System.Collections.Generic.HashSet<{destElemDisplay}>();");
                sb.AppendLine($"                foreach (var __collItem in {sourceLocalName})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {resultVar}.Add({methodName}(__collItem));");
                sb.Append("                }");
                return (CollectionInlineKind.MultiStatement, sb.ToString());
            }

            // List<U>, IList<U>, ICollection<U>, IReadOnlyList<U>, IReadOnlyCollection<U>: pre-sized List + foreach
            var sb2 = new StringBuilder();
            if (HasCheapCount(sourceCollType))
            {
                var countExpr = GetCollectionLengthExpression(sourceCollType, sourceLocalName);
                sb2.AppendLine($"                var {resultVar} = new global::System.Collections.Generic.List<{destElemDisplay}>({countExpr});");
            }
            else
            {
                sb2.AppendLine($"                var {resultVar} = new global::System.Collections.Generic.List<{destElemDisplay}>();");
            }
            sb2.AppendLine($"                foreach (var __collItem in {sourceLocalName})");
            sb2.AppendLine("                {");
            sb2.AppendLine($"                    {resultVar}.Add({methodName}(__collItem));");
            sb2.Append("                }");
            return (CollectionInlineKind.MultiStatement, sb2.ToString());
        }

        return null;
    }

    /// <summary>
    /// Tries to auto-wire a collection property by finding a matching element forge method and
    /// generating inline collection iteration code. Used in property assignment (object initializer) context.
    /// Returns an expression string for single-expression collections, or null when a multi-statement
    /// block is added to <paramref name="postConstructionCollections"/> (or <paramref name="preConstructionBlocks"/> for init-only).
    /// Returns null with no side effects if not applicable.
    /// </summary>
    private string? TryAutoWireCollectionInline(
        IPropertySymbol destProp,
        ITypeSymbol sourcePropertyType,
        string sourceExpr,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        List<(string DestPropName, string Block)>? postConstructionCollections,
        List<string>? preConstructionBlocks)
    {
        // Both source and dest must be collection types
        var srcElemType = GetCollectionElementType(sourcePropertyType);
        var destElemType = GetCollectionElementType(destProp.Type);
        if (srcElemType == null || destElemType == null)
            return null;

        // If there is an explicit collection-level forge method, let the existing auto-wire handle it
        var collectionLevelCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropertyType, destProp.Type);
        if (collectionLevelCandidates.Count > 0)
            return null;

        // Find element-level forge method candidates
        var elemCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
        if (elemCandidates.Count == 0)
            return null;

        if (elemCandidates.Count > 1)
        {
            // FM0025: ambiguous
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousAutoWire,
                method.Locations.FirstOrDefault(),
                destProp.Name, destProp.ContainingType.Name);
            return null;
        }

        var elementMethod = elemCandidates[0];

        // FM0027: info diagnostic for visibility
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.PropertyAutoWired,
            method.Locations.FirstOrDefault(),
            destProp.Name, elementMethod.Name);

        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
        var collLocal = $"__coll_{destProp.Name}";

        var inlineResult = GenerateInlineCollectionCode(
            sourcePropertyType, destProp.Type, destElemType,
            collLocal, destProp.Name, elementMethod);

        if (inlineResult == null)
            return null;

        var (kind, code) = inlineResult.Value;
        var isInitOnly = destProp.SetMethod?.IsInitOnly == true;

        if (kind == CollectionInlineKind.SingleExpression)
        {
            // Single expression — fits in object initializer
            var nullFallback = GenerateCollectionNullFallback(destProp, strategy, sourceExpr);
            if (strategy == 1 && !isInitOnly && postConstructionCollections != null)
            {
                // SkipNull: post-construction if-guard (no else branch)
                var block = new StringBuilder();
                block.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                block.AppendLine($"            {{");
                block.AppendLine($"                result.{destProp.Name} = {code};");
                block.Append($"            }}");
                postConstructionCollections.Add((destProp.Name, block.ToString()));
                return null;
            }

            return $"{sourceExpr} is {{ }} {collLocal} ? {code} : {nullFallback}";
        }
        else
        {
            // Multi-statement — needs post-construction or pre-construction block
            var resultVar = $"__collResult_{destProp.Name}";

            if (isInitOnly)
            {
                // Init-only: build local before constructor, assign in initializer
                var block = new StringBuilder();
                block.AppendLine($"            {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}? __collInit_{destProp.Name} = null;");
                block.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                block.AppendLine($"            {{");
                block.AppendLine(code);
                block.AppendLine($"                __collInit_{destProp.Name} = {resultVar};");
                block.Append($"            }}");
                preConstructionBlocks?.Add(block.ToString());

                if (strategy == 2) // CoalesceToDefault
                {
                    var defaultExpr = GenerateEmptyCollectionExpression(destProp.Type) ?? GenerateCoalesceDefault(destProp.Type);
                    return $"__collInit_{destProp.Name} ?? {defaultExpr ?? $"new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"}";
                }
                if (strategy == 3) // ThrowException
                {
                    return $"__collInit_{destProp.Name} ?? throw new global::System.ArgumentNullException(\"{destProp.Name}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destProp.ContainingType.Name}.{destProp.Name}'.\")";
                }
                return $"__collInit_{destProp.Name}!"; // NullForgiving (or SkipNull fallback)
            }

            if (postConstructionCollections == null)
                return null;

            // Non-init-only: emit post-construction block
            var postBlock = new StringBuilder();
            if (strategy == 1) // SkipNull — no else
            {
                postBlock.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                postBlock.AppendLine($"            {{");
                postBlock.AppendLine(code);
                postBlock.AppendLine($"                result.{destProp.Name} = {resultVar};");
                postBlock.Append($"            }}");
            }
            else
            {
                postBlock.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                postBlock.AppendLine($"            {{");
                postBlock.AppendLine(code);
                postBlock.AppendLine($"                result.{destProp.Name} = {resultVar};");
                postBlock.AppendLine($"            }}");
                postBlock.AppendLine($"            else");
                postBlock.AppendLine($"            {{");
                if (strategy == 2) // CoalesceToDefault
                {
                    var defaultExpr = GenerateEmptyCollectionExpression(destProp.Type) ?? GenerateCoalesceDefault(destProp.Type);
                    postBlock.AppendLine($"                result.{destProp.Name} = {defaultExpr ?? $"new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"};");
                }
                else if (strategy == 3) // ThrowException
                {
                    postBlock.AppendLine($"                throw new global::System.ArgumentNullException(\"{destProp.Name}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destProp.ContainingType.Name}.{destProp.Name}'.\");");
                }
                else // NullForgiving (strategy == 0)
                {
                    postBlock.AppendLine($"                result.{destProp.Name} = null!;");
                }
                postBlock.Append($"            }}");
            }

            postConstructionCollections.Add((destProp.Name, postBlock.ToString()));
            return null;
        }
    }

    /// <summary>
    /// Tries automatic flattening: matches dest property "CustomerName" to source path "Customer.Name".
    /// Walks the source type hierarchy looking for concatenated property name matches.
    /// </summary>
    private string? TryAutoFlatten(IPropertySymbol destProp, string sourceParam, INamedTypeSymbol sourceType, out ITypeSymbol? leafTypeOut)
    {
        var destName = destProp.Name;
        var (expr, leafType) = TryAutoFlattenRecursive(destName, 0, sourceParam, sourceType);
        leafTypeOut = leafType;
        if (expr == null || leafType == null)
            return null;

        // Validate type compatibility between leaf and destination property
        if (!CanAssign(leafType, destProp.Type))
            return null;

        // Handle nullable-to-non-nullable value type conversion
        if (IsNullableToNonNullableValueType(leafType, destProp.Type))
            return expr.Contains("?.") ? $"({destProp.Type.ToDisplayString()})({expr})" : $"({destProp.Type.ToDisplayString()}){expr}";

        // Handle lifted value type from null-conditional: source.Customer?.Age is int? even
        // though Age is int. The ! operator suppresses warnings but doesn't cast, so emit
        // an explicit cast to the destination type.
        if (expr.Contains("?.") && leafType.IsValueType && GetNullableUnderlyingType(leafType) == null
            && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
            return $"({destProp.Type.ToDisplayString()})({expr})";

        return expr;
    }

    private (string? Expression, ITypeSymbol? LeafType) TryAutoFlattenRecursive(string destName, int startIndex, string currentExpr, INamedTypeSymbol currentType)
    {
        if (startIndex >= destName.Length)
            return (null, null);

        var properties = GetMappableProperties(currentType).ToList();

        foreach (var prop in properties)
        {
            var propName = prop.Name;
            if (destName.Length >= startIndex + propName.Length &&
                string.Equals(destName.Substring(startIndex, propName.Length), propName, _config.PropertyNameComparison))
            {
                var newStartIndex = startIndex + propName.Length;

                if (newStartIndex == destName.Length)
                {
                    // Full match - this property is the leaf
                    // Use null-conditional only if the chain already has ?. (ancestor was nullable)
                    string leafExpr;
                    if (currentExpr.Contains("?."))
                        leafExpr = $"{currentExpr}?.{propName}!";
                    else
                        leafExpr = $"{currentExpr}.{propName}";
                    return (leafExpr, prop.Type);
                }

                // Partial match - recurse into this property's type
                if (prop.Type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Class)
                {
                    var nullConditionalExpr = prop.Type.IsReferenceType
                        ? $"{currentExpr}?.{propName}"
                        : $"{currentExpr}.{propName}";
                    var result = TryAutoFlattenRecursive(destName, newStartIndex, nullConditionalExpr, namedType);
                    if (result.Expression != null)
                        return result;
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Generates the null fallback expression for collection properties based on NullPropertyHandling strategy.
    /// </summary>
    private string GenerateCollectionNullFallback(IPropertySymbol destProp, int strategy, string sourceExpr)
    {
        switch (strategy)
        {
            case 2: // CoalesceToDefault
                var defaultExpr = GenerateEmptyCollectionExpression(destProp.Type) ?? GenerateCoalesceDefault(destProp.Type);
                return defaultExpr ?? "null!";
            case 3: // ThrowException
                return $"throw new global::System.ArgumentNullException(\"{destProp.Name}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destProp.ContainingType.Name}.{destProp.Name}'.\")";
            default: // NullForgiving (0) or SkipNull fallback
                return "null!";
        }
    }

    /// <summary>
    /// Generates an empty-collection expression for a given collection destination type.
    /// Handles interfaces by mapping to concrete types (e.g., IReadOnlyList → new List).
    /// Returns null if the type is not a recognized collection.
    /// </summary>
    private static string? GenerateEmptyCollectionExpression(ITypeSymbol destType)
    {
        if (destType is IArrayTypeSymbol arrayType)
        {
            var elemDisplay = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"global::System.Array.Empty<{elemDisplay}>()";
        }

        if (destType is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            var elemDisplay = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var originalDef = namedType.OriginalDefinition.ToDisplayString();

            if (originalDef == "System.Collections.Generic.HashSet<T>")
                return $"new global::System.Collections.Generic.HashSet<{elemDisplay}>()";

            // IEnumerable<T> → Enumerable.Empty
            if (originalDef == "System.Collections.Generic.IEnumerable<T>")
                return $"global::System.Linq.Enumerable.Empty<{elemDisplay}>()";

            // All other supported collection interfaces/types → new List<T>()
            if (originalDef == "System.Collections.Generic.List<T>" ||
                originalDef == "System.Collections.Generic.IList<T>" ||
                originalDef == "System.Collections.Generic.ICollection<T>" ||
                originalDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                originalDef == "System.Collections.Generic.IReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{elemDisplay}>()";
        }

        return null;
    }

    /// <summary>
    /// Tries to auto-wire a collection property inline for ForgeInto (statement) context.
    /// Returns a complete block of statements, or null if not applicable.
    /// </summary>
    private string? TryAutoWireCollectionInlineStatements(
        IPropertySymbol destProp,
        ITypeSymbol sourcePropertyType,
        string sourceExpr,
        string destVarName,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method,
        Dictionary<string, int> nullPropertyHandlingOverrides)
    {
        var srcElemType = GetCollectionElementType(sourcePropertyType);
        var destElemType = GetCollectionElementType(destProp.Type);
        if (srcElemType == null || destElemType == null)
            return null;

        var collectionLevelCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropertyType, destProp.Type);
        if (collectionLevelCandidates.Count > 0)
            return null;

        var elemCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
        if (elemCandidates.Count == 0)
            return null;

        if (elemCandidates.Count > 1)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousAutoWire,
                method.Locations.FirstOrDefault(),
                destProp.Name, destProp.ContainingType.Name);
            return null;
        }

        var elementMethod = elemCandidates[0];

        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.PropertyAutoWired,
            method.Locations.FirstOrDefault(),
            destProp.Name, elementMethod.Name);

        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
        var collLocal = $"__coll_{destProp.Name}";

        var inlineResult = GenerateInlineCollectionCode(
            sourcePropertyType, destProp.Type, destElemType,
            collLocal, destProp.Name, elementMethod);

        if (inlineResult == null)
            return null;

        var (kind, code) = inlineResult.Value;
        var resultVar = $"__collResult_{destProp.Name}";

        if (kind == CollectionInlineKind.SingleExpression)
        {
            var nullFallback = GenerateCollectionNullFallback(destProp, strategy, sourceExpr);
            if (strategy == 1) // SkipNull
            {
                return $"            if ({sourceExpr} is {{ }} {collLocal})\n" +
                       $"            {{\n" +
                       $"                {destVarName}.{destProp.Name} = {code};\n" +
                       $"            }}";
            }

            return $"            {destVarName}.{destProp.Name} = {sourceExpr} is {{ }} {collLocal} ? {code} : {nullFallback};";
        }
        else
        {
            // Multi-statement
            var sb = new StringBuilder();
            if (strategy == 1) // SkipNull
            {
                sb.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                sb.AppendLine($"            {{");
                sb.AppendLine(code);
                sb.AppendLine($"                {destVarName}.{destProp.Name} = {resultVar};");
                sb.Append($"            }}");
            }
            else
            {
                sb.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                sb.AppendLine($"            {{");
                sb.AppendLine(code);
                sb.AppendLine($"                {destVarName}.{destProp.Name} = {resultVar};");
                sb.AppendLine($"            }}");
                sb.AppendLine($"            else");
                sb.AppendLine($"            {{");
                if (strategy == 2)
                {
                    var defaultExpr = GenerateEmptyCollectionExpression(destProp.Type) ?? GenerateCoalesceDefault(destProp.Type);
                    sb.AppendLine($"                {destVarName}.{destProp.Name} = {defaultExpr ?? $"new {destProp.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()"};");
                }
                else if (strategy == 3)
                {
                    sb.AppendLine($"                throw new global::System.ArgumentNullException(\"{destProp.Name}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destProp.ContainingType.Name}.{destProp.Name}'.\");");
                }
                else
                {
                    sb.AppendLine($"                {destVarName}.{destProp.Name} = null!;");
                }
                sb.Append($"            }}");
            }

            return sb.ToString();
        }
    }
}
