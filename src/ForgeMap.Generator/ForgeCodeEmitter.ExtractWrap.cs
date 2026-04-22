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

        // For member lookup, unwrap Nullable<T> — the underlying struct's properties live on T,
        // not Nullable<T>. The null guard below ensures we only access .Value when non-null.
        var sourceUnderlying = GetNullableUnderlyingType(sourceType);
        var memberLookupType = sourceUnderlying ?? sourceType;

        // FM0066: find a public, readable instance property (the getter itself must be public,
        // not just the property — `public string Name { private get; set; }` would otherwise slip through).
        // Walk the inheritance chain so base-class properties are visible.
        var srcProp = (memberLookupType is INamedTypeSymbol srcNamed
                ? GetMappableProperties(srcNamed)
                : memberLookupType.GetMembers().OfType<IPropertySymbol>())
            .Where(p => p.Name == propertyName)
            .FirstOrDefault(p => !p.IsStatic
                && p.GetMethod != null
                && p.DeclaredAccessibility == Accessibility.Public
                && p.GetMethod.DeclaredAccessibility == Accessibility.Public);

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
        // For Nullable<T> source, dereference via .Value (safe after the null guard).
        var accessRoot = sourceUnderlying != null
            ? $"{sourceParam.Name}!.Value"
            : sourceParam.Name;
        var rawAccess = $"{accessRoot}.{srcProp.Name}";
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
    /// Coercion ladder: direct (CanAssign) → DateTimeOffset→DateTime → string↔enum (respects
    /// <c>_config.StringToEnum</c> and handles Nullable&lt;Enum&gt;).
    /// </summary>
    private bool TryCoerceForExtract(
        ITypeSymbol sourcePropType,
        ITypeSymbol targetType,
        string rawAccess,
        out string expression)
    {
        // 1) Direct assignability (covers nullability widening/narrowing and Nullable<T> ↔ T).
        if (CanAssign(sourcePropType, targetType))
        {
            // Special-case Nullable<T> → T: emitting raw access would fail to compile.
            // Use .GetValueOrDefault() so the lifted access is well-formed; callers that
            // need exception semantics can layer their own null check upstream.
            var srcUnderlying = GetNullableUnderlyingType(sourcePropType);
            if (srcUnderlying != null && GetNullableUnderlyingType(targetType) == null)
            {
                expression = $"{rawAccess}.GetValueOrDefault()";
                return true;
            }
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

        // 3) string → enum. Routes through the shared helper so _config.StringToEnum
        //    (Parse / TryParse / None / StrictParse) and Nullable<Enum> are honored —
        //    matches PropertyAssignment/Projection behavior.
        if (_config.StringToEnum != 2 && IsStringToEnumPair(sourcePropType, targetType))
        {
            expression = GenerateStringToEnumParseExpression(sourcePropType, targetType, rawAccess, "value");
            return true;
        }

        // 4) enum → string. Handle Nullable<Enum> via null-conditional ToString.
        if (IsEnumToStringPair(sourcePropType, targetType))
        {
            var srcIsNullable = GetNullableUnderlyingType(sourcePropType) != null
                || sourcePropType.NullableAnnotation == NullableAnnotation.Annotated;
            expression = srcIsNullable ? $"{rawAccess}?.ToString()" : $"{rawAccess}.ToString()";
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

        // Abstract destinations cannot be instantiated via `new T(...)` or `new T { ... }`,
        // so neither wrap strategy can produce compilable code. Surface as FM0068 rather than
        // emitting an uncompilable `new AbstractType(...)`.
        if (destNamedType.IsAbstract)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.WrapPropertyNotFound,
                location, propertyName!, destNamedType.ToDisplayString(), method.Name);
            return string.Empty;
        }

        // Inventory the destination type (walk inheritance chain so base-class properties are visible):
        //   - settable/init property of the named member (initializer path)
        //   - public constructor with a parameter of the named member (ctor path)
        var namedSettable = GetMappableProperties(destNamedType)
            .Where(p => p.Name == propertyName)
            .FirstOrDefault(p => !p.IsStatic
                && p.SetMethod != null
                && p.DeclaredAccessibility == Accessibility.Public
                && p.SetMethod.DeclaredAccessibility == Accessibility.Public);

        // Strategy: object initializer
        var hasParameterlessCtor = destNamedType.InstanceConstructors.Any(c =>
            c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

        var unsatisfiedRequiredOnInit = EnumerateUnsatisfiedRequiredMembers(destNamedType, propertyName!).ToList();
        // Init strategy must also be type-compatible with the wrap source. If the named property's
        // type cannot accept the source (e.g. property is `Guid`, source is `string`), the
        // initializer strategy is NOT viable — we must let the ctor path try its own match instead
        // of committing to init and emitting FM0069. Without this guard, a ctor with a same-named
        // parameter of a coercible type would be silently bypassed.
        var initStrategyViable = hasParameterlessCtor
            && namedSettable != null
            && unsatisfiedRequiredOnInit.Count == 0
            && TryCoerceForWrap(sourceType, namedSettable.Type, "_", out _);

        // Pick strategy per ConstructorPreference and the named-member shape.
        // 0 = Auto, 1 = PreferParameterless (see ConstructorPreference.cs).
        bool preferInit;
        if (_config.ConstructorPreference == 1) // PreferParameterless
            preferInit = true;
        else // Auto
            preferInit = namedSettable != null;

        // Defer constructor selection: when initializer is the preferred-and-viable path, no ctor
        // is needed, and invoking FindWrapConstructor would incorrectly report FM0013 for ctor
        // ambiguity that is irrelevant to the chosen path. EXCEPTION: an explicit [ForgeConstructor]
        // annotation on the method is authoritative and must always be honored — never deferred.
        // [ForgeConstructor(...)] with at least one Type argument explicitly selects a
        // parameterized ctor and must override ConstructorPreference / init-strategy deferral.
        // [ForgeConstructor()] (empty params) means "use the parameterless ctor", which is
        // compatible with — and effectively opts into — the initializer strategy when viable.
        var explicitForgeCtorAttr = _forgeConstructorAttributeSymbol == null
            ? null
            : method.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, _forgeConstructorAttributeSymbol));
        var hasExplicitParameterizedForgeConstructor = explicitForgeCtorAttr != null
            && explicitForgeCtorAttr.ConstructorArguments.Length == 1
            && !explicitForgeCtorAttr.ConstructorArguments[0].IsNull
            && explicitForgeCtorAttr.ConstructorArguments[0].Values.Length > 0;
        var deferCtorSelection = preferInit && initStrategyViable && !hasExplicitParameterizedForgeConstructor;

        // Strategy: constructor with named parameter — selection uses the wrap-specific
        // FindWrapConstructor helper, including its tie-break for single-required-param matches
        // and [ForgeConstructor] honoring (FM0047) for explicit ctor picks.
        var ctorChoice = deferCtorSelection
            ? default
            : FindWrapConstructor(destNamedType, propertyName!, sourceType, context, method);

        // Ambiguity (FM0013) was already reported by FindWrapConstructor — short-circuit so we
        // don't pile FM0068/FM0071 on top of the same root cause.
        if (ctorChoice.AmbiguityReported)
            return string.Empty;

        var unsatisfiedRequiredOnCtor = ctorChoice.Constructor != null
            ? EnumerateUnsatisfiedRequiredMembers(destNamedType, propertyName!,
                ignoredRequiredMembersFromCtor: ctorChoice.Constructor,
                ctorTrustsSetsRequired: HasSetsRequiredMembers(ctorChoice.Constructor)).ToList()
            : new List<string>();
        var ctorStrategyViable = ctorChoice.Constructor != null
            && ctorChoice.MatchedParameter != null
            && unsatisfiedRequiredOnCtor.Count == 0;

        bool useInit;
        // Explicit [ForgeConstructor(T1, T2, ...)] selecting a parameterized ctor is authoritative —
        // it overrides ConstructorPreference and pins the strategy to ctor. If that ctor turns out
        // not to be viable (e.g. unsatisfied required members), we must NOT silently fall back to
        // the initializer strategy — the user explicitly opted out. Let the FM0071/FM0068 block
        // below surface the real failure instead. The empty form [ForgeConstructor()] means
        // "parameterless ctor", which is compatible with the initializer strategy.
        if (hasExplicitParameterizedForgeConstructor)
            useInit = false;
        else if (preferInit)
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
                // FM0069 vs FM0068: if some ctor has a parameter matching the named member but
                // its type is incompatible with the wrap source, the user's real problem is
                // type incompatibility — emit FM0069 with the specific types instead of the
                // less informative FM0068 ("not found"). Same applies if the init property
                // matches by name but its type cannot accept the source.
                IParameterSymbol? typeMismatchParam = null;
                foreach (var ctor in destNamedType.InstanceConstructors
                    .Where(c => c.DeclaredAccessibility == Accessibility.Public))
                {
                    var match = ctor.Parameters.FirstOrDefault(p =>
                        string.Equals(p.Name, propertyName!, System.StringComparison.OrdinalIgnoreCase));
                    if (match != null && !TryCoerceForWrap(sourceType, match.Type, "_", out _))
                    {
                        typeMismatchParam = match;
                        break;
                    }
                }
                ITypeSymbol? typeMismatchInitType = null;
                if (typeMismatchParam == null
                    && namedSettable != null
                    && hasParameterlessCtor
                    && !TryCoerceForWrap(sourceType, namedSettable.Type, "_", out _))
                {
                    typeMismatchInitType = namedSettable.Type;
                }
                if (typeMismatchParam != null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyTypeIncompatible,
                        location, sourceType.ToDisplayString(),
                        typeMismatchParam.Type.ToDisplayString(), method.Name);
                }
                else if (typeMismatchInitType != null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyTypeIncompatible,
                        location, sourceType.ToDisplayString(),
                        typeMismatchInitType.ToDisplayString(), method.Name);
                }
                else
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyNotFound,
                        location, propertyName!, destNamedType.ToDisplayString(), method.Name);
                }
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
    /// supports DateTime → DateTimeOffset (via DateTimeOffset constructor), string↔enum (respects
    /// <c>_config.StringToEnum</c> and Nullable&lt;Enum&gt;), and direct.
    /// </summary>
    private bool TryCoerceForWrap(
        ITypeSymbol sourceParamType,
        ITypeSymbol sinkType,
        string rawAccess,
        out string expression)
    {
        if (CanAssign(sourceParamType, sinkType))
        {
            // Nullable<T> → T: raw access doesn't compile.
            var srcUnderlying = GetNullableUnderlyingType(sourceParamType);
            if (srcUnderlying != null && GetNullableUnderlyingType(sinkType) == null)
            {
                expression = $"{rawAccess}.GetValueOrDefault()";
                return true;
            }
            expression = rawAccess;
            return true;
        }

        // DateTime → DateTimeOffset (mirror of TryCoerceForExtract's DateTimeOffset → DateTime,
        // which normalizes via .UtcDateTime). Treat the input DateTime as UTC so the round-trip
        // is offset-invariant — `new DateTimeOffset(dt)` would interpret Unspecified/Local Kind
        // as local time and silently shift the instant. Handles all four nullable combinations.
        var dto = TryGenerateDateTimeToDateTimeOffsetCoercion(sourceParamType, sinkType, rawAccess);
        if (dto != null)
        {
            expression = dto;
            return true;
        }

        // string → enum. Routes through the shared helper so _config.StringToEnum
        // (Parse / TryParse / None / StrictParse) and Nullable<Enum> are honored.
        if (_config.StringToEnum != 2 && IsStringToEnumPair(sourceParamType, sinkType))
        {
            expression = GenerateStringToEnumParseExpression(sourceParamType, sinkType, rawAccess, "value");
            return true;
        }

        // enum → string. Handle Nullable<Enum> via null-conditional ToString.
        if (IsEnumToStringPair(sourceParamType, sinkType))
        {
            var srcIsNullable = GetNullableUnderlyingType(sourceParamType) != null
                || sourceParamType.NullableAnnotation == NullableAnnotation.Annotated;
            expression = srcIsNullable ? $"{rawAccess}?.ToString()" : $"{rawAccess}.ToString()";
            return true;
        }

        expression = string.Empty;
        return false;
    }

    /// <summary>
    /// Mirror of <c>TryGenerateDateTimeOffsetToDateTimeCoercion</c>. Handles all four nullable
    /// combinations of <c>DateTime → DateTimeOffset</c>. The input DateTime is normalized to
    /// <c>DateTimeKind.Utc</c> before construction so the resulting offset is always zero —
    /// otherwise <c>DateTimeKind.Unspecified</c> would be interpreted as local time.
    /// </summary>
    private static string? TryGenerateDateTimeToDateTimeOffsetCoercion(
        ITypeSymbol sourceType, ITypeSymbol destType, string sourceExpr)
    {
        var srcUnderlying = GetNullableUnderlyingType(sourceType);
        var dstUnderlying = GetNullableUnderlyingType(destType);
        var srcCore = srcUnderlying ?? sourceType;
        var dstCore = dstUnderlying ?? destType;

        if (srcCore.SpecialType != SpecialType.System_DateTime
            || dstCore.ToDisplayString() != "System.DateTimeOffset")
            return null;

        var srcIsNullable = srcUnderlying != null;
        var dstIsNullable = dstUnderlying != null;

        const string utcKind = "global::System.DateTimeKind.Utc";
        const string ctor = "global::System.DateTimeOffset";
        const string specifyKind = "global::System.DateTime.SpecifyKind";

        if (!srcIsNullable && !dstIsNullable)
            return $"new {ctor}({specifyKind}({sourceExpr}, {utcKind}))";

        if (srcIsNullable && dstIsNullable)
            return $"{sourceExpr}.HasValue ? new {ctor}({specifyKind}({sourceExpr}.Value, {utcKind})) : ({ctor}?)null";

        if (srcIsNullable && !dstIsNullable)
            return $"new {ctor}({specifyKind}({sourceExpr}!.Value, {utcKind}))";

        return $"({ctor}?)new {ctor}({specifyKind}({sourceExpr}, {utcKind}))";
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
    /// emit cannot satisfy.
    /// <para>
    /// Initializer path (no constructor supplied): the wrapped member is satisfied by the
    /// emitted object initializer assignment, so it is excluded from the result.
    /// </para>
    /// <para>
    /// Constructor path (constructor supplied): C# only treats `required` members as satisfied
    /// when the constructor is annotated <c>[SetsRequiredMembers]</c> — matching a parameter
    /// name does NOT satisfy the required-member check (CS9035 still fires). So when
    /// <paramref name="ctorTrustsSetsRequired"/> is false we must report ALL required members,
    /// including the wrapped one and any whose names happen to match constructor parameters.
    /// </para>
    /// </summary>
    private static IEnumerable<string> EnumerateUnsatisfiedRequiredMembers(
        INamedTypeSymbol destType,
        string wrappedMemberName,
        IMethodSymbol? ignoredRequiredMembersFromCtor = null,
        bool ctorTrustsSetsRequired = false)
    {
        if (ctorTrustsSetsRequired)
            yield break;

        var isCtorPath = ignoredRequiredMembersFromCtor != null;

        // Walk the inheritance chain so inherited `required` members are detected.
        // C# 11 `required` applies to both properties AND fields, so check both.
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var current = destType;
        while (current != null && current.SpecialType != SpecialType.System_Object)
        {
            foreach (var member in current.GetMembers())
            {
                string memberName;
                switch (member)
                {
                    case IPropertySymbol p when p.IsRequired:
                        memberName = p.Name;
                        break;
                    case IFieldSymbol f when f.IsRequired:
                        memberName = f.Name;
                        break;
                    default:
                        continue;
                }
                if (!seen.Add(memberName)) continue;
                // Initializer path: the wrapped member's assignment in `new T { Wrapped = ... }`
                // satisfies the required-member check. Ctor path: it does not — only
                // [SetsRequiredMembers] does, and that case already short-circuited above.
                if (!isCtorPath && memberName == wrappedMemberName) continue;
                yield return memberName;
            }
            current = current.BaseType;
        }
    }

    /// <summary>
    /// Wrap-specific constructor selection.
    /// 0. If the method carries [ForgeConstructor], honor it: locate the ctor matching the
    ///    declared parameter types (FM0047 if not found). The matched ctor must still contain
    ///    a parameter named <paramref name="namedMember"/> (case-insensitive); if not, return
    ///    Empty so the caller can emit FM0068. This mirrors the v1.6 ResolveConstructor path
    ///    so [ForgeConstructor] / FM0047 behavior is consistent across the generator.
    /// 1. Otherwise, if a public constructor exists whose only required parameter (or only
    ///    parameter) matches <paramref name="namedMember"/> case-insensitively AND can accept
    ///    the wrap source type via TryCoerceForWrap, prefer it (wrap-specific FM0013 tie-break,
    ///    spec line 511).
    /// 2. Otherwise, scan public constructors that have a parameter named <paramref name="namedMember"/>
    ///    where every other parameter has a default. If exactly one such constructor exists, return it.
    ///    If multiple exist, defer to the established FM0013 ambiguity behavior (this method emits FM0013).
    /// 3. If none exist, return Empty (no diagnostic — caller decides FM0068 vs FM0071).
    /// </summary>
    private (IMethodSymbol? Constructor, IParameterSymbol? MatchedParameter, bool AmbiguityReported) FindWrapConstructor(
        INamedTypeSymbol destType,
        string namedMember,
        ITypeSymbol wrapSourceType,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var publicCtors = destType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        // Step 0: honor [ForgeConstructor] if present (mirrors ResolveConstructor's contract
        // for FM0047 / explicit ctor pick — keeps WrapProperty consistent with the rest of
        // the generator per spec line 511).
        var forgeConstructorAttr = _forgeConstructorAttributeSymbol != null
            ? method.GetAttributes().FirstOrDefault(a =>
                SymbolEqualityComparer.Default.Equals(a.AttributeClass, _forgeConstructorAttributeSymbol))
            : null;
        if (forgeConstructorAttr != null)
        {
            IReadOnlyList<TypedConstant>? specifiedTypes = null;
            var ctorArgs = forgeConstructorAttr.ConstructorArguments;
            if (ctorArgs.Length > 0 && ctorArgs[0].Kind == TypedConstantKind.Array)
                specifiedTypes = ctorArgs[0].Values;

            if (specifiedTypes != null)
            {
                IMethodSymbol? matchedCtor = null;
                foreach (var ctor in publicCtors)
                {
                    if (ctor.Parameters.Length != specifiedTypes.Count) continue;
                    var match = true;
                    for (int i = 0; i < ctor.Parameters.Length; i++)
                    {
                        var specType = specifiedTypes[i].Value as ITypeSymbol;
                        if (specType == null
                            || !SymbolEqualityComparer.Default.Equals(ctor.Parameters[i].Type, specType))
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) { matchedCtor = ctor; break; }
                }

                if (matchedCtor == null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.SpecifiedConstructorNotFound,
                        method.Locations.FirstOrDefault(),
                        destType.Name);
                    // AmbiguityReported=true so caller short-circuits — FM0047 already
                    // explains the failure; piling FM0068/FM0071 on top adds noise.
                    return (null, null, true);
                }

                // Explicit [ForgeConstructor] selection must fully govern wrap resolution: if the
                // chosen ctor cannot bind the wrapped member or accept the wrap source type, hard-fail
                // (AmbiguityReported=true) so the caller does not silently fall back to a different
                // ctor or the initializer strategy. Report the specific wrap failure (FM0068/FM0069)
                // before returning so the user gets an explanation instead of only the downstream CS8795.
                var matchedParam = matchedCtor.Parameters.FirstOrDefault(p =>
                    string.Equals(p.Name, namedMember, System.StringComparison.OrdinalIgnoreCase));
                if (matchedParam == null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyNotFound,
                        method.Locations.FirstOrDefault(),
                        namedMember, destType.ToDisplayString(), method.Name);
                    return (null, null, true);
                }
                if (!TryCoerceForWrap(wrapSourceType, matchedParam.Type, "_", out _))
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyTypeIncompatible,
                        method.Locations.FirstOrDefault(),
                        wrapSourceType.ToDisplayString(), matchedParam.Type.ToDisplayString(), method.Name);
                    return (null, null, true);
                }
                var allOthersOptional = matchedCtor.Parameters.All(p =>
                    ReferenceEquals(p, matchedParam) || p.HasExplicitDefaultValue || p.IsOptional);
                if (!allOthersOptional)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.WrapPropertyNotFound,
                        method.Locations.FirstOrDefault(),
                        namedMember, destType.ToDisplayString(), method.Name);
                    return (null, null, true);
                }
                return (matchedCtor, matchedParam, false);
            }
        }

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
            return (singleValueCandidates[0].Ctor, singleValueCandidates[0].Param, false);

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
            return (generalCandidates[0].Ctor, generalCandidates[0].Param, false);

        if (generalCandidates.Count > 1)
        {
            // Defer to established FM0013 ambiguity behavior. Caller short-circuits on
            // AmbiguityReported so FM0068/FM0071 don't pile on the same root cause.
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousConstructor,
                method.Locations.FirstOrDefault(),
                destType.ToDisplayString());
            return (null, null, true);
        }

        return (null, null, false);
    }
}
