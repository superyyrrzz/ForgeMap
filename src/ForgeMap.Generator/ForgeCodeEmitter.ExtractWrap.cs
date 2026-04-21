using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
#pragma warning disable IDE0051 // Remove unused private members — wired up in tasks 5-7
    /// <summary>
    /// Generates the body of a partial method annotated with [ExtractProperty] or [WrapProperty].
    /// Validates that no conflicting method-level attributes are present (FM0065), the signature
    /// is exactly one parameter with a non-void return (FM0070), and emits per-attribute body
    /// via <see cref="GenerateExtractPropertyBody"/> / <see cref="GenerateWrapPropertyBody"/>.
    /// Returns string.Empty when validation fails — the partial then stays unimplemented and
    /// the user sees the diagnostic plus a CS8795 (must declare body) from the C# compiler.
    /// </summary>
    private string GenerateExtractWrapMethod(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var hasExtract = HasExtractPropertyAttribute(method);
        var hasWrap = HasWrapPropertyAttribute(method);
        if (!hasExtract && !hasWrap)
            return string.Empty;

        // FM0065: conflict with [ConvertWith], [ForgeFrom], [ForgeWith], [ForgeProperty]
        var hasConvertWith = HasConvertWithAttribute(method);
        var hasForgeFrom = GetResolverMappings(method).Count > 0;
        var hasForgeWith = GetForgeWithMappings(method).Count > 0;
        var hasForgeProperty = GetPropertyMappings(method).Count > 0;
        if (hasConvertWith || hasForgeFrom || hasForgeWith || hasForgeProperty || (hasExtract && hasWrap))
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractWrapConflictsWithMethodAttributes,
                method.Locations.FirstOrDefault(),
                method.Name);
            return string.Empty;
        }

        // FM0070: exactly one parameter, non-void return
        if (method.Parameters.Length != 1 || method.ReturnsVoid)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractWrapInvalidSignature,
                method.Locations.FirstOrDefault(),
                method.Name);
            return string.Empty;
        }

        // [BeforeForge]/[AfterForge] are not applicable on extract/wrap methods.
        // Re-use the established helper that already runs for [ConvertWith].
        ReportHooksNotSupportedIfPresent(method, context);

        if (hasExtract)
            return GenerateExtractPropertyBody(method, forger, context);

        return GenerateWrapPropertyBody(method, forger, context);
    }

    /// <summary>
    /// Emits the body of an [ExtractProperty]-annotated partial method. Validates the named
    /// source property exists and is publicly readable (FM0066), and that its type is
    /// assignable (directly or via built-in coercion) to the return type (FM0067).
    /// Honors _config.NullHandling for the source-null guard. Emits FM0074 (info, disabled
    /// by default) when the return type is a value type under ReturnNull semantics.
    /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter — 'forger' kept for symmetry with WrapPropertyBody
    private string GenerateExtractPropertyBody(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var propertyName = GetExtractPropertyName(method);
        if (string.IsNullOrEmpty(propertyName))
            return string.Empty;

        var sourceParam = method.Parameters[0];
        var sourceType = sourceParam.Type;
        var returnType = method.ReturnType;

        // FM0066: find a public, readable instance property
        var srcProp = sourceType.GetMembers(propertyName!)
            .OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic
                && p.GetMethod != null
                && p.DeclaredAccessibility == Accessibility.Public);

        if (srcProp == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractPropertyNotFound,
                method.Locations.FirstOrDefault(),
                propertyName!,
                sourceType.ToDisplayString(),
                method.Name);
            return string.Empty;
        }

        // FM0067: type compatibility — direct, then built-in coercions.
        var rawAccess = $"{sourceParam.Name}.{srcProp.Name}";
        if (!TryCoerceForExtract(srcProp.Type, returnType, rawAccess, out var returnExpression))
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractPropertyTypeIncompatible,
                method.Locations.FirstOrDefault(),
                srcProp.Type.ToDisplayString(),
                returnType.ToDisplayString(),
                method.Name);
            return string.Empty;
        }

        // FM0074: value-type return under ReturnNull collapses null sources to default.
        // Only emit when source can be null (reference type or Nullable<T>).
        var sourceCanBeNull = sourceType.IsReferenceType
            || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        if (sourceCanBeNull && returnType.IsValueType && _config.NullHandling == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractWrapValueTypeReturnUnderReturnNull,
                method.Locations.FirstOrDefault(),
                method.Name,
                returnType.ToDisplayString());
        }

        // Emit body.
        var sb = new StringBuilder();
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var returnDisplay = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sourceDisplay = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine($"        {accessibility} partial {returnDisplay} {method.Name}({sourceDisplay} {sourceParam.Name})");
        sb.AppendLine("        {");

        if (sourceCanBeNull)
        {
            var nullReturn = returnType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam.Name, nullReturn));
        }

        sb.AppendLine($"            return {returnExpression};");
        sb.AppendLine("        }");
        return sb.ToString();
    }
#pragma warning restore IDE0060

    /// <summary>
    /// Decides how to express <paramref name="rawAccess"/> (an expression of <paramref name="sourcePropType"/>)
    /// as a value of <paramref name="targetType"/>. Returns true and sets <paramref name="expression"/> when
    /// a direct or built-in-coercion path exists; returns false otherwise.
    /// Coercion ladder: direct (CanAssign) → DateTimeOffset→DateTime → string↔enum.
    /// </summary>
    private static bool TryCoerceForExtract(
        ITypeSymbol sourcePropType,
        ITypeSymbol targetType,
        string rawAccess,
        out string expression)
    {
        // 1) Direct assignability (covers nullability widening/narrowing and Nullable<T> ↔ T).
        if (CanAssign(sourcePropType, targetType))
        {
            expression = rawAccess;
            return true;
        }

        // 2) DateTimeOffset → DateTime (existing helper already handles nullable forms).
        var dto = TryGenerateDateTimeOffsetToDateTimeCoercion(sourcePropType, targetType, rawAccess);
        if (dto != null)
        {
            expression = dto;
            return true;
        }

        // 3) string → enum.
        if (sourcePropType.SpecialType == SpecialType.System_String && targetType.TypeKind == TypeKind.Enum)
        {
            var enumFqn = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            expression = $"string.IsNullOrEmpty({rawAccess}) ? default({enumFqn}) : ({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {rawAccess}, true)";
            return true;
        }

        // 4) enum → string.
        if (sourcePropType.TypeKind == TypeKind.Enum && targetType.SpecialType == SpecialType.System_String)
        {
            expression = $"{rawAccess}.ToString()";
            return true;
        }

        expression = string.Empty;
        return false;
    }

    /// <summary>
    /// Stub — implemented in Task 6.
    /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter — parameters used in Task 6
    private string GenerateWrapPropertyBody(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        return string.Empty;
    }
#pragma warning restore IDE0060
#pragma warning restore IDE0051
}
