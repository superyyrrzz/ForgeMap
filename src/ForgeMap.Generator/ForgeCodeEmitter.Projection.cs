using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    /// <summary>
    /// Tries to generate a per-property LINQ projection expression for v1.7 SelectProperty.
    /// Returns the assignment expression (or null if a multi-statement post-construction block was added).
    /// Returns ProjectionEmitResult.NotApplicable when the property has no SelectProperty mapping.
    /// Returns ProjectionEmitResult.Failed when validation produced an error diagnostic.
    /// </summary>
    private ProjectionEmitResult TryGenerateProjectionAssignment(
        IPropertySymbol destProp,
        string sourceParam,
        INamedTypeSymbol sourceType,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> selectPropertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        Dictionary<string, (string? MethodName, string? ConverterTypeName, INamedTypeSymbol? ConverterTypeSymbol)>? propertyConvertWithMappings,
        Dictionary<string, int> nullPropertyHandlingOverrides,
        SourceProductionContext context,
        IMethodSymbol method,
        List<(string DestPropName, string Block)>? postConstructionCollections)
    {
        if (!selectPropertyMappings.TryGetValue(destProp.Name, out var memberName))
            return ProjectionEmitResult.NotApplicable();

        var location = method.Locations.FirstOrDefault();

        // FM0058: SelectProperty conflicts with ConvertWith / ConvertWithType on same [ForgeProperty]
        var hasConvertWith = propertyConvertWithMappings != null
            && propertyConvertWithMappings.TryGetValue(destProp.Name, out var cw)
            && (!string.IsNullOrEmpty(cw.MethodName) || !string.IsNullOrEmpty(cw.ConverterTypeName));
        if (hasConvertWith)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyConflictsWithConverter,
                location, destProp.Name);
            return ProjectionEmitResult.Failed();
        }

        // FM0072: SelectProperty conflicts with [ForgeFrom] / [ForgeWith] on same destination
        if (resolverMappings.ContainsKey(destProp.Name) || forgeWithMappings.ContainsKey(destProp.Name))
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyConflictsWithForgeFromOrWith,
                location, destProp.Name);
            return ProjectionEmitResult.Failed();
        }

        // Resolve source path: must come from [ForgeProperty] mapping (we already required src/dest names)
        if (!propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
        {
            // Should not happen — [ForgeProperty] always populates propertyMappings.
            return ProjectionEmitResult.NotApplicable();
        }

        var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceType);
        if (sourceLeafType == null)
            return ProjectionEmitResult.NotApplicable();

        // FM0055: source must be enumerable.
        // Walk IEnumerable<T> on AllInterfaces as a fallback so user-defined sequences
        // (ImmutableArray<T>, custom IEnumerable<T> implementers) are recognized — not
        // only the wrapper set baked into GetCollectionElementType.
        var srcElemType = GetCollectionElementType(sourceLeafType) ?? GetIEnumerableElementType(sourceLeafType);
        if (srcElemType == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertySourceNotEnumerable,
                location, destProp.Name, sourceLeafType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            return ProjectionEmitResult.Failed();
        }

        // FM0073: dest must be enumerable
        var destElemType = GetCollectionElementType(destProp.Type) ?? (destProp.Type is IArrayTypeSymbol arr ? arr.ElementType : null);
        if (destElemType == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyDestinationNotEnumerable,
                location, destProp.Name, destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            return ProjectionEmitResult.Failed();
        }

        // FM0056: SelectProperty member must exist on element type as public readable property.
        // Both the property and its getter must be public — `public string Name { private get; set; }`
        // would otherwise pass DeclaredAccessibility but fail to compile when emitted.
        var elemMember = srcElemType
            .GetMembers(memberName)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic
                && p.DeclaredAccessibility == Accessibility.Public
                && p.GetMethod != null
                && p.GetMethod.DeclaredAccessibility == Accessibility.Public);
        if (elemMember == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyMemberNotFound,
                location, memberName, srcElemType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat), destProp.Name);
            return ProjectionEmitResult.Failed();
        }

        // Compose lambda body. Default: __x.Member
        // Coercions: enum cast, string<->enum, enum->string, DateTimeOffset->DateTime
        var projectedType = elemMember.Type;
        var lambdaParam = "__x";
        var rawAccess = $"{lambdaParam}.{memberName}";

        string? lambdaBody = BuildProjectionLambdaBody(projectedType, destElemType, rawAccess);
        if (lambdaBody == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyElementTypeIncompatible,
                location, memberName,
                projectedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                destElemType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                destProp.Name);
            return ProjectionEmitResult.Failed();
        }

        // FM0059 — info diagnostic
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.SelectPropertyApplied,
            location, destProp.Name, sourcePropName, memberName);

        // Materialize: choose .ToList()/.ToArray()/new HashSet<>(...)/etc. based on destination wrapper
        var destElemDisplay = destElemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var collLocal = $"__sel_{destProp.Name}";
        var selectExpr = $"global::System.Linq.Enumerable.Select({collLocal}, {lambdaParam} => {lambdaBody})";
        var materialized = MaterializeProjectedSelect(destProp.Type, destElemDisplay, selectExpr);
        if (materialized == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.SelectPropertyDestinationNotEnumerable,
                location, destProp.Name, destProp.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
            return ProjectionEmitResult.Failed();
        }

        var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceType);
        var strategy = ResolveNullPropertyHandling(destProp.Name, nullPropertyHandlingOverrides);
        var isInitOnly = destProp.SetMethod?.IsInitOnly == true;

        // SkipNull on non-init-only: emit post-construction guarded assignment
        if (strategy == 1 && !isInitOnly && postConstructionCollections != null)
        {
            var block = new StringBuilder();
            block.AppendLine($"            if ({sourceExpr} is {{ }} {collLocal})");
            block.AppendLine($"            {{");
            block.AppendLine($"                result.{destProp.Name} = {materialized};");
            block.Append($"            }}");
            postConstructionCollections.Add((destProp.Name, block.ToString()));
            return ProjectionEmitResult.Skipped();
        }

        // Default: ternary with null fallback
        var nullFallback = ProjectionNullFallback(destProp.Type, strategy, destProp.Name, sourcePropName);
        return ProjectionEmitResult.FromExpression($"{sourceExpr} is {{ }} {collLocal} ? {materialized} : {nullFallback}");
    }

    /// <summary>
    /// Builds the lambda body expression for projection (the part after `__x =>`).
    /// Returns null when no compatible coercion path exists.
    /// </summary>
    private string? BuildProjectionLambdaBody(ITypeSymbol projectedType, ITypeSymbol destElemType, string rawAccess)
    {
        // 1) Direct assignment — but unwrap Nullable<T> -> T because Select preserves
        //    element type, so emitting x.Id as-is for int? -> int produces IEnumerable<int?>
        //    which won't materialize into List<int>.
        if (CanAssign(projectedType, destElemType))
        {
            var srcUnderlying = GetNullableUnderlyingType(projectedType);
            var destUnderlying = GetNullableUnderlyingType(destElemType);
            if (srcUnderlying != null && destUnderlying == null)
                return $"{rawAccess}.GetValueOrDefault()";
            return rawAccess;
        }

        // 2) Compatible enum cast (different namespaces, same members)
        var enumCast = TryGenerateCompatibleEnumCast(projectedType, destElemType, rawAccess);
        if (enumCast != null) return enumCast;

        // 3) string -> enum (honors _config.StringToEnum mode)
        if (_config.StringToEnum != 2 && IsStringToEnumPair(projectedType, destElemType))
        {
            var destEnumUnderlying = GetNullableUnderlyingType(destElemType) ?? destElemType;
            var enumFqn = $"global::{destEnumUnderlying.ToDisplayString()}";
            // For Nullable<TEnum> destination element, wrap with (TEnum?) so Select preserves
            // the nullable element type and the materialized List<TEnum?> assignment compiles.
            // On null/empty/parse-failure the failure value is `null`, not (TEnum?)0.
            var destIsNullable = GetNullableUnderlyingType(destElemType) != null;
            var nullableCast = destIsNullable ? $"({enumFqn}?)" : string.Empty;
            var failureValue = destIsNullable ? "null" : $"default({enumFqn})";
            // Mode 1 (TryParse): inline conditional, returns failureValue on failure
            if (_config.StringToEnum == 1)
            {
                return $"(global::System.Enum.TryParse<{enumFqn}>({rawAccess}, true, out var __sel_enum) ? {nullableCast}__sel_enum : {failureValue})";
            }
            // Mode 3 (StrictParse): no null guard, throws on null/empty/invalid
            if (_config.StringToEnum == 3)
            {
                return $"{nullableCast}({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {rawAccess}, true)";
            }
            // Mode 0 (Parse, default): null-safe guard returning failureValue for null/empty
            return $"(string.IsNullOrEmpty({rawAccess}) ? {failureValue} : {nullableCast}({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {rawAccess}, true))";
        }

        // 4) enum -> string
        if (IsEnumToStringPair(projectedType, destElemType))
        {
            var srcUnderlying = GetNullableUnderlyingType(projectedType);
            return srcUnderlying != null ? $"{rawAccess}?.ToString()" : $"{rawAccess}.ToString()";
        }

        // 5) DateTimeOffset -> DateTime
        var dto = TryGenerateDateTimeOffsetToDateTimeCoercion(projectedType, destElemType, rawAccess);
        if (dto != null) return dto;

        return null;
    }

    /// <summary>
    /// Materializes a Select(...) expression into the destination wrapper type.
    /// Returns null when the destination is not a supported enumerable wrapper.
    /// </summary>
    private static string? MaterializeProjectedSelect(ITypeSymbol destCollType, string destElemDisplay, string selectExpr)
    {
        if (destCollType is IArrayTypeSymbol)
            return $"global::System.Linq.Enumerable.ToArray({selectExpr})";

        if (destCollType is INamedTypeSymbol destNamed && destNamed.IsGenericType)
        {
            var def = destNamed.OriginalDefinition.ToDisplayString();
            switch (def)
            {
                case "System.Collections.Generic.IEnumerable<T>":
                    return selectExpr;
                case "System.Collections.Generic.HashSet<T>":
                    return $"new global::System.Collections.Generic.HashSet<{destElemDisplay}>({selectExpr})";
                case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                    return $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{destElemDisplay}>(global::System.Linq.Enumerable.ToList({selectExpr}))";
                case "System.Collections.Generic.List<T>":
                case "System.Collections.Generic.IList<T>":
                case "System.Collections.Generic.ICollection<T>":
                case "System.Collections.Generic.IReadOnlyList<T>":
                case "System.Collections.Generic.IReadOnlyCollection<T>":
                    return $"global::System.Linq.Enumerable.ToList({selectExpr})";
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the element type T when <paramref name="type"/> implements <c>IEnumerable&lt;T&gt;</c>,
    /// or <c>null</c> otherwise. Used as a fallback for user-defined or unsupported-wrapper
    /// sequence types that <see cref="GetCollectionElementType"/> does not recognize.
    /// String is excluded — it implements IEnumerable&lt;char&gt; but is not a "collection" here.
    /// </summary>
    private static ITypeSymbol? GetIEnumerableElementType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
            return null;

        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return named.TypeArguments[0];
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.IsGenericType
                && iface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            {
                return iface.TypeArguments[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Computes the null-fallback expression used when the source collection is null.
    /// </summary>
    private static string ProjectionNullFallback(ITypeSymbol destCollType, int strategy, string destPropName, string sourcePath)
    {
        // strategy: 0 NullForgiving, 1 SkipNull (handled upstream), 2 CoalesceToDefault, 3 Throw, 4 CoalesceToNew
        var destDisplay = destCollType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (strategy == 2 || strategy == 4)
        {
            // Try to construct an empty collection of the destination type.
            if (destCollType is IArrayTypeSymbol arr)
                return $"global::System.Array.Empty<{arr.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>()";
            if (destCollType is INamedTypeSymbol n && n.IsGenericType)
            {
                var def = n.OriginalDefinition.ToDisplayString();
                var elemDisplay = n.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                switch (def)
                {
                    case "System.Collections.Generic.IEnumerable<T>":
                        return $"global::System.Linq.Enumerable.Empty<{elemDisplay}>()";
                    case "System.Collections.Generic.IList<T>":
                    case "System.Collections.Generic.ICollection<T>":
                    case "System.Collections.Generic.IReadOnlyList<T>":
                    case "System.Collections.Generic.IReadOnlyCollection<T>":
                    case "System.Collections.Generic.List<T>":
                        return $"new global::System.Collections.Generic.List<{elemDisplay}>()";
                    case "System.Collections.Generic.HashSet<T>":
                        return $"new global::System.Collections.Generic.HashSet<{elemDisplay}>()";
                    case "System.Collections.ObjectModel.ReadOnlyCollection<T>":
                        return $"new global::System.Collections.ObjectModel.ReadOnlyCollection<{elemDisplay}>(new global::System.Collections.Generic.List<{elemDisplay}>())";
                }
            }
            return $"new {destDisplay}()";
        }
        if (strategy == 3) // Throw
        {
            return $"throw new global::System.ArgumentNullException(\"{destPropName}\", \"SelectProperty source collection '{sourcePath}' is null and NullPropertyHandling = ThrowException.\")";
        }
        // NullForgiving (default) or SkipNull-on-init-only fallback
        return "null!";
    }
}

internal readonly struct ProjectionEmitResult
{
    private ProjectionEmitResult(bool applicable, bool failed, bool skipped, string? expression)
    {
        Applicable = applicable;
        DidFail = failed;
        WasSkipped = skipped;
        Expression = expression;
    }

    public bool Applicable { get; }
    public bool DidFail { get; }
    public bool WasSkipped { get; }
    public string? Expression { get; }

    public static ProjectionEmitResult NotApplicable() => new(false, false, false, null);
    public static ProjectionEmitResult Failed() => new(true, true, false, null);
    public static ProjectionEmitResult Skipped() => new(true, false, true, null);
    public static ProjectionEmitResult FromExpression(string expr) => new(true, false, false, expr);
}
