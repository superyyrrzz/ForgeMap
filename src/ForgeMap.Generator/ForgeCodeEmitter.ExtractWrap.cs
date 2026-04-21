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
    /// Stub — implemented in Task 5.
    /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter — parameters used in Task 5
    private string GenerateExtractPropertyBody(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        return string.Empty;
    }
#pragma warning restore IDE0060

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
