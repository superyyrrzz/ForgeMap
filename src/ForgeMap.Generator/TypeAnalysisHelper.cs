using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ForgeMap.Generator;

/// <summary>
/// Static helper methods for type analysis, compatibility checking, and collection utilities.
/// Extracted from ForgeCodeEmitter to improve cohesion.
/// </summary>
internal static class TypeAnalysisHelper
{
    internal static readonly HashSet<string> CollectionTypesWithCheapCount = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.HashSet<T>"
    };

    internal static readonly HashSet<string> SupportedCollectionTypes = new(StringComparer.Ordinal)
    {
        "System.Collections.Generic.List<T>",
        "System.Collections.Generic.IList<T>",
        "System.Collections.Generic.ICollection<T>",
        "System.Collections.Generic.IEnumerable<T>",
        "System.Collections.Generic.IReadOnlyList<T>",
        "System.Collections.Generic.IReadOnlyCollection<T>",
        "System.Collections.Generic.HashSet<T>"
    };

    internal static bool CanAssign(ITypeSymbol source, ITypeSymbol dest)
    {
        // Simple type compatibility check
        if (SymbolEqualityComparer.Default.Equals(source, dest))
            return true;

        // Handle Nullable<T> to T (value types)
        var sourceUnderlying = GetNullableUnderlyingType(source);
        var destUnderlying = GetNullableUnderlyingType(dest);

        if (sourceUnderlying != null && destUnderlying == null)
        {
            // Nullable<T> -> T - allowed but may throw at runtime
            return SymbolEqualityComparer.Default.Equals(sourceUnderlying, dest);
        }

        if (sourceUnderlying == null && destUnderlying != null)
        {
            // T -> Nullable<T> - always allowed
            return SymbolEqualityComparer.Default.Equals(source, destUnderlying);
        }

        // Handle nullable reference types: nullable ref -> non-nullable ref
        if (source.NullableAnnotation == NullableAnnotation.Annotated &&
            dest.NullableAnnotation != NullableAnnotation.Annotated)
        {
            var underlyingSource = source.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return SymbolEqualityComparer.Default.Equals(underlyingSource, dest);
        }

        // Handle nullable reference types: non-nullable ref -> nullable ref (always valid)
        if (source.NullableAnnotation != NullableAnnotation.Annotated &&
            dest.NullableAnnotation == NullableAnnotation.Annotated)
        {
            var underlyingDest = dest.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return SymbolEqualityComparer.Default.Equals(source, underlyingDest);
        }

        // Handle inheritance
        var currentType = source;
        while (currentType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(currentType, dest))
                return true;
            currentType = currentType.BaseType;
        }

        // Handle interfaces
        foreach (var iface in source.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, dest))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if source and dest are enums with identical member names and values (in declaration order),
    /// enabling auto-cast between enums defined in different namespaces.
    /// </summary>
    internal static bool AreCompatibleEnums(ITypeSymbol source, ITypeSymbol dest)
    {
        if (source.TypeKind != TypeKind.Enum || dest.TypeKind != TypeKind.Enum)
            return false;

        // Already identical types — not a "compatible enum" case
        if (SymbolEqualityComparer.Default.Equals(source, dest))
            return false;

        var srcNamed = (INamedTypeSymbol)source;
        var dstNamed = (INamedTypeSymbol)dest;

        // Underlying types must match — boxed constant Equals() is type-sensitive
        // (e.g., ((int)0).Equals((byte)0) is false), so require same underlying type.
        if (srcNamed.EnumUnderlyingType?.SpecialType != dstNamed.EnumUnderlyingType?.SpecialType)
            return false;

        var sourceMembers = source.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue).ToArray();
        var destMembers = dest.GetMembers().OfType<IFieldSymbol>()
            .Where(f => f.HasConstantValue).ToArray();

        if (sourceMembers.Length != destMembers.Length)
            return false;

        for (int i = 0; i < sourceMembers.Length; i++)
        {
            if (sourceMembers[i].Name != destMembers[i].Name)
                return false;
            if (!Equals(sourceMembers[i].ConstantValue, destMembers[i].ConstantValue))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if source and dest types are compatible enums (possibly wrapped in Nullable).
    /// Used for ctor param matching where CanAssign returns false for cross-namespace enums.
    /// </summary>
    internal static bool IsCompatibleEnumPair(ITypeSymbol source, ITypeSymbol dest)
    {
        var srcEnum = GetNullableUnderlyingType(source) ?? source;
        var dstEnum = GetNullableUnderlyingType(dest) ?? dest;
        return AreCompatibleEnums(srcEnum, dstEnum);
    }

    /// <summary>
    /// Tries to generate a compatible enum cast expression. Returns null if not applicable.
    /// Handles EnumA→EnumB, Nullable&lt;EnumA&gt;→EnumB, EnumA→Nullable&lt;EnumB&gt;, and Nullable&lt;EnumA&gt;→Nullable&lt;EnumB&gt;.
    /// Uses the actual enum underlying type instead of hardcoding int.
    /// When <paramref name="isLifted"/> is true, treats a non-nullable source type as nullable
    /// (e.g., null-conditional lifting: source.Customer?.Priority is Priority? at runtime even
    /// though the leaf property type is non-nullable Priority).
    /// </summary>
    internal static string? TryGenerateCompatibleEnumCast(ITypeSymbol sourceType, ITypeSymbol destType, string sourceExpr, bool isLifted = false)
    {
        var srcUnderlying = GetNullableUnderlyingType(sourceType);
        var dstUnderlying = GetNullableUnderlyingType(destType);
        var srcEnum = srcUnderlying ?? sourceType;
        var dstEnum = dstUnderlying ?? destType;

        if (!AreCompatibleEnums(srcEnum, dstEnum))
            return null;

        // If expression is lifted by null-conditional (?.),
        // treat source as nullable even if sourceType is non-nullable
        var srcIsNullable = srcUnderlying != null || isLifted;

        var srcNamed = srcEnum as INamedTypeSymbol;
        var underlyingTypeName = srcNamed?.EnumUnderlyingType?.ToDisplayString() ?? "int";
        var destDisplay = destType.ToDisplayString();

        // Wrap sourceExpr in parentheses to handle null-conditional chains (e.g. source.Customer?.Priority)
        // Without parens, appending .HasValue/.Value extends the ?. chain with wrong semantics
        var safeExpr = sourceExpr.Contains("?.") ? $"({sourceExpr})" : sourceExpr;

        // Nullable source -> Nullable dest: propagate null via pattern match (single evaluation)
        if (srcIsNullable && dstUnderlying != null)
            return $"{safeExpr} is {{ }} __v ? ({destDisplay})({underlyingTypeName})__v : null";

        // Nullable source -> non-nullable dest: unwrap with .Value before casting (! suppresses CS8629)
        if (srcIsNullable && dstUnderlying == null)
            return $"({destDisplay})({underlyingTypeName}){safeExpr}!.Value";

        // Non-nullable source -> dest (nullable or not)
        return $"({destDisplay})({underlyingTypeName}){safeExpr}";
    }

    /// <summary>
    /// Gets the underlying type for Nullable&lt;T&gt;, or null if the type is not a nullable value type.
    /// </summary>
    internal static ITypeSymbol? GetNullableUnderlyingType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol namedType &&
            namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            namedType.TypeArguments.Length == 1)
        {
            return namedType.TypeArguments[0];
        }
        return null;
    }

    /// <summary>
    /// Checks if source is Nullable&lt;T&gt; and dest is T.
    /// </summary>
    internal static bool IsNullableToNonNullableValueType(ITypeSymbol source, ITypeSymbol dest)
    {
        var sourceUnderlying = GetNullableUnderlyingType(source);
        if (sourceUnderlying == null)
            return false;

        var destUnderlying = GetNullableUnderlyingType(dest);
        if (destUnderlying != null)
            return false;

        return SymbolEqualityComparer.Default.Equals(sourceUnderlying, dest);
    }

    /// <summary>
    /// Checks if source is a nullable reference type and dest is a non-nullable reference type.
    /// Intentionally excludes NullableAnnotation.None (oblivious) destinations since they don't produce CS8601.
    /// </summary>
    internal static bool IsNullableToNonNullableReferenceType(ITypeSymbol source, ITypeSymbol dest)
    {
        return source.IsReferenceType
            && dest.IsReferenceType
            && source.NullableAnnotation == NullableAnnotation.Annotated
            && dest.NullableAnnotation == NullableAnnotation.NotAnnotated;
    }

    /// <summary>
    /// Generates a type-appropriate default expression for the CoalesceToDefault strategy.
    /// Returns null if no suitable default can be determined (caller should fall back to NullForgiving).
    /// </summary>
    internal static string? GenerateCoalesceDefault(ITypeSymbol destType)
    {
        // string → ""
        if (destType.SpecialType == SpecialType.System_String)
            return "\"\"";

        // T[] → Array.Empty<T>()
        if (destType is IArrayTypeSymbol arrayType)
        {
            var elementDisplay = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"global::System.Array.Empty<{elementDisplay}>()";
        }

        // Named types with parameterless constructor → new FQN()
        // Skip abstract types and interfaces — they cannot be instantiated
        // Use Public accessibility: this is a shared helper without assembly context.
        // Same-assembly internal ctors are handled by ValidateCoalesceToNew separately.
        if (destType is INamedTypeSymbol namedType && !namedType.IsAbstract && namedType.TypeKind != TypeKind.Interface)
        {
            var hasParameterlessCtor = namedType.InstanceConstructors
                .Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

            if (hasParameterlessCtor)
            {
                return $"new {namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}()";
            }
        }

        // No suitable default — caller falls back to NullForgiving
        return null;
    }

    /// <summary>
    /// Gets the element type if the given type is a supported collection type.
    /// Returns null if the type is not a collection.
    /// </summary>
    internal static ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
    {
        // Handle arrays (T[])
        if (type is IArrayTypeSymbol arrayType)
        {
            return arrayType.ElementType;
        }

        // Handle generic collections
        if (type is INamedTypeSymbol namedType && namedType.IsGenericType && namedType.TypeArguments.Length == 1)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();

            if (SupportedCollectionTypes.Contains(originalDef))
            {
                return namedType.TypeArguments[0];
            }
        }

        return null;
    }

    internal static IEnumerable<IPropertySymbol> GetMappableProperties(INamedTypeSymbol? type)
    {
        if (type == null)
            return Enumerable.Empty<IPropertySymbol>();

        // Walk the full BaseType chain to collect properties from the entire
        // inheritance hierarchy (including compiled/metadata references).
        // Properties are returned base-first; if a derived type new-shadows
        // a base property, the derived declaration wins.
        var levels = new List<INamedTypeSymbol>();
        var current = type;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            levels.Add(current);
            current = current.BaseType;
        }

        // Reverse so base properties come first
        levels.Reverse();

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<IPropertySymbol>();

        foreach (var level in levels)
        {
            foreach (var prop in level.GetMembers().OfType<IPropertySymbol>())
            {
                if (prop.DeclaredAccessibility == Accessibility.Public &&
                    !prop.IsStatic &&
                    !prop.IsIndexer &&
                    prop.GetMethod != null)
                {
                    if (seen.TryGetValue(prop.Name, out var idx))
                    {
                        // Derived shadows base — replace the earlier entry
                        result[idx] = prop;
                    }
                    else
                    {
                        seen[prop.Name] = result.Count;
                        result.Add(prop);
                    }
                }
            }
        }

        return result;
    }

    internal static bool HasCheapCount(ITypeSymbol sourceType)
    {
        if (sourceType is IArrayTypeSymbol)
            return true;

        if (sourceType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();
            return CollectionTypesWithCheapCount.Contains(originalDef);
        }

        return false;
    }

    internal static string GetCollectionLengthExpression(ITypeSymbol sourceType, string sourceParam)
    {
        if (sourceType is IArrayTypeSymbol)
            return $"{sourceParam}.Length";

        if (sourceType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();
            if (CollectionTypesWithCheapCount.Contains(originalDef))
            {
                return $"{sourceParam}.Count";
            }
        }

        // Fallback to LINQ Count()
        return $"{sourceParam}.Count()";
    }

    /// <summary>
    /// Returns true if the type is a scalar (primitive, enum, string, etc.) that should not be auto-wired.
    /// </summary>
    internal static bool IsScalarType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum) return true;
        if (type.SpecialType != SpecialType.None) return true;
        return false;
    }

    /// <summary>
    /// Returns true when source is string (or string?) and dest is an enum (or Nullable&lt;enum&gt;).
    /// </summary>
    internal static bool IsStringToEnumPair(ITypeSymbol source, ITypeSymbol dest)
    {
        if (source.SpecialType != SpecialType.System_String)
            return false;
        var destUnderlying = GetNullableUnderlyingType(dest) ?? dest;
        return destUnderlying.TypeKind == TypeKind.Enum;
    }

    /// <summary>
    /// Returns true when source is an enum (or Nullable&lt;enum&gt;) and dest is string (or string?).
    /// </summary>
    internal static bool IsEnumToStringPair(ITypeSymbol source, ITypeSymbol dest)
    {
        if (dest.SpecialType != SpecialType.System_String)
            return false;
        var srcUnderlying = GetNullableUnderlyingType(source) ?? source;
        return srcUnderlying.TypeKind == TypeKind.Enum;
    }
}
