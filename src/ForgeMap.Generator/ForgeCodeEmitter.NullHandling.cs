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
        int strategy,
        IMethodSymbol? method = null)
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

            case 4: // CoalesceToNew — assembly-aware expression generation
                var newExpr = method != null
                    ? GenerateCoalesceNewExpression(destType, method)
                    : GenerateCoalesceDefault(destType);
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

            var sameAssembly = SymbolEqualityComparer.Default.Equals(
                namedType.ContainingAssembly, method.ContainingAssembly);
            var minAccessibility = sameAssembly ? Accessibility.Internal : Accessibility.Public;

            var hasParameterlessCtor = namedType.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= minAccessibility);
            if (!hasParameterlessCtor)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.CoalesceToNewNoConstructor,
                    method.Locations.FirstOrDefault(),
                    namedType.ToDisplayString());
                return;
            }

            // Check for uninitialized required members (C# 11+), including inherited ones
            var hasUninitializedRequired = HasUninitializedRequiredMembers(namedType);
            if (hasUninitializedRequired)
            {
                // Check if any accessible parameterless constructor has [SetsRequiredMembers]
                var hasSetsRequired = namedType.InstanceConstructors
                    .Where(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= minAccessibility)
                    .Any(c => c.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == "SetsRequiredMembersAttribute"
                            || a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"));
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

    /// <summary>
    /// Generates the CoalesceToNew fallback expression for a destination type.
    /// Uses GenerateCoalesceDefault first (public ctor), then falls back to assembly-aware
    /// internal ctor check. Returns null if no suitable expression can be generated.
    /// </summary>
    private static string? GenerateCoalesceNewExpression(ITypeSymbol destType, IMethodSymbol method)
    {
        // Try collection expressions first — handles interfaces like IReadOnlyList<T>
        var collExpr = GenerateEmptyCollectionExpression(destType);
        if (collExpr != null)
            return collExpr;

        var expr = GenerateCoalesceDefault(destType);
        if (expr != null)
        {
            // GenerateCoalesceDefault returns new T() for public ctors — verify no required members
            if (destType is INamedTypeSymbol publicNamedType && HasUninitializedRequiredMembers(publicNamedType))
            {
                var ctor = publicNamedType.InstanceConstructors
                    .FirstOrDefault(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
                if (ctor != null)
                {
                    var hasSetsRequired = ctor.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == "SetsRequiredMembersAttribute"
                            || a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
                    if (!hasSetsRequired)
                        return null; // FM0038 already reported by ValidateCoalesceToNew
                }
            }
            return expr;
        }

        // GenerateCoalesceDefault uses Public-only; check if internal ctor is accessible (same assembly)
        if (destType is INamedTypeSymbol namedType
            && !namedType.IsAbstract && namedType.TypeKind != TypeKind.Interface
            && SymbolEqualityComparer.Default.Equals(namedType.ContainingAssembly, method.ContainingAssembly))
        {
            // Check required members for internal ctors too
            if (HasUninitializedRequiredMembers(namedType))
            {
                var ctor = namedType.InstanceConstructors
                    .FirstOrDefault(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= Accessibility.Internal);
                if (ctor != null)
                {
                    var hasSetsRequired = ctor.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == "SetsRequiredMembersAttribute"
                            || a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
                    if (!hasSetsRequired)
                        return null;
                }
            }

            var hasInternalCtor = namedType.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility >= Accessibility.Internal);
            if (hasInternalCtor)
                return $"new {namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()";
        }

        return null;
    }

    /// <summary>
    /// Checks if a type or any of its base types have uninitialized required members.
    /// </summary>
    private static bool HasUninitializedRequiredMembers(INamedTypeSymbol type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol prop && prop.IsRequired)
                    return true;
                if (member is IFieldSymbol field && field.IsRequired)
                    return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Applies NullPropertyHandling to a Nullable&lt;T&gt; → T value type assignment in property initializers.
    /// Returns the expression string, or null when SkipNull applies (added to skipNullAssignments).
    /// </summary>
    private string? ApplyNullableValueTypeHandling(
        string sourceExpr,
        ITypeSymbol destType,
        string destPropertyName,
        string destTypeName,
        int strategy,
        List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)>? skipNullAssignments,
        IPropertySymbol destProp)
    {
        var destDisplay = destType.ToDisplayString();
        var isParenNeeded = sourceExpr.Contains("?.");

        switch (strategy)
        {
            case 0: // NullForgiving — existing behavior: forced unwrap
                return isParenNeeded
                    ? $"({destDisplay})({sourceExpr})!"
                    : $"({destDisplay}){sourceExpr}!";

            case 1: // SkipNull
                if (skipNullAssignments == null || destProp.SetMethod?.IsInitOnly == true)
                    return isParenNeeded
                        ? $"({destDisplay})({sourceExpr})!"
                        : $"({destDisplay}){sourceExpr}!";
                var localVar = $"__val_{destPropertyName}";
                skipNullAssignments.Add((destPropertyName, sourceExpr, localVar, null));
                return null;

            case 2: // CoalesceToDefault
                return $"{sourceExpr} ?? default({destDisplay})";

            case 3: // ThrowException
                return $"{sourceExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\")";

            case 4: // CoalesceToNew — for value types, same as CoalesceToDefault
                return $"{sourceExpr} ?? default({destDisplay})";

            default:
                return isParenNeeded
                    ? $"({destDisplay})({sourceExpr})!"
                    : $"({destDisplay}){sourceExpr}!";
        }
    }

    /// <summary>
    /// Applies NullPropertyHandling to a Nullable&lt;T&gt; → T value type for constructor parameters.
    /// SkipNull is not applicable for ctor params — callers should remap to NullForgiving before calling.
    /// </summary>
    private static string ApplyNullableValueTypeCtorHandling(
        string sourceExpr,
        ITypeSymbol destType,
        string destPropertyName,
        string destTypeName,
        int strategy)
    {
        var destDisplay = destType.ToDisplayString();
        var isParenNeeded = sourceExpr.Contains("?.");

        switch (strategy)
        {
            case 0: // NullForgiving
                return isParenNeeded
                    ? $"({destDisplay})({sourceExpr})!"
                    : $"({destDisplay}){sourceExpr}!";

            case 2: // CoalesceToDefault
                return $"{sourceExpr} ?? default({destDisplay})";

            case 3: // ThrowException
                return $"{sourceExpr} ?? throw new global::System.ArgumentNullException(\"{destPropertyName}\", \"Cannot assign null source property '{sourceExpr}' to non-nullable destination '{destTypeName}.{destPropertyName}'.\")";

            case 4: // CoalesceToNew — for value types, same as CoalesceToDefault
                return $"{sourceExpr} ?? default({destDisplay})";

            default:
                return isParenNeeded
                    ? $"({destDisplay})({sourceExpr})!"
                    : $"({destDisplay}){sourceExpr}!";
        }
    }
}
