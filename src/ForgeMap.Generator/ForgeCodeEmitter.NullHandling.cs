using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private void ReportFM0007(
        SourceProductionContext context,
        IMethodSymbol method,
        string sourceTypeName,
        string sourcePropertyName,
        string destTypeName,
        string destPropertyName)
    {
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.NullableToNonNullableMapping,
            method.Locations.FirstOrDefault(),
            sourceTypeName, sourcePropertyName, destTypeName, destPropertyName);
    }

    /// <summary>
    /// Resolves the effective NullPropertyHandling strategy for a destination property.
    /// Priority: per-property override > forger config > assembly default.
    /// </summary>
    private int ResolveNullPropertyHandling(string destPropertyName, Dictionary<string, int> overrides)
    {
        if (overrides.TryGetValue(destPropertyName, out var perProperty))
            return perProperty;
        return _config.NullPropertyHandling;
    }

    /// <summary>
    /// Applies NullPropertyHandling strategy to a property assignment expression.
    /// Returns the modified expression, or null when the strategy is SkipNull (which requires separate statement handling by the caller).
    /// </summary>
    private string? ApplyNullPropertyHandlingExpression(
        string sourceExpr,
        ITypeSymbol destType,
        string destPropertyName,
        string destTypeName,
        int strategy)
    {
        switch (strategy)
        {
            case 0: // NullForgiving
                return $"{sourceExpr}!";

            case 1: // SkipNull — cannot be expressed as a single expression in object initializer
                return null;

            case 2: // CoalesceToDefault
                var defaultExpr = GenerateCoalesceDefault(destType);
                if (defaultExpr != null)
                    return $"{sourceExpr} ?? {defaultExpr}";
                // Fallback to NullForgiving — caller already reported FM0007
                return $"{sourceExpr}!";

            case 3: // ThrowException
                return $"{sourceExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\")";

            case 4: // CoalesceToNew — same expression as CoalesceToDefault, but FM0038 validation done by caller
                var newExpr = GenerateCoalesceDefault(destType);
                if (newExpr != null)
                    return $"{sourceExpr} ?? {newExpr}";
                // FM0038 should have already been reported; fall back to NullForgiving
                return $"{sourceExpr}!";

            default:
                return $"{sourceExpr}!";
        }
    }

    /// <summary>
    /// Validates that CoalesceToNew can synthesize a default for the given destination type.
    /// Reports FM0038 if the type is a non-collection reference type without an accessible parameterless constructor.
    /// </summary>
    private void ValidateCoalesceToNew(
        ITypeSymbol destType,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        if (destType.IsValueType) return;
        if (destType.SpecialType == SpecialType.System_String) return;
        if (destType is IArrayTypeSymbol) return;
        if (GenerateEmptyCollectionExpression(destType) != null) return;

        if (destType is INamedTypeSymbol namedType)
        {
            if (namedType.IsAbstract || namedType.TypeKind == TypeKind.Interface)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CoalesceToNewNoConstructor,
                    method.Locations.FirstOrDefault(),
                    namedType.ToDisplayString());
                return;
            }

            var hasParameterlessCtor = namedType.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= Accessibility.Internal);
            if (!hasParameterlessCtor)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CoalesceToNewNoConstructor,
                    method.Locations.FirstOrDefault(),
                    namedType.ToDisplayString());
                return;
            }

            // Check for uninitialized required members (C# 11+)
            var hasUninitializedRequired = namedType.GetMembers()
                .Any(m => m is IPropertySymbol prop && prop.IsRequired
                    || m is IFieldSymbol field && field.IsRequired);
            if (hasUninitializedRequired)
            {
                // Check if the parameterless constructor has [SetsRequiredMembers]
                var ctor = namedType.InstanceConstructors
                    .First(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= Accessibility.Internal);
                var hasSetsRequired = ctor.GetAttributes()
                    .Any(a => a.AttributeClass?.Name == "SetsRequiredMembersAttribute"
                        || a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
                if (!hasSetsRequired)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.CoalesceToNewNoConstructor,
                        method.Locations.FirstOrDefault(),
                        namedType.ToDisplayString());
                }
            }
        }
    }
}
