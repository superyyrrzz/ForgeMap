using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

internal sealed partial class ForgeCodeEmitter
{
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
    /// Emits the body of a [WrapProperty]-annotated partial method. Selects between an
    /// object-initializer strategy and a constructor strategy per the spec resolution
    /// algorithm (Feature 3, lines 511–520):
    ///   - PreferParameterless: initializer first, fall through to constructor.
    ///   - Auto + get-only named member: constructor only.
    ///   - Auto + settable/init named member: initializer first, fall through to constructor.
    /// Emits FM0068 when no strategy is viable, FM0071 when the only blocker is unsatisfied
    /// `required` members, FM0069 when type compatibility fails, and FM0074 (info, disabled)
    /// when the return type is a value type under ReturnNull semantics.
    /// </summary>
#pragma warning disable IDE0060 // Remove unused parameter — 'forger' kept for symmetry with ExtractPropertyBody
    private string GenerateWrapPropertyBody(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var propertyName = GetWrapPropertyName(method);
        if (string.IsNullOrEmpty(propertyName))
            return string.Empty;

        var sourceParam = method.Parameters[0];
        var sourceType = sourceParam.Type;
        var returnType = method.ReturnType;
        var location = method.Locations.FirstOrDefault();

        if (returnType is not INamedTypeSymbol destNamedType)
        {
            // Non-named return (e.g., array, type parameter) cannot be constructed by name —
            // surface as FM0068.
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.WrapPropertyNotFound,
                location, propertyName!, returnType.ToDisplayString(), method.Name);
            return string.Empty;
        }

        // Inventory the destination type:
        //   - settable/init property of the named member (initializer path)
        //   - public constructor with a parameter of the named member (ctor path)
        var namedSettable = destNamedType.GetMembers(propertyName!).OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic
                && p.SetMethod != null
                && p.DeclaredAccessibility == Accessibility.Public);
        var namedReadable = destNamedType.GetMembers(propertyName!).OfType<IPropertySymbol>()
            .FirstOrDefault(p => !p.IsStatic
                && p.GetMethod != null
                && p.DeclaredAccessibility == Accessibility.Public);

        // Strategy: object initializer
        var hasParameterlessCtor = destNamedType.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        var unsatisfiedRequiredOnInit = EnumerateUnsatisfiedRequiredMembers(destNamedType, propertyName!).ToList();
        var initStrategyViable = hasParameterlessCtor
            && namedSettable != null
            && unsatisfiedRequiredOnInit.Count == 0;

        // Strategy: constructor with named parameter — selection delegated to existing pipeline,
        // with the wrap-specific tie-break for single-required-param matches.
        var ctorChoice = FindWrapConstructor(destNamedType, propertyName!, sourceType, context, method);
        var unsatisfiedRequiredOnCtor = ctorChoice.Constructor != null
            ? EnumerateUnsatisfiedRequiredMembers(destNamedType, propertyName!,
                ignoredRequiredMembersFromCtor: ctorChoice.Constructor,
                ctorTrustsSetsRequired: HasSetsRequiredMembers(ctorChoice.Constructor)).ToList()
            : new List<string>();
        var ctorStrategyViable = ctorChoice.Constructor != null
            && ctorChoice.MatchedParameter != null
            && unsatisfiedRequiredOnCtor.Count == 0;

        // Pick strategy per ConstructorPreference and the named-member shape.
        // 0 = Auto, 1 = PreferParameterless (see ConstructorPreference.cs).
        bool preferInit;
        if (_config.ConstructorPreference == 1) // PreferParameterless
            preferInit = true;
        else // Auto
            preferInit = namedSettable != null;

        bool useInit;
        if (preferInit)
            useInit = initStrategyViable; // first preference; if not viable, fall through to ctor
        else
            useInit = false;

        // FM0071 vs FM0068 precedence: if the only thing blocking the chosen-or-fallback path
        // is unsatisfied required members, prefer FM0071. Otherwise FM0068.
        if (!useInit && !ctorStrategyViable)
        {
            // Pick the more specific diagnostic.
            var requiredBlockers = new HashSet<string>(unsatisfiedRequiredOnInit);
            requiredBlockers.UnionWith(unsatisfiedRequiredOnCtor);

            // Required-member error wins ONLY when at least one strategy had a matching
            // sink for the named member (initializer or ctor parameter) — otherwise the
            // user's actual problem is "named member not found", which is FM0068.
            var hadNameMatch = (namedSettable != null && hasParameterlessCtor)
                || ctorChoice.MatchedParameter != null;
            if (hadNameMatch && requiredBlockers.Count > 0)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.WrapPropertyRequiredMembersUnsatisfied,
                    location, destNamedType.ToDisplayString(),
                    string.Join(", ", requiredBlockers));
            }
            else
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.WrapPropertyNotFound,
                    location, propertyName!, destNamedType.ToDisplayString(), method.Name);
            }
            return string.Empty;
        }

        // Type compatibility for the chosen strategy.
        ITypeSymbol sinkType = useInit ? namedSettable!.Type : ctorChoice.MatchedParameter!.Type;
        if (!TryCoerceForWrap(sourceType, sinkType, sourceParam.Name, out var assignExpression))
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.WrapPropertyTypeIncompatible,
                location, sourceType.ToDisplayString(), sinkType.ToDisplayString(), method.Name);
            return string.Empty;
        }

        // FM0074: return-type collapse audit.
        var sourceCanBeNull = sourceType.IsReferenceType
            || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        if (sourceCanBeNull && returnType.IsValueType && _config.NullHandling == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ExtractWrapValueTypeReturnUnderReturnNull,
                location, method.Name, returnType.ToDisplayString());
        }

        // Emit body.
        var sb = new StringBuilder();
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var returnDisplay = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var sourceDisplay = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var destDisplay = destNamedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        sb.AppendLine($"        {accessibility} partial {returnDisplay} {method.Name}({sourceDisplay} {sourceParam.Name})");
        sb.AppendLine("        {");

        if (sourceCanBeNull)
        {
            var nullReturn = returnType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam.Name, nullReturn));
        }

        if (useInit)
        {
            sb.AppendLine($"            return new {destDisplay} {{ {namedSettable!.Name} = {assignExpression} }};");
        }
        else
        {
            sb.AppendLine($"            return new {destDisplay}({ctorChoice.MatchedParameter!.Name}: {assignExpression});");
        }

        sb.AppendLine("        }");
        return sb.ToString();
    }
