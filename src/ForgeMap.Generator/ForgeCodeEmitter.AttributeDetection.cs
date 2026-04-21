using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
    private bool HasForgeIntoPattern(IMethodSymbol method)
    {
        return GetUseExistingValueParameter(method) != null;
    }

    private bool HasReverseForgeAttribute(IMethodSymbol method)
    {
        if (_reverseForgeAttributeSymbol == null)
            return false;

        return method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _reverseForgeAttributeSymbol));
    }

    private bool HasForgeAllDerivedAttribute(IMethodSymbol method)
    {
        if (_forgeAllDerivedAttributeSymbol == null)
            return false;

        return method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _forgeAllDerivedAttributeSymbol));
    }

    private bool HasConvertWithAttribute(IMethodSymbol method)
    {
        if (_convertWithAttributeSymbol == null)
            return false;

        return method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _convertWithAttributeSymbol));
    }

#pragma warning disable IDE0051 // Remove unused private members — consumed in tasks 4-7
    private bool HasExtractPropertyAttribute(IMethodSymbol method)
    {
        if (_extractPropertyAttributeSymbol == null)
            return false;

        return method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _extractPropertyAttributeSymbol));
    }

    private bool HasWrapPropertyAttribute(IMethodSymbol method)
    {
        if (_wrapPropertyAttributeSymbol == null)
            return false;

        return method.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _wrapPropertyAttributeSymbol));
    }

    /// <summary>
    /// Returns the property-name argument from the [ExtractProperty] attribute on this method,
    /// or null if the attribute is absent or malformed.
    /// </summary>
    private string? GetExtractPropertyName(IMethodSymbol method)
    {
        if (_extractPropertyAttributeSymbol == null) return null;
        var attr = method.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _extractPropertyAttributeSymbol));
        if (attr == null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }

    /// <summary>
    /// Returns the property-name argument from the [WrapProperty] attribute on this method,
    /// or null if the attribute is absent or malformed.
    /// </summary>
    private string? GetWrapPropertyName(IMethodSymbol method)
    {
        if (_wrapPropertyAttributeSymbol == null) return null;
        var attr = method.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _wrapPropertyAttributeSymbol));
        if (attr == null || attr.ConstructorArguments.Length == 0) return null;
        return attr.ConstructorArguments[0].Value as string;
    }
#pragma warning restore IDE0051

    private bool HasUseExistingValueAttribute(IParameterSymbol param)
    {
        if (_useExistingValueAttributeSymbol == null)
            return false;

        return param.GetAttributes().Any(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _useExistingValueAttributeSymbol));
    }

    private IParameterSymbol? GetUseExistingValueParameter(IMethodSymbol method)
    {
        return method.Parameters.FirstOrDefault(p => HasUseExistingValueAttribute(p));
    }

    /// <summary>
    /// Extracts [ConvertWith] attribute data from a method.
    /// Returns null if the attribute is not present.
    /// </summary>
    private ConvertWithInfo? GetConvertWithInfo(IMethodSymbol method)
    {
        if (_convertWithAttributeSymbol == null)
            return null;

        var attr = method.GetAttributes().FirstOrDefault(a =>
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _convertWithAttributeSymbol));

        if (attr == null || attr.ConstructorArguments.Length == 0)
            return null;

        var arg = attr.ConstructorArguments[0];
        if (arg.Value is INamedTypeSymbol typeSymbol)
            return new ConvertWithInfo(typeSymbol, null);
        if (arg.Value is string memberName)
            return new ConvertWithInfo(null, memberName);

        return null;
    }

    /// <summary>
    /// Checks whether <paramref name="converterType"/> implements ITypeConverter&lt;TSource, TDest&gt;
    /// with the matching type arguments.
    /// </summary>
    private bool ImplementsITypeConverter(ITypeSymbol converterType, ITypeSymbol sourceType, ITypeSymbol destType)
    {
        if (_iTypeConverterOpenSymbol == null)
            return false;

        foreach (var iface in converterType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, _iTypeConverterOpenSymbol))
                continue;
            if (iface.TypeArguments.Length != 2)
                continue;
            if (SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], sourceType) &&
                SymbolEqualityComparer.Default.Equals(iface.TypeArguments[1], destType))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds a field or property on the forger type whose type matches the given DI interface type.
    /// Returns the member name, or null if not found.
    /// </summary>
    private string? FindFieldByType(INamedTypeSymbol forgerType, INamedTypeSymbol targetType)
    {
        foreach (var member in forgerType.GetMembers())
        {
            if (member is IFieldSymbol field && !field.IsStatic &&
                SymbolEqualityComparer.Default.Equals(field.Type, targetType))
                return field.Name;
            if (member is IPropertySymbol prop && !prop.IsStatic &&
                SymbolEqualityComparer.Default.Equals(prop.Type, targetType))
                return prop.Name;
        }
        return null;
    }
}

/// <summary>
/// Data extracted from a [ConvertWith] attribute.
/// </summary>
internal readonly struct ConvertWithInfo
{
    public ConvertWithInfo(INamedTypeSymbol? converterType, string? memberName)
    {
        ConverterType = converterType;
        MemberName = memberName;
    }

    public INamedTypeSymbol? ConverterType { get; }
    public string? MemberName { get; }
}
