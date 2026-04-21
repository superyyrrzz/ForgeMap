using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

// Helpers are defined here for an upcoming task; callers will be wired in later.
internal sealed partial class ForgeCodeEmitter
{
    /// <summary>
    /// Conditional mode — Condition or SkipWhen.
    /// </summary>
    internal enum ConditionalKind
    {
        Condition, // predicate true => assign
        SkipWhen,  // predicate true => skip
    }

    /// <summary>
    /// Result of resolving a per-property predicate. Indicates whether a guard
    /// applies, and if so the predicate identity needed to emit the guard expression.
    /// </summary>
    internal readonly struct ConditionalResolution
    {
        private ConditionalResolution(bool applicable, bool failed, ConditionalKind kind, IMethodSymbol? predicate, string? predicateName)
        {
            Applicable = applicable;
            DidFail = failed;
            Kind = kind;
            Predicate = predicate;
            PredicateName = predicateName;
        }

        public bool Applicable { get; }
        public bool DidFail { get; }
        public ConditionalKind Kind { get; }
        public IMethodSymbol? Predicate { get; }
        public string? PredicateName { get; }

        public static ConditionalResolution NotApplicable() => new(false, false, default, null, null);
        public static ConditionalResolution Failed() => new(true, true, default, null, null);
        public static ConditionalResolution FromPredicate(ConditionalKind kind, IMethodSymbol method, string name)
            => new(true, false, kind, method, name);
    }