#pragma warning restore IDE0060

    /// <summary>
    /// Decides how to express <paramref name="rawAccess"/> (an expression of <paramref name="sourceParamType"/>)
    /// as a value of <paramref name="sinkType"/>. Mirrors <see cref="TryCoerceForExtract"/> in reverse —
    /// supports DateTime → DateTimeOffset (via DateTimeOffset constructor), string↔enum, and direct.
    /// </summary>
    private static bool TryCoerceForWrap(
        ITypeSymbol sourceParamType,
        ITypeSymbol sinkType,
        string rawAccess,
        out string expression)
    {
        if (CanAssign(sourceParamType, sinkType))
        {
            expression = rawAccess;
            return true;
        }

        // string → enum
        if (sourceParamType.SpecialType == SpecialType.System_String && sinkType.TypeKind == TypeKind.Enum)
        {
            var enumFqn = sinkType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            expression = $"string.IsNullOrEmpty({rawAccess}) ? default({enumFqn}) : ({enumFqn})global::System.Enum.Parse(typeof({enumFqn}), {rawAccess}, true)";
            return true;
        }

        // enum → string
        if (sourceParamType.TypeKind == TypeKind.Enum && sinkType.SpecialType == SpecialType.System_String)
        {
            expression = $"{rawAccess}.ToString()";
            return true;
        }

        expression = string.Empty;
        return false;
    }

    /// <summary>
    /// True when the constructor is annotated [SetsRequiredMembers]. Matches the established
    /// detection pattern in ForgeCodeEmitter.NullHandling.cs.
    /// </summary>
    private static bool HasSetsRequiredMembers(IMethodSymbol ctor)
    {
        return ctor.GetAttributes().Any(a =>
            a.AttributeClass?.Name == "SetsRequiredMembersAttribute"
            || a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
    }

    /// <summary>
    /// Yields the names of `required` members of <paramref name="destType"/> that the wrap
    /// emit cannot satisfy. The `wrappedMemberName` is the one the [WrapProperty] attribute
    /// names — it is satisfiable. When a constructor is supplied, parameters bound to required
    /// members (by name match) AND any required member trusted by [SetsRequiredMembers] are
    /// considered satisfied.
    /// </summary>
    private static IEnumerable<string> EnumerateUnsatisfiedRequiredMembers(
        INamedTypeSymbol destType,
        string wrappedMemberName,
        IMethodSymbol? ignoredRequiredMembersFromCtor = null,
        bool ctorTrustsSetsRequired = false)
    {
        if (ctorTrustsSetsRequired)
            yield break;

        var ctorParamNames = ignoredRequiredMembersFromCtor != null
            ? new HashSet<string>(ignoredRequiredMembersFromCtor.Parameters.Select(p => p.Name))
            : new HashSet<string>();

        foreach (var member in destType.GetMembers().OfType<IPropertySymbol>())
        {
            if (!member.IsRequired) continue;
            if (member.Name == wrappedMemberName) continue;
            // Match constructor parameters case-insensitively because parameter names are
            // conventionally camelCase while properties are PascalCase.
            if (ctorParamNames.Any(p => string.Equals(p, member.Name, System.StringComparison.OrdinalIgnoreCase)))
                continue;
            yield return member.Name;
        }
    }

    /// <summary>
    /// Wrap-specific constructor selection.
    /// 1. If a public constructor exists whose only required parameter (or only parameter) matches
    ///    <paramref name="namedMember"/> case-insensitively AND can accept the wrap source type
    ///    via TryCoerceForWrap, prefer it (this is the wrap-specific FM0013 tie-break, spec line 511).
    /// 2. Otherwise, scan public constructors that have a parameter named <paramref name="namedMember"/>
    ///    where every other parameter has a default. If exactly one such constructor exists, return it.
    ///    If multiple exist, defer to the established FM0013 ambiguity behavior (this method emits FM0013).
    /// 3. If none exist, return Empty (no diagnostic — caller decides FM0068 vs FM0071).
    /// </summary>
    private (IMethodSymbol? Constructor, IParameterSymbol? MatchedParameter) FindWrapConstructor(
        INamedTypeSymbol destType,
        string namedMember,
        ITypeSymbol wrapSourceType,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var publicCtors = destType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        // Tie-break: prefer the unique single-value-wrap ctor.
        var singleValueCandidates = new List<(IMethodSymbol Ctor, IParameterSymbol Param)>();
        foreach (var ctor in publicCtors)
        {
            if (ctor.Parameters.Length == 0) continue;
            var requiredParams = ctor.Parameters.Where(p => !p.HasExplicitDefaultValue && !p.IsOptional).ToList();
            if (requiredParams.Count != 1) continue;
            var soleRequired = requiredParams[0];
            if (!string.Equals(soleRequired.Name, namedMember, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (!TryCoerceForWrap(wrapSourceType, soleRequired.Type, "_", out _))
                continue;
            singleValueCandidates.Add((ctor, soleRequired));
        }

        if (singleValueCandidates.Count == 1)
            return (singleValueCandidates[0].Ctor, singleValueCandidates[0].Param);

        // General path: any public ctor with a named parameter, all others optional/defaulted.
        var generalCandidates = new List<(IMethodSymbol Ctor, IParameterSymbol Param)>();
        foreach (var ctor in publicCtors)
        {
            if (ctor.Parameters.Length == 0) continue;
            var matched = ctor.Parameters.FirstOrDefault(p =>
                string.Equals(p.Name, namedMember, System.StringComparison.OrdinalIgnoreCase));
            if (matched == null) continue;
            var allOthersOptional = ctor.Parameters.All(p =>
                ReferenceEquals(p, matched) || p.HasExplicitDefaultValue || p.IsOptional);
            if (!allOthersOptional) continue;
            if (!TryCoerceForWrap(wrapSourceType, matched.Type, "_", out _))
                continue;
            generalCandidates.Add((ctor, matched));
        }

        if (generalCandidates.Count == 1)
            return (generalCandidates[0].Ctor, generalCandidates[0].Param);

        if (generalCandidates.Count > 1)
        {
            // Defer to established FM0013 ambiguity behavior.
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousConstructor,
                method.Locations.FirstOrDefault(),
                destType.ToDisplayString());
            return (null, null);
        }

        return (null, null);
    }
}
