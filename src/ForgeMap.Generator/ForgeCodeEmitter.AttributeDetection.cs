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
}