    /// <summary>
    /// Resolves the per-property conditional configuration for a destination property:
    /// validates exclusivity (FM0060), conflict with [ForgeFrom]/[ForgeWith] (FM0063
    /// — suppressed when SelectProperty conflict already triggered FM0072), the destination
    /// shape (FM0062 for ctor/init/required), and the predicate signature (FM0061).
    /// Reports diagnostics directly. Returns NotApplicable when no Condition/SkipWhen
    /// is set; Failed when a diagnostic was emitted; FromPredicate with the resolved
    /// predicate method when emit may proceed.
    /// </summary>
    private ConditionalResolution ResolveConditionalForProperty(
        IPropertySymbol destProp,
        ITypeSymbol sourceType,
        Dictionary<string, string> conditionMappings,
        Dictionary<string, string> skipWhenMappings,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> selectPropertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        bool isCtorBound,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var hasCondition = conditionMappings.TryGetValue(destProp.Name, out var conditionName);
        var hasSkipWhen = skipWhenMappings.TryGetValue(destProp.Name, out var skipWhenName);
        if (!hasCondition && !hasSkipWhen)
            return ConditionalResolution.NotApplicable();

        var location = method.Locations.FirstOrDefault();

        // FM0060: mutual exclusivity
        if (hasCondition && hasSkipWhen)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ConditionAndSkipWhenBothSet,
                location, destProp.Name);
            return ConditionalResolution.Failed();
        }

        // FM0063 (suppressed by FM0072 precedence): conflict with [ForgeFrom] / [ForgeWith]
        var hasForgeFromOrWith = resolverMappings.ContainsKey(destProp.Name) || forgeWithMappings.ContainsKey(destProp.Name);
        if (hasForgeFromOrWith)
        {
            // Per spec: when SelectProperty also conflicts with ForgeFrom/ForgeWith,
            // FM0072 wins and FM0063 is suppressed for this destination property.
            if (!selectPropertyMappings.ContainsKey(destProp.Name))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ConditionalConflictsWithForgeFromOrWith,
                    location, destProp.Name);
            }
            return ConditionalResolution.Failed();
        }

        // FM0062: destination must be a settable, non-init, non-required, non-ctor-bound property
        var isInitOnly = destProp.SetMethod?.IsInitOnly == true;
        var isRequired = destProp.IsRequired;
        if (isCtorBound || isInitOnly || isRequired)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ConditionalNotSupportedOnInitOrCtor,
                location, destProp.Name);
            return ConditionalResolution.Failed();
        }

        var predicateName = hasCondition ? conditionName! : skipWhenName!;
        var kind = hasCondition ? ConditionalKind.Condition : ConditionalKind.SkipWhen;

        ITypeSymbol expectedArgType;
        if (kind == ConditionalKind.Condition)
        {
            string sourcePropPath = ResolveSourcePropertyPath(destProp, sourceType, propertyMappings);
            expectedArgType = sourceType is INamedTypeSymbol named
                ? ResolvePathLeafType(sourcePropPath, named) ?? sourceType
                : sourceType;
        }
        else
        {
            expectedArgType = sourceType;
        }

        var candidates = forger.Symbol.GetMembers(predicateName).OfType<IMethodSymbol>().ToList();
        var predicate = candidates.FirstOrDefault(m =>
            m.Parameters.Length == 1 &&
            m.ReturnType.SpecialType == SpecialType.System_Boolean &&
            CanAssign(expectedArgType, m.Parameters[0].Type));

        if (predicate == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ConditionalPredicateInvalid,
                location, predicateName, destProp.Name, expectedArgType.ToDisplayString());
            return ConditionalResolution.Failed();
        }

        // FM0064 — info diagnostic
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.ConditionalAssignmentApplied,
            location, destProp.Name, predicateName);

        return ConditionalResolution.FromPredicate(kind, predicate, predicateName);
    }

    /// <summary>
    /// Re-resolves the conditional predicate for a destination property *without*
    /// reporting diagnostics. Used by the Forge writer after GeneratePropertyAssignment
    /// has already validated the configuration and reported any errors.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Kept symmetric with ResolveConditionalForProperty for caller convenience.")]
    private ConditionalResolution TryResolveConditionalSilently(
        IPropertySymbol destProp,
        ITypeSymbol sourceType,
        Dictionary<string, string> conditionMappings,
        Dictionary<string, string> skipWhenMappings,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> selectPropertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        bool isCtorBound,
        ForgerInfo forger)
    {
        var hasCondition = conditionMappings.TryGetValue(destProp.Name, out var conditionName);
        var hasSkipWhen = skipWhenMappings.TryGetValue(destProp.Name, out var skipWhenName);
        if (!hasCondition && !hasSkipWhen) return ConditionalResolution.NotApplicable();
        if (hasCondition && hasSkipWhen) return ConditionalResolution.NotApplicable();
        if (resolverMappings.ContainsKey(destProp.Name) || forgeWithMappings.ContainsKey(destProp.Name))
            return ConditionalResolution.NotApplicable();
        if (isCtorBound || destProp.SetMethod?.IsInitOnly == true || destProp.IsRequired)
            return ConditionalResolution.NotApplicable();

        var predicateName = hasCondition ? conditionName! : skipWhenName!;
        var kind = hasCondition ? ConditionalKind.Condition : ConditionalKind.SkipWhen;

        ITypeSymbol expectedArgType;
        if (kind == ConditionalKind.Condition)
        {
            string sourcePropPath = ResolveSourcePropertyPath(destProp, sourceType, propertyMappings);
            expectedArgType = sourceType is INamedTypeSymbol named
                ? ResolvePathLeafType(sourcePropPath, named) ?? sourceType
                : sourceType;
        }
        else
        {
            expectedArgType = sourceType;
        }

        var predicate = forger.Symbol.GetMembers(predicateName).OfType<IMethodSymbol>().FirstOrDefault(m =>
            m.Parameters.Length == 1 &&
            m.ReturnType.SpecialType == SpecialType.System_Boolean &&
            CanAssign(expectedArgType, m.Parameters[0].Type));

        return predicate == null
            ? ConditionalResolution.NotApplicable()
            : ConditionalResolution.FromPredicate(kind, predicate, predicateName);
    }

    /// <summary>
    /// Resolves the dotted source-property path used to locate the value passed to a Condition
    /// predicate. Honors explicit [ForgeProperty(source, dest)] mappings and falls back to
    /// convention matching that respects PropertyMatching (case-insensitive when configured).
    /// </summary>
    private string ResolveSourcePropertyPath(IPropertySymbol destProp, ITypeSymbol sourceType, Dictionary<string, string> propertyMappings)
    {
        if (propertyMappings.TryGetValue(destProp.Name, out var mapped))
            return mapped;

        if (sourceType is INamedTypeSymbol named)
        {
            var matched = GetMappableProperties(named)
                .FirstOrDefault(p => string.Equals(p.Name, destProp.Name, _config.PropertyNameComparison));
            if (matched != null)
                return matched.Name;
        }

        return destProp.Name;
    }

    /// <summary>
     /// Builds the guard expression text used in <c>if (&lt;guard&gt;) ...</c>
     /// for a resolved conditional. SkipWhen is negated.
     /// </summary>
    private static string BuildConditionalGuardExpression(in ConditionalResolution resolution, string argExpr)
    {
        var call = $"{resolution.PredicateName}({argExpr})";
        return resolution.Kind == ConditionalKind.SkipWhen ? $"!{call}" : call;
    }

    /// <summary>
    /// Wraps any skipNull/postConstruction entries appended to the given lists since the
    /// recorded snapshot counts inside the conditional guard. Used by the Forge writers
    /// when GeneratePropertyAssignment returns null (the property's emission was queued
    /// instead of returned as a single expression) so the user's Condition/SkipWhen still
    /// applies. Without this, conditional config is silently dropped for SkipNull /
    /// post-construction collection / projection paths.
    /// </summary>
    private static void WrapQueuedEntriesWithConditionalGuard(
        in ConditionalResolution conditional,
        string predicateArg,
        List<(string DestPropName, string SourceExpr, string LocalVarName, string? AssignExpr)> skipNullAssignments,
        int skipNullSnapshot,
        List<(string DestPropName, string Block)> postConstructionCollections,
        int postCtorSnapshot)
    {
        if (!conditional.Applicable || conditional.DidFail) return;
        var guard = BuildConditionalGuardExpression(in conditional, predicateArg);

        for (int i = skipNullSnapshot; i < skipNullAssignments.Count; i++)
        {
            var (dest, src, localVar, assign) = skipNullAssignments[i];
            // Convert skipNull entry into a post-construction guarded block and clear the entry.
            var inner = $"                if ({src} is {{ }} {localVar})\n                {{\n                    result.{dest} = {assign ?? localVar};\n                }}";
            var block = $"            if ({guard})\n            {{\n{inner}\n            }}";
            postConstructionCollections.Add((dest, block));
        }
        if (skipNullAssignments.Count > skipNullSnapshot)
            skipNullAssignments.RemoveRange(skipNullSnapshot, skipNullAssignments.Count - skipNullSnapshot);

        for (int i = postCtorSnapshot; i < postConstructionCollections.Count; i++)
        {
            var (dest, block) = postConstructionCollections[i];
            var indented = string.Join("\n", block.Split('\n').Select(l => l.Length == 0 ? l : "    " + l));
            postConstructionCollections[i] = (dest, $"            if ({guard})\n            {{\n{indented}\n            }}");
        }
    }
}
