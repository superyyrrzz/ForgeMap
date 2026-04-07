using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    /// <summary>
    /// Validates a BeforeForge hook method. Must be void with a single parameter matching the source type.
    /// Returns the validated symbol name on success, or null on failure.
    /// </summary>
    private string? ValidateBeforeForgeHook(
        string hookMethodName,
        ITypeSymbol sourceType,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var candidates = forger.Symbol.GetMembers(hookMethodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.ReturnsVoid && m.Parameters.Length == 1 &&
                !m.IsGenericMethod &&
                m.Parameters[0].RefKind == RefKind.None &&
                !m.Parameters[0].IsParams &&
                (SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType) ||
                 CanAssign(sourceType, m.Parameters[0].Type)))
            .ToList();

        if (candidates.Count == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.HookMethodInvalid,
                method.Locations.FirstOrDefault(),
                hookMethodName);
            return null;
        }

        // Prefer exact type match over assignable match
        var exactMatch = candidates.FirstOrDefault(m =>
            SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType));

        if (exactMatch != null)
            return exactMatch.Name;

        if (candidates.Count == 1)
            return candidates[0].Name;

        // Multiple assignable candidates with no exact match — ambiguous
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.HookMethodInvalid,
            method.Locations.FirstOrDefault(),
            hookMethodName);
        return null;
    }

    /// <summary>
    /// Validates an AfterForge hook method. Must be void with two parameters: source type and destination type.
    /// Returns the validated symbol name on success, or null on failure.
    /// </summary>
    private string? ValidateAfterForgeHook(
        string hookMethodName,
        ITypeSymbol sourceType,
        ITypeSymbol destType,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var candidates = forger.Symbol.GetMembers(hookMethodName)
            .OfType<IMethodSymbol>()
            .Where(m => m.ReturnsVoid && m.Parameters.Length == 2 &&
                !m.IsGenericMethod &&
                m.Parameters[0].RefKind == RefKind.None &&
                !m.Parameters[0].IsParams &&
                m.Parameters[1].RefKind == RefKind.None &&
                !m.Parameters[1].IsParams &&
                (SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType) ||
                 CanAssign(sourceType, m.Parameters[0].Type)) &&
                (SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, destType) ||
                 CanAssign(destType, m.Parameters[1].Type)))
            .ToList();

        if (candidates.Count == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.HookMethodInvalid,
                method.Locations.FirstOrDefault(),
                hookMethodName);
            return null;
        }

        // Prefer exact type match over assignable match
        var exactMatch = candidates.FirstOrDefault(m =>
            SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType) &&
            SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, destType));

        if (exactMatch != null)
            return exactMatch.Name;

        if (candidates.Count == 1)
            return candidates[0].Name;

        // Multiple assignable candidates with no exact match — ambiguous
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.HookMethodInvalid,
            method.Locations.FirstOrDefault(),
            hookMethodName);
        return null;
    }

    /// <summary>
    /// Reports FM0018 if [BeforeForge] or [AfterForge] attributes are present on a method
    /// that does not support hooks (enum or collection forge methods).
    /// </summary>
    private void ReportHooksNotSupportedIfPresent(IMethodSymbol method, SourceProductionContext context)
    {
        var hasHooks = (_beforeForgeAttributeSymbol != null && method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _beforeForgeAttributeSymbol))) ||
            (_afterForgeAttributeSymbol != null && method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _afterForgeAttributeSymbol)));

        if (hasHooks)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.HooksNotSupportedOnMethodKind,
                method.Locations.FirstOrDefault());
        }
    }

    /// <summary>
    /// Finds a ForgeInto (void mutation) method on the forger class that accepts the given source property type
    /// and has a [UseExistingValue] parameter matching the destination property type.
    /// Used for nested existing-target mapping.
    /// </summary>
    private IMethodSymbol? FindForgeIntoMethod(INamedTypeSymbol forgerType, ITypeSymbol sourcePropertyType, ITypeSymbol destPropertyType)
    {
        return forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.IsPartialDefinition &&
                m.ReturnsVoid &&
                m.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourcePropertyType) &&
                HasUseExistingValueAttribute(m.Parameters[1]) &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, destPropertyType));
    }

    /// <summary>
    /// Finds all ForgeInto (void mutation) method candidates on the forger class for auto-wiring.
    /// </summary>
    private List<IMethodSymbol> FindAutoWireForgeIntoMethodCandidates(INamedTypeSymbol forgerType, ITypeSymbol sourcePropertyType, ITypeSymbol destPropertyType)
    {
        return forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.IsPartialDefinition &&
                m.ReturnsVoid &&
                m.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourcePropertyType) &&
                HasUseExistingValueAttribute(m.Parameters[1]) &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, destPropertyType))
            .ToList();
    }

    /// <summary>
    /// Finds a forging method on the forger class that accepts the given source type and returns the given destination type.
    /// </summary>
    private static IMethodSymbol? FindForgingMethod(INamedTypeSymbol forgerType, string methodName, ITypeSymbol sourcePropertyType, ITypeSymbol destPropertyType)
    {
        return forgerType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m =>
                m.IsPartialDefinition &&
                m.Parameters.Length == 1 &&
                !m.ReturnsVoid &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourcePropertyType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, destPropertyType));
    }

    /// <summary>
    /// Finds all partial forge method candidates on the forger class that accept the given source type
    /// and return the given destination type. Used for auto-wiring nested properties.
    /// </summary>
    private static List<IMethodSymbol> FindAutoWireForgeMethodCandidates(
        INamedTypeSymbol forgerType, ITypeSymbol sourcePropertyType, ITypeSymbol destPropertyType)
    {
        return forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.IsPartialDefinition &&
                m.Parameters.Length == 1 &&
                !m.ReturnsVoid &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourcePropertyType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, destPropertyType))
            .ToList();
    }

    /// <summary>
    /// Finds forge method candidates for FM0026 reverse checking, including both partial definitions
    /// and non-partial methods (explicit reverse implementations provided by the user).
    /// </summary>
    private static List<IMethodSymbol> FindReverseForgeMethodCandidates(
        INamedTypeSymbol forgerType, ITypeSymbol sourcePropertyType, ITypeSymbol destPropertyType)
    {
        return forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.Parameters.Length == 1 &&
                !m.ReturnsVoid &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourcePropertyType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, destPropertyType))
            .ToList();
    }

    /// <summary>
    /// Finds a resolver method in the forger class.
    /// Prefers exact type matches over assignable matches for deterministic overload selection.
    /// </summary>
    private IMethodSymbol? FindResolverMethod(INamedTypeSymbol forgerType, string methodName, ITypeSymbol sourceType, ITypeSymbol? preferredParamType)
    {
        var candidates = forgerType.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(m => !m.ReturnsVoid && m.Parameters.Length == 1)
            .ToList();

        if (candidates.Count == 0)
            return null;

        // Prefer Method(TPreferredType) if preferredParamType is provided
        if (preferredParamType != null)
        {
            // First try exact match
            var exactMatch = candidates.FirstOrDefault(m =>
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, preferredParamType));
            if (exactMatch != null)
                return exactMatch;

            // Then try assignable match
            var assignableMatch = candidates.FirstOrDefault(m =>
                CanAssign(preferredParamType, m.Parameters[0].Type));
            if (assignableMatch != null)
                return assignableMatch;
        }

        // Fall back to Method(TSource) - prefer exact match first
        var exactSourceMatch = candidates.FirstOrDefault(m =>
            SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType));
        if (exactSourceMatch != null)
            return exactSourceMatch;

        var assignableSourceMatch = candidates.FirstOrDefault(m =>
            CanAssign(sourceType, m.Parameters[0].Type));

        return assignableSourceMatch ?? candidates.FirstOrDefault();
    }

    /// <summary>
    /// Discovers all forge methods in the same forger class whose source parameter type is a class
    /// that derives from the base source type and whose return type is assignable to the base return
    /// type (including interface implementation and nullable reference type variations). Results are
    /// ordered most-derived first; ties broken alphabetically by fully qualified name.
    /// </summary>
    private static List<IMethodSymbol> DiscoverDerivedForgeMethods(
        IMethodSymbol baseMethod,
        INamedTypeSymbol baseSourceType,
        INamedTypeSymbol baseDestinationType,
        ForgerInfo forger)
    {
        var candidates = new List<(IMethodSymbol Method, int Depth)>();

        foreach (var member in forger.Symbol.GetMembers().OfType<IMethodSymbol>())
        {
            // Must be a partial definition with matching method name, one parameter, non-void return
            if (!member.IsPartialDefinition || member.Parameters.Length != 1 || member.ReturnsVoid)
                continue;

            // Skip the base method itself
            if (SymbolEqualityComparer.Default.Equals(member, baseMethod))
                continue;

            var memberSourceType = member.Parameters[0].Type as INamedTypeSymbol;
            var memberReturnType = member.ReturnType as INamedTypeSymbol;
            if (memberSourceType == null || memberReturnType == null)
                continue;

            // Source param type must derive from base source type (class inheritance only;
            // DerivesFrom also matches interfaces, which would break GetInheritanceDepth ordering)
            if (!ClassDerivesFrom(memberSourceType, baseSourceType))
                continue;

            // Return type must be assignable to the base return type (supports interfaces and NRT)
            if (!CanAssign(memberReturnType, baseDestinationType))
                continue;

            // Method name must match the base method
            if (member.Name != baseMethod.Name)
                continue;

            var depth = GetInheritanceDepth(memberSourceType, baseSourceType);
            candidates.Add((member, depth));
        }

        // Sort: most-derived first (highest depth), then alphabetical by source type FQN
        candidates.Sort((a, b) =>
        {
            var depthCmp = b.Depth.CompareTo(a.Depth);
            if (depthCmp != 0) return depthCmp;
            var aName = a.Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var bName = b.Method.Parameters[0].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return string.Compare(aName, bName, StringComparison.Ordinal);
        });

        return candidates.Select(c => c.Method).ToList();
    }

    /// <summary>
    /// Finds all element-level forge methods on the forger class that match a given
    /// source → destination element type pair. Used for standalone collection method resolution.
    /// Excludes methods whose parameter and return types are both collection types
    /// (those are collection methods themselves, not element methods).
    /// </summary>
    private static List<IMethodSymbol> FindElementForgeMethodsByType(
        INamedTypeSymbol forgerType, ITypeSymbol sourceElementType, ITypeSymbol destElementType)
    {
        return forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.IsPartialDefinition &&
                m.Parameters.Length == 1 &&
                !m.ReturnsVoid &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceElementType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, destElementType) &&
                // Exclude methods that are themselves collection methods
                GetCollectionElementType(m.Parameters[0].Type) == null &&
                GetCollectionElementType(m.ReturnType) == null)
            .ToList();
    }

    /// <summary>
    /// Returns true if <paramref name="derived"/> inherits from <paramref name="baseType"/>
    /// via the BaseType chain only (excludes interface implementation).
    /// Used by <see cref="DiscoverDerivedForgeMethods"/> where <see cref="GetInheritanceDepth"/>
    /// requires a class hierarchy.
    /// </summary>
    private static bool ClassDerivesFrom(INamedTypeSymbol derived, INamedTypeSymbol baseType)
    {
        var current = derived.BaseType;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    /// Counts how many steps from <paramref name="derived"/> up the BaseType chain to reach <paramref name="baseType"/>.
    /// Returns 0 if they are the same type.
    /// </summary>
    private static int GetInheritanceDepth(INamedTypeSymbol derived, INamedTypeSymbol baseType)
    {
        int depth = 0;
        var current = derived;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return depth;
            depth++;
            current = current.BaseType;
        }
        return depth; // returns depth to the end of the hierarchy if baseType is not found (callers should ensure ClassDerivesFrom is true)
    }

    /// <summary>
    /// Returns true if the type is a class, struct, or record suitable for object-initializer-based
    /// reverse code generation. Returns false for enums, primitives, strings, delegates, collections,
    /// and other types that would produce invalid code like <c>return new int { };</c>.
    /// </summary>
    private bool IsReversibleObjectType(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum || type.TypeKind == TypeKind.Delegate)
            return false;

        // SpecialType covers all CLR primitives (int, bool, decimal, etc.), string, DateTime, etc.
        if (type.SpecialType != SpecialType.None)
            return false;

        if (GetCollectionElementType(type) != null)
            return false;

        // Must be a class, struct, or record (TypeKind.Class or TypeKind.Struct)
        return type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct;
    }
}
