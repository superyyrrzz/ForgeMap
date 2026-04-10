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

    private enum CodeEmissionMode { Expression, Statement }

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
        IMethodSymbol method,
        Dictionary<string, int> nullPropertyHandlingOverrides)
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
                string nullFallback;
                if (destProp.Type.IsValueType)
                {
                    nullFallback = "default";
                }
                else
                {
                    var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
                    if (strategy == 4) // CoalesceToNew
                    {
                        ValidateCoalesceToNew(destProp.Type, context, method);
                        var newExpr = GenerateCoalesceNewExpression(destProp.Type, method);
                        nullFallback = newExpr ?? "null!";
                    }
                    else
                    {
                        nullFallback = "null!";
                    }
                }
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

            // HashSet<U>: foreach + Add
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

            // ReadOnlyCollection<U>: build list + AsReadOnly
            if (originalDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
            {
                var sb = new StringBuilder();
                var listVar = $"__collList_{destPropName}";
                if (HasCheapCount(sourceCollType))
                {
                    var countExpr = GetCollectionLengthExpression(sourceCollType, sourceLocalName);
                    sb.AppendLine($"                var {listVar} = new global::System.Collections.Generic.List<{destElemDisplay}>({countExpr});");
                }
                else
                {
                    sb.AppendLine($"                var {listVar} = new global::System.Collections.Generic.List<{destElemDisplay}>();");
                }
                sb.AppendLine($"                foreach (var __collItem in {sourceLocalName})");
                sb.AppendLine("                {");
                sb.AppendLine($"                    {listVar}.Add({methodName}(__collItem));");
                sb.AppendLine("                }");
                sb.AppendLine($"                var {resultVar} = {listVar}.AsReadOnly();");
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
        return TryAutoWireCollectionInlineCore(
            destProp, sourcePropertyType, sourceExpr, forger, context, method,
            nullPropertyHandlingOverrides, CodeEmissionMode.Expression,
            destVarName: null, postConstructionCollections, preConstructionBlocks);
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
        return TryAutoWireCollectionInlineCore(
            destProp, sourcePropertyType, sourceExpr, forger, context, method,
            nullPropertyHandlingOverrides, CodeEmissionMode.Statement,
            destVarName, postConstructionCollections: null, preConstructionBlocks: null);
    }

    /// <summary>
    /// Core implementation for auto-wiring collection properties inline.
    /// Handles both Expression mode (object initializer context) and Statement mode (ForgeInto context).
    /// </summary>
    private string? TryAutoWireCollectionInlineCore(
        IPropertySymbol destProp,
        ITypeSymbol sourcePropertyType,
        string sourceExpr,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        CodeEmissionMode mode,
        string? destVarName,
        List<(string DestPropName, string Block)>? postConstructionCollections,
        List<string>? preConstructionBlocks)
    {
        // Both source and dest must be collection types
        var srcElemType = GetCollectionElementType(sourcePropertyType);
        var destElemType = GetCollectionElementType(destProp.Type);

        // Check for dictionary coercion (dictionaries have 2 type args and aren't in SupportedCollectionTypes)
        if (srcElemType == null || destElemType == null)
        {
            var srcDict = GetDictionaryKeyValueTypes(sourcePropertyType);
            var destDict = GetDictionaryKeyValueTypes(destProp.Type);
            if (srcDict != null && destDict != null)
            {
                if (CanAssign(sourcePropertyType, destProp.Type))
                    return null; // already assignable

                if (mode == CodeEmissionMode.Expression)
                {
                    if (!SymbolEqualityComparer.Default.Equals(srcDict.Value.KeyType, destDict.Value.KeyType))
                    {
                        ReportDiagnosticIfNotSuppressed(context,
                            DiagnosticDescriptors.CollectionCoercionNotSupported,
                            method.Locations.FirstOrDefault(),
                            destProp.Name,
                            sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                        return null;
                    }

                    if (!SymbolEqualityComparer.Default.Equals(srcDict.Value.ValueType, destDict.Value.ValueType))
                    {
                        ReportDiagnosticIfNotSuppressed(context,
                            DiagnosticDescriptors.CollectionCoercionNotSupported,
                            method.Locations.FirstOrDefault(),
                            destProp.Name,
                            sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                        return null;
                    }
                }
                else // Statement mode — combined check
                {
                    if (!SymbolEqualityComparer.Default.Equals(srcDict.Value.KeyType, destDict.Value.KeyType) ||
                        !SymbolEqualityComparer.Default.Equals(srcDict.Value.ValueType, destDict.Value.ValueType))
                    {
                        ReportDiagnosticIfNotSuppressed(context,
                            DiagnosticDescriptors.CollectionCoercionNotSupported,
                            method.Locations.FirstOrDefault(),
                            destProp.Name,
                            sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                        return null;
                    }
                }

                var dictCollLocal = $"__coll_{destProp.Name}";
                var dictExpr = TryGenerateDictionaryCoercion(sourcePropertyType, destProp.Type,
                    srcDict.Value.KeyType, srcDict.Value.ValueType, dictCollLocal, destProp.Name);
                if (dictExpr != null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.CollectionTypeCoerced,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    return ApplyCoercionNullHandlingCore(destProp, sourceExpr, dictCollLocal,
                        dictExpr, mode, destVarName,
                        nullPropertyHandlingOverrides, postConstructionCollections);
                }

                if (mode == CodeEmissionMode.Expression)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.CollectionCoercionNotSupported,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                }
            }
            return null;
        }

        // If there is an explicit collection-level forge method, let the existing auto-wire handle it
        var collectionLevelCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, sourcePropertyType, destProp.Type);
        if (collectionLevelCandidates.Count > 0)
            return null;

        // Find element-level forge method candidates
        var elemCandidates = FindAutoWireForgeMethodCandidates(forger.Symbol, srcElemType, destElemType);
        if (elemCandidates.Count == 0)
        {
            // No element forge method. Check for pure container coercion (identical element types only).
            // We require exact element type match — assignable-but-different types (e.g., string->object)
            // would generate invalid code for invariant generic containers.
            // However, we allow nullable annotation mismatches (e.g., object vs object?) via safe coercion.
            var elemTypesExactMatch = SymbolEqualityComparer.Default.Equals(srcElemType, destElemType);
            var elemTypesNullableMismatchOnly = !elemTypesExactMatch &&
                SymbolEqualityComparer.Default.Equals(
                    srcElemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    destElemType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

            if (!elemTypesExactMatch && !elemTypesNullableMismatchOnly)
                return null;

            // Already assignable containers -> no coercion needed (CanAssign already handled this upstream)
            if (CanAssign(sourcePropertyType, destProp.Type))
                return null;

            // Try dictionary coercion (handles both exact match and nullable mismatch)
            var srcDict = GetDictionaryKeyValueTypes(sourcePropertyType);
            var destDict = GetDictionaryKeyValueTypes(destProp.Type);
            if (srcDict != null && destDict != null)
            {
                var keysExact = SymbolEqualityComparer.Default.Equals(srcDict.Value.KeyType, destDict.Value.KeyType);
                var keysNullMismatch = !keysExact &&
                    SymbolEqualityComparer.Default.Equals(
                        srcDict.Value.KeyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                        destDict.Value.KeyType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));
                var valsExact = SymbolEqualityComparer.Default.Equals(srcDict.Value.ValueType, destDict.Value.ValueType);
                var valsNullMismatch = !valsExact &&
                    SymbolEqualityComparer.Default.Equals(
                        srcDict.Value.ValueType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                        destDict.Value.ValueType.WithNullableAnnotation(NullableAnnotation.NotAnnotated));

                if (!keysExact && !keysNullMismatch)
                {
                    // Key type mismatch (not just nullability) — FM0040
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.CollectionCoercionNotSupported,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    return null;
                }

                var hasNullableMismatch = keysNullMismatch || valsNullMismatch;

                if (hasNullableMismatch)
                {
                    // Generate nullable-safe dictionary coercion via adapter expression
                    var nullableCoercionResult = TryGenerateNullableDictionaryCoercion(
                        destProp.Type,
                        srcDict.Value.KeyType, srcDict.Value.ValueType,
                        destDict.Value.KeyType, destDict.Value.ValueType,
                        $"__coll_{destProp.Name}");
                    if (nullableCoercionResult != null)
                    {
                        ReportDiagnosticIfNotSuppressed(context,
                            DiagnosticDescriptors.NullableCollectionCoercionApplied,
                            method.Locations.FirstOrDefault(),
                            destProp.Name,
                            sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                            destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                        return ApplyCoercionNullHandlingCore(destProp, sourceExpr, $"__coll_{destProp.Name}",
                            nullableCoercionResult, mode, destVarName,
                            nullPropertyHandlingOverrides, postConstructionCollections);
                    }

                    // Unsupported nullable coercion — FM0051
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.NullableCollectionCoercionUnsupported,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                    return null;
                }

                var dictExpr = TryGenerateDictionaryCoercion(sourcePropertyType, destProp.Type,
                    srcDict.Value.KeyType, srcDict.Value.ValueType, $"__coll_{destProp.Name}", destProp.Name);
                if (dictExpr != null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.CollectionTypeCoerced,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    return ApplyCoercionNullHandlingCore(destProp, sourceExpr, $"__coll_{destProp.Name}",
                        dictExpr, mode, destVarName,
                        nullPropertyHandlingOverrides, postConstructionCollections);
                }
            }

            // Try nullable-safe sequence coercion when element types differ only in nullability
            if (elemTypesNullableMismatchOnly)
            {
                var nullableSeqResult = TryGenerateNullableSequenceCoercion(
                    destProp.Type, destElemType, $"__coll_{destProp.Name}");
                if (nullableSeqResult != null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.NullableCollectionCoercionApplied,
                        method.Locations.FirstOrDefault(),
                        destProp.Name,
                        sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                    return ApplyCoercionNullHandlingCore(destProp, sourceExpr, $"__coll_{destProp.Name}",
                        nullableSeqResult, mode, destVarName,
                        nullPropertyHandlingOverrides, postConstructionCollections);
                }

                // Unsupported nullable coercion — FM0051
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.NullableCollectionCoercionUnsupported,
                    method.Locations.FirstOrDefault(),
                    destProp.Name,
                    sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                return null;
            }

            // Try sequence coercion
            var seqCollLocal = $"__coll_{destProp.Name}";
            var coercionExpr = TryGenerateSequenceCoercion(sourcePropertyType, destProp.Type, srcElemType, seqCollLocal);
            if (coercionExpr != null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CollectionTypeCoerced,
                    method.Locations.FirstOrDefault(),
                    destProp.Name,
                    sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                return ApplyCoercionNullHandlingCore(destProp, sourceExpr, seqCollLocal,
                    coercionExpr, mode, destVarName,
                    nullPropertyHandlingOverrides, postConstructionCollections);
            }

            // Both are collections but no known coercion — FM0040
            if (srcDict != null || destDict != null ||
                GetCollectionElementType(sourcePropertyType) != null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CollectionCoercionNotSupported,
                    method.Locations.FirstOrDefault(),
                    destProp.Name,
                    sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            }
            return null;
        }

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
        {
            // Fallback: coercion with element mapping
            var coercionExpr = TryGenerateCoercionWithElementMapping(
                destProp.Type, destElemType, collLocal, elementMethod.Name);
            if (coercionExpr != null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CollectionTypeCoerced,
                    method.Locations.FirstOrDefault(),
                    destProp.Name,
                    sourcePropertyType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));

                return ApplyCoercionNullHandlingCore(destProp, sourceExpr, collLocal,
                    coercionExpr, mode, destVarName,
                    nullPropertyHandlingOverrides, postConstructionCollections);
            }
            return null;
        }

        var (kind, code) = inlineResult.Value;

        if (mode == CodeEmissionMode.Expression)
        {
            return EmitCollectionResultExpression(destProp, sourceExpr, collLocal, strategy, kind, code,
                postConstructionCollections, preConstructionBlocks);
        }
        else // Statement mode
        {
            return EmitCollectionResultStatements(destProp, sourceExpr, destVarName!, collLocal, strategy, kind, code);
        }
    }

    /// <summary>
    /// Emits the final collection result for Expression mode (object initializer context).
    /// </summary>
    private string? EmitCollectionResultExpression(
        IPropertySymbol destProp, string sourceExpr, string collLocal, int strategy,
        CollectionInlineKind kind, string code,
        List<(string DestPropName, string Block)>? postConstructionCollections,
        List<string>? preConstructionBlocks)
    {
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

                if (strategy == 2 || strategy == 4) // CoalesceToDefault / CoalesceToNew
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
                if (strategy == 2 || strategy == 4) // CoalesceToDefault / CoalesceToNew
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
    /// Emits the final collection result for Statement mode (ForgeInto context).
    /// </summary>
    private string EmitCollectionResultStatements(
        IPropertySymbol destProp, string sourceExpr, string destVarName,
        string collLocal, int strategy,
        CollectionInlineKind kind, string code)
    {
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
                if (strategy == 2 || strategy == 4) // CoalesceToDefault / CoalesceToNew
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
            case 4: // CoalesceToNew — same as CoalesceToDefault for collections
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

        if (destType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();

            if (namedType.TypeArguments.Length == 1)
            {
                var elemDisplay = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (originalDef == "System.Collections.Generic.HashSet<T>")
                    return $"new global::System.Collections.Generic.HashSet<{elemDisplay}>()";

                if (originalDef == "System.Collections.Generic.IEnumerable<T>")
                    return $"global::System.Linq.Enumerable.Empty<{elemDisplay}>()";

                if (originalDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
                    return $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{elemDisplay}>(global::System.Array.Empty<{elemDisplay}>())";

                if (originalDef == "System.Collections.Generic.List<T>" ||
                    originalDef == "System.Collections.Generic.IList<T>" ||
                    originalDef == "System.Collections.Generic.ICollection<T>" ||
                    originalDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                    originalDef == "System.Collections.Generic.IReadOnlyCollection<T>")
                    return $"new global::System.Collections.Generic.List<{elemDisplay}>()";
            }

            if (namedType.TypeArguments.Length == 2)
            {
                var keyDisplay = namedType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var valueDisplay = namedType.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                if (originalDef == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
                    originalDef == "System.Collections.Generic.IDictionary<TKey, TValue>")
                    return $"new global::System.Collections.Generic.Dictionary<{keyDisplay}, {valueDisplay}>()";

                if (originalDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>" ||
                    originalDef == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
                    return $"new global::System.Collections.ObjectModel.ReadOnlyDictionary<{keyDisplay}, {valueDisplay}>(new global::System.Collections.Generic.Dictionary<{keyDisplay}, {valueDisplay}>())";
            }
        }

        return null;
    }

    /// <summary>
    /// Generates a sequence collection coercion expression (same element type, different container).
    /// Returns null if no known coercion path exists.
    /// </summary>
    private static string? TryGenerateSequenceCoercion(
        ITypeSymbol sourceCollType, ITypeSymbol destCollType,
        ITypeSymbol elementType, string sourceExpr)
    {
        var elemDisplay = elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Destination: Array
        if (destCollType is IArrayTypeSymbol)
        {
            if (sourceCollType is IArrayTypeSymbol)
                return sourceExpr; // same type — should have been CanAssign
            return $"global::System.Linq.Enumerable.ToArray({sourceExpr})";
        }

        if (destCollType is INamedTypeSymbol destNamed)
        {
            var destDef = destNamed.OriginalDefinition.ToDisplayString();

            // Destination: HashSet<T>
            if (destDef == "System.Collections.Generic.HashSet<T>")
                return $"new global::System.Collections.Generic.HashSet<{elemDisplay}>({sourceExpr})";

            // Destination: ReadOnlyCollection<T>
            if (destDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{elemDisplay}>({sourceExpr}).AsReadOnly()";

            // Destination: List<T> / IList<T> / ICollection<T>
            if (destDef == "System.Collections.Generic.List<T>" ||
                destDef == "System.Collections.Generic.IList<T>" ||
                destDef == "System.Collections.Generic.ICollection<T>")
            {
                if (sourceCollType is IArrayTypeSymbol || (sourceCollType is INamedTypeSymbol srcN &&
                    srcN.OriginalDefinition.ToDisplayString() != destDef))
                    return $"new global::System.Collections.Generic.List<{elemDisplay}>({sourceExpr})";
                return sourceExpr; // same type
            }

            // Destination: IReadOnlyList<T> / IReadOnlyCollection<T>
            if (destDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                destDef == "System.Collections.Generic.IReadOnlyCollection<T>")
            {
                // List<T> implements IReadOnlyList<T> — but CanAssign should already handle this.
                // For T[], IEnumerable<T>, HashSet<T>→IReadOnlyList/IReadOnlyCollection, materialize via new List.
                return $"new global::System.Collections.Generic.List<{elemDisplay}>({sourceExpr})";
            }

            // Destination: IEnumerable<T> — CanAssign covers most cases, fallback:
            if (destDef == "System.Collections.Generic.IEnumerable<T>")
                return sourceExpr;
        }

        return null;
    }

    /// <summary>
    /// Generates a dictionary coercion expression. Returns null if no known coercion path exists.
    /// Always copies to avoid aliasing. Preserves comparer when available via pattern-matching.
    /// </summary>
    private static string? TryGenerateDictionaryCoercion(
        ITypeSymbol sourceCollType, ITypeSymbol destCollType,
        ITypeSymbol keyType, ITypeSymbol valueType,
        string sourceExpr, string destPropName)
    {
        if (sourceCollType is not INamedTypeSymbol srcNamed || destCollType is not INamedTypeSymbol destNamed)
            return null;

        var srcDef = srcNamed.OriginalDefinition.ToDisplayString();
        var destDef = destNamed.OriginalDefinition.ToDisplayString();

        var keyDisplay = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var valueDisplay = valueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dictType = $"global::System.Collections.Generic.Dictionary<{keyDisplay}, {valueDisplay}>";
        var rodType = $"global::System.Collections.ObjectModel.ReadOnlyDictionary<{keyDisplay}, {valueDisplay}>";
        var dictLocal = $"__dict_{destPropName}";

        var srcIsConcreteDictionary = srcDef == "System.Collections.Generic.Dictionary<TKey, TValue>";
        var destIsReadOnlyDictionary = destDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>" ||
            destDef == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>";
        var destIsConcreteDictionary = destDef == "System.Collections.Generic.Dictionary<TKey, TValue>";

        // Dictionary/IDictionary → ReadOnlyDictionary/IReadOnlyDictionary
        if (destIsReadOnlyDictionary &&
            (srcDef == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
             srcDef == "System.Collections.Generic.IDictionary<TKey, TValue>"))
        {
            if (srcIsConcreteDictionary)
            {
                // Concrete Dictionary — access .Comparer directly
                return $"new {rodType}(new {dictType}({sourceExpr}, {sourceExpr}.Comparer))";
            }
            // IDictionary — pattern-match to preserve comparer when available
            return $"{sourceExpr} is {dictType} {dictLocal} ? new {rodType}(new {dictType}({dictLocal}, {dictLocal}.Comparer)) : new {rodType}(new {dictType}({sourceExpr}))";
        }

        // IReadOnlyDictionary → Dictionary
        if (destIsConcreteDictionary &&
            (srcDef == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" ||
             srcDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>"))
        {
            return $"{sourceExpr} is {dictType} {dictLocal} ? new {dictType}({dictLocal}, {dictLocal}.Comparer) : new {dictType}({sourceExpr})";
        }

        // IDictionary → Dictionary (mutable to mutable, copy)
        if (destIsConcreteDictionary &&
            srcDef == "System.Collections.Generic.IDictionary<TKey, TValue>")
        {
            return $"{sourceExpr} is {dictType} {dictLocal} ? new {dictType}({dictLocal}, {dictLocal}.Comparer) : new {dictType}({sourceExpr})";
        }

        // Any supported source → IDictionary<K,V> (copy into new Dictionary)
        var destIsIDictionary = destDef == "System.Collections.Generic.IDictionary<TKey, TValue>";
        if (destIsIDictionary)
        {
            if (srcIsConcreteDictionary)
            {
                return $"new {dictType}({sourceExpr}, {sourceExpr}.Comparer)";
            }
            return $"{sourceExpr} is {dictType} {dictLocal} ? new {dictType}({dictLocal}, {dictLocal}.Comparer) : new {dictType}({sourceExpr})";
        }

        return null;
    }

    /// <summary>
    /// Generates a nullable-safe dictionary coercion when key/value types differ only in nullability.
    /// Uses .ToDictionary() adapter with casts for nullable annotation bridging.
    /// </summary>
    private static string? TryGenerateNullableDictionaryCoercion(
        ITypeSymbol destCollType,
        ITypeSymbol srcKeyType, ITypeSymbol srcValueType,
        ITypeSymbol destKeyType, ITypeSymbol destValueType,
        string sourceExpr)
    {
        if (destCollType is not INamedTypeSymbol destNamed)
            return null;

        var destKeyDisplay = destKeyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var destValueDisplay = destValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var destDef = destNamed.OriginalDefinition.ToDisplayString();

        // Build the key/value cast expressions
        var keyCast = SymbolEqualityComparer.Default.Equals(srcKeyType, destKeyType)
            ? "kv.Key"
            : $"({destKeyDisplay})kv.Key";
        var valueCast = SymbolEqualityComparer.Default.Equals(srcValueType, destValueType)
            ? "kv.Value"
            : $"({destValueDisplay})kv.Value";

        var toDictExpr = $"global::System.Linq.Enumerable.ToDictionary(global::System.Linq.Enumerable.Select({sourceExpr}, kv => new global::System.Collections.Generic.KeyValuePair<{destKeyDisplay}, {destValueDisplay}>({keyCast}, {valueCast})), kv => kv.Key, kv => kv.Value)";

        // For IReadOnlyDictionary, wrap in ReadOnlyDictionary
        if (destDef == "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" ||
            destDef == "System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>")
        {
            var rodType = $"global::System.Collections.ObjectModel.ReadOnlyDictionary<{destKeyDisplay}, {destValueDisplay}>";
            return $"new {rodType}({toDictExpr})";
        }

        // For Dictionary, IDictionary, ToDictionary already returns a compatible type
        if (destDef == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
            destDef == "System.Collections.Generic.IDictionary<TKey, TValue>")
        {
            return toDictExpr;
        }

        // Unsupported concrete dictionary shapes (e.g. SortedDictionary, ConcurrentDictionary)
        // fall back so FM0051 can be raised
        return null;
    }

    /// <summary>
    /// Generates a nullable-safe sequence coercion when element types differ only in nullability.
    /// Uses Select + cast to bridge the nullable annotation gap.
    /// </summary>
    private static string? TryGenerateNullableSequenceCoercion(
        ITypeSymbol destCollType,
        ITypeSymbol destElemType,
        string sourceExpr)
    {
        var destElemDisplay = destElemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var selectExpr = $"global::System.Linq.Enumerable.Select({sourceExpr}, __item => ({destElemDisplay})__item)";

        // Array destination
        if (destCollType is IArrayTypeSymbol)
            return $"global::System.Linq.Enumerable.ToArray({selectExpr})";

        if (destCollType is INamedTypeSymbol destNamed)
        {
            var destDef = destNamed.OriginalDefinition.ToDisplayString();

            if (destDef == "System.Collections.Generic.HashSet<T>")
                return $"new global::System.Collections.Generic.HashSet<{destElemDisplay}>({selectExpr})";

            if (destDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{destElemDisplay}>({selectExpr}).AsReadOnly()";

            if (destDef == "System.Collections.Generic.List<T>" ||
                destDef == "System.Collections.Generic.IList<T>" ||
                destDef == "System.Collections.Generic.ICollection<T>" ||
                destDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                destDef == "System.Collections.Generic.IReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{destElemDisplay}>({selectExpr})";

            if (destDef == "System.Collections.Generic.IEnumerable<T>")
                return selectExpr;
        }

        return null;
    }

    /// <summary>
    /// Generates coercion expression with element-level mapping (different elem types + different container types).
    /// </summary>
    private static string? TryGenerateCoercionWithElementMapping(
        ITypeSymbol destCollType, ITypeSymbol destElemType,
        string sourceExpr, string elementMethodName)
    {
        var destElemDisplay = destElemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var selectExpr = $"global::System.Linq.Enumerable.Select({sourceExpr}, __item => {elementMethodName}(__item))";

        // Array destination
        if (destCollType is IArrayTypeSymbol)
            return $"global::System.Linq.Enumerable.ToArray({selectExpr})";

        if (destCollType is INamedTypeSymbol destNamed)
        {
            var destDef = destNamed.OriginalDefinition.ToDisplayString();

            if (destDef == "System.Collections.Generic.HashSet<T>")
                return $"new global::System.Collections.Generic.HashSet<{destElemDisplay}>({selectExpr})";

            if (destDef == "System.Collections.ObjectModel.ReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{destElemDisplay}>({selectExpr}).AsReadOnly()";

            if (destDef == "System.Collections.Generic.List<T>" ||
                destDef == "System.Collections.Generic.IList<T>" ||
                destDef == "System.Collections.Generic.ICollection<T>" ||
                destDef == "System.Collections.Generic.IReadOnlyList<T>" ||
                destDef == "System.Collections.Generic.IReadOnlyCollection<T>")
                return $"new global::System.Collections.Generic.List<{destElemDisplay}>({selectExpr})";

            if (destDef == "System.Collections.Generic.IEnumerable<T>")
                return selectExpr;
        }

        return null;
    }

    /// <summary>
    /// Core implementation for applying null handling to coercion expressions.
    /// In Expression mode, returns an expression (or null when added to postConstructionCollections).
    /// In Statement mode, returns a complete statement block.
    /// </summary>
    private string? ApplyCoercionNullHandlingCore(
        IPropertySymbol destProp, string sourceExpr, string collLocal,
        string coercionExpr, CodeEmissionMode mode, string? destVarName,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        List<(string DestPropName, string Block)>? postConstructionCollections)
    {
        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
        var nullFallback = GenerateCollectionNullFallback(destProp, strategy, sourceExpr);

        if (mode == CodeEmissionMode.Expression)
        {
            var isInitOnly = destProp.SetMethod?.IsInitOnly == true;

            if (strategy == 1 && !isInitOnly && postConstructionCollections != null)
            {
                // SkipNull: post-construction if-guard
                var block = new StringBuilder();
                block.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
                block.AppendLine($"            {{");
                block.AppendLine($"                result.{destProp.Name} = {coercionExpr};");
                block.Append($"            }}");
                postConstructionCollections.Add((destProp.Name, block.ToString()));
                return null;
            }

            if (isInitOnly && (strategy == 1))
            {
                // Init-only + SkipNull — must provide value, fall back to NullForgiving
                return $"{sourceExpr} is {{ }} {collLocal} ? {coercionExpr} : null!";
            }

            return $"{sourceExpr} is {{ }} {collLocal} ? {coercionExpr} : {nullFallback}";
        }
        else // Statement mode
        {
            if (strategy == 1) // SkipNull
            {
                return $"            if ({sourceExpr} is {{ }} {collLocal})\n" +
                       $"            {{\n" +
                       $"                {destVarName}.{destProp.Name} = {coercionExpr};\n" +
                       $"            }}";
            }

            return $"            {destVarName}.{destProp.Name} = {sourceExpr} is {{ }} {collLocal} ? {coercionExpr} : {nullFallback};";
        }
    }
}
