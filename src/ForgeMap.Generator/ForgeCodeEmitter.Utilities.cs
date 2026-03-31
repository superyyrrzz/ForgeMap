using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private static string GenerateSafeVariableName(ITypeSymbol type)
    {
        var name = type.Name;
        if (string.IsNullOrEmpty(name))
            return "value";

        // camelCase the type name
        var varName = char.ToLowerInvariant(name[0]) + name.Substring(1);

        // Use SyntaxFacts to detect all reserved and contextual keywords
        if (Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetKeywordKind(varName) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None
            || Microsoft.CodeAnalysis.CSharp.SyntaxFacts.GetContextualKeywordKind(varName) != Microsoft.CodeAnalysis.CSharp.SyntaxKind.None)
        {
            varName = "@" + varName;
        }

        return varName;
    }

    private static string GetMethodSignatureKey(IMethodSymbol method)
    {
        var paramTypes = string.Join(",", method.Parameters.Select(p => GetNullabilityNormalizedDisplayString(p.Type)));
        return $"{method.Name}({paramTypes}):{GetNullabilityNormalizedDisplayString(method.ReturnType)}";
    }

    /// <summary>
    /// Returns a display string for a type with nullable annotations stripped,
    /// so that signature comparisons are not affected by nullability differences.
    /// </summary>
    private static string GetNullabilityNormalizedDisplayString(ITypeSymbol type)
    {
        return type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString();
    }

    private static string GetAccessibilityKeyword(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "internal"
        };
    }

    private (string Expression, bool HasNullConditional) GenerateSourceExpressionWithNullInfo(string sourceParam, string sourcePath, INamedTypeSymbol sourceType)
    {
        if (!sourcePath.Contains("."))
        {
            // Use symbol name when available for defense-in-depth
            var simpleProp = GetMappableProperties(sourceType).FirstOrDefault(p => p.Name == sourcePath);
            var safeName = simpleProp?.Name ?? sourcePath;
            return ($"{sourceParam}.{safeName}", false);
        }

        // Handle nested path (e.g., "Customer.Name")
        var parts = sourcePath.Split('.');
        var expression = new StringBuilder(sourceParam);
        var currentType = sourceType;
        var hasNullConditional = false;

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var prop = GetMappableProperties(currentType).FirstOrDefault(p => p.Name == part);

            if (prop == null)
            {
                // Property not found, just append directly
                expression.Append($".{part}");
                continue;
            }

            // Add null-conditional for reference types (except the last property)
            if (i < parts.Length - 1 && prop.Type.IsReferenceType)
            {
                expression.Append($".{prop.Name}?");
                hasNullConditional = true;
            }
            else
            {
                expression.Append($".{prop.Name}");
            }

            if (prop.Type is INamedTypeSymbol namedType)
            {
                currentType = namedType;
            }
        }

        return (expression.ToString(), hasNullConditional);
    }

    /// <summary>
    /// Resolves the leaf property type for a potentially nested path.
    /// Returns null if the path cannot be fully resolved.
    /// </summary>
    private ITypeSymbol? ResolvePathLeafType(string sourcePath, INamedTypeSymbol sourceType)
    {
        var parts = sourcePath.Split('.');
        ITypeSymbol currentType = sourceType;

        foreach (var part in parts)
        {
            if (currentType is not INamedTypeSymbol namedType)
                return null;

            var prop = GetMappableProperties(namedType).FirstOrDefault(p => p.Name == part);
            if (prop == null)
                return null;

            currentType = prop.Type;
        }

        return currentType;
    }
}
