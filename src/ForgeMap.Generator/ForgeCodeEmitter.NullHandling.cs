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
                return $"{sourceExpr} ?? throw new global::System.ArgumentNullException(\"{sourceExpr}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\")";

            default:
                return $"{sourceExpr}!";
        }
    }
}
