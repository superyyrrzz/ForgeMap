using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;

namespace ForgeMap.Generator;

/// <summary>
/// Resolved configuration for a forger class, combining assembly-level defaults
/// and class-level [ForgeMap] attribute overrides.
/// </summary>
internal sealed class ForgerConfig
{
    /// <summary>0 = ReturnNull (default), 1 = ThrowException</summary>
    public int NullHandling { get; set; }

    /// <summary>0 = ByName (default, case-sensitive), 1 = ByNameCaseInsensitive</summary>
    public int PropertyMatching { get; set; }

    /// <summary>Whether to generate collection mapping methods. Default true.</summary>
    public bool GenerateCollectionMappings { get; set; } = true;

    /// <summary>Diagnostic IDs to suppress for this forger (e.g. "FM0005").</summary>
    public HashSet<string> SuppressDiagnostics { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public StringComparison PropertyNameComparison =>
        PropertyMatching == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}

/// <summary>
/// Emits generated forging code for ForgeMap forger classes.
/// </summary>
internal sealed class ForgeCodeEmitter
{
    private readonly INamedTypeSymbol? _ignoreAttributeSymbol;
    private readonly INamedTypeSymbol? _forgePropertyAttributeSymbol;
    private readonly INamedTypeSymbol? _forgeFromAttributeSymbol;
    private readonly INamedTypeSymbol? _forgeWithAttributeSymbol;
    private readonly INamedTypeSymbol? _reverseForgeAttributeSymbol;
    private readonly INamedTypeSymbol? _beforeForgeAttributeSymbol;
    private readonly INamedTypeSymbol? _afterForgeAttributeSymbol;
    private readonly INamedTypeSymbol? _includeBaseForgeAttributeSymbol;
    private readonly INamedTypeSymbol? _forgeAllDerivedAttributeSymbol;
    private readonly INamedTypeSymbol? _convertWithAttributeSymbol;
    private readonly ForgerConfig _assemblyDefaults;
    private ForgerConfig _config = null!;

    public ForgeCodeEmitter(Compilation compilation, ForgerConfig assemblyDefaults)
    {
        _ignoreAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.IgnoreAttribute");
        _forgePropertyAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ForgePropertyAttribute");
        _forgeFromAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ForgeFromAttribute");
        _forgeWithAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ForgeWithAttribute");
        _reverseForgeAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ReverseForgeAttribute");
        _beforeForgeAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.BeforeForgeAttribute");
        _afterForgeAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.AfterForgeAttribute");
        _includeBaseForgeAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.IncludeBaseForgeAttribute");
        _forgeAllDerivedAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ForgeAllDerivedAttribute");
        _convertWithAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.ConvertWithAttribute");
        _assemblyDefaults = assemblyDefaults;
    }

    /// <summary>
    /// Resolves the effective configuration for a forger class by overlaying
    /// class-level [ForgeMap] attribute settings on top of assembly defaults.
    /// </summary>
    private ForgerConfig ResolveForgerConfig(ForgerInfo forger)
    {
        var config = new ForgerConfig
        {
            NullHandling = _assemblyDefaults.NullHandling,
            PropertyMatching = _assemblyDefaults.PropertyMatching,
            GenerateCollectionMappings = _assemblyDefaults.GenerateCollectionMappings,
            SuppressDiagnostics = new HashSet<string>(_assemblyDefaults.SuppressDiagnostics, StringComparer.OrdinalIgnoreCase),
        };

        var attr = forger.ForgeMapAttribute;
        foreach (var named in attr.NamedArguments)
        {
            switch (named.Key)
            {
                case "NullHandling":
                    config.NullHandling = (int)named.Value.Value!;
                    break;
                case "PropertyMatching":
                    config.PropertyMatching = (int)named.Value.Value!;
                    break;
                case "SuppressDiagnostics":
                    if (!named.Value.IsNull)
                    {
                        foreach (var item in named.Value.Values)
                        {
                            if (item.Value is string s)
                                config.SuppressDiagnostics.Add(s);
                        }
                    }
                    break;
            }
        }

        return config;
    }

    /// <summary>
    /// Reports a diagnostic unless it is suppressed by the forger's SuppressDiagnostics config.
    /// </summary>
    private void ReportDiagnosticIfNotSuppressed(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        Location? location,
        params object?[] messageArgs)
    {
        if (_config.SuppressDiagnostics.Contains(descriptor.Id))
            return;

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, messageArgs));
    }

    /// <summary>
    /// Generates the null-check code for source parameter based on NullHandling config.
    /// For ReturnNull: "if (source == null) return {nullReturn};" (or "return;" if nullReturn is null)
    /// For ThrowException: "if (source == null) throw new ArgumentNullException(nameof(source));"
    /// </summary>
    private string GenerateNullCheck(string sourceParam, string? nullReturn)
    {
        if (_config.NullHandling == 1) // ThrowException
        {
            return $"            if ({sourceParam} == null) throw new global::System.ArgumentNullException(nameof({sourceParam}));";
        }

        // ReturnNull (default)
        if (nullReturn == null)
            return $"            if ({sourceParam} == null) return;";
        return $"            if ({sourceParam} == null) return {nullReturn};";
    }

    public string GenerateForger(ForgerInfo forger, SourceProductionContext context)
    {
        _config = ResolveForgerConfig(forger);
        var sb = new StringBuilder();

        // File header
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();

        // Namespace
        var ns = forger.Symbol.ContainingNamespace;
        if (!ns.IsGlobalNamespace)
        {
            sb.AppendLine($"namespace {ns.ToDisplayString()}");
            sb.AppendLine("{");
        }

        // Class declaration with same accessibility
        var accessibility = GetAccessibilityKeyword(forger.Symbol.DeclaredAccessibility);
        var className = forger.Symbol.IsGenericType
            ? $"{forger.Symbol.Name}<{string.Join(", ", forger.Symbol.TypeParameters.Select(tp => tp.Name))}>"
            : forger.Symbol.Name;
        sb.AppendLine($"    {accessibility} partial class {className}");

        // Emit type parameter constraints for generic forgers
        if (forger.Symbol.IsGenericType)
        {
            foreach (var tp in forger.Symbol.TypeParameters)
            {
                var constraints = new List<string>();
                if (tp.HasReferenceTypeConstraint) constraints.Add("class");
                if (tp.HasValueTypeConstraint) constraints.Add("struct");
                if (tp.HasUnmanagedTypeConstraint) constraints.Add("unmanaged");
                if (tp.HasNotNullConstraint) constraints.Add("notnull");
                foreach (var ct in tp.ConstraintTypes)
                    constraints.Add(ct.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                if (tp.HasConstructorConstraint) constraints.Add("new()");
                if (constraints.Count > 0)
                    sb.AppendLine($"        where {tp.Name} : {string.Join(", ", constraints)}");
            }
        }

        sb.AppendLine("    {");

        // Find and implement partial methods
        var partialMethods = forger.Symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.IsPartialDefinition && !m.ReturnsVoid || (m.IsPartialDefinition && m.ReturnsVoid && HasForgeIntoPattern(m)))
            .ToList();

        // Collect all method signatures (partial + non-partial) so we know which reverse methods to skip.
        // Including non-partial methods prevents generating a duplicate if the user provides an explicit
        // reverse implementation as a regular (non-partial) method.
        var declaredSignatures = new HashSet<string>();
        foreach (var member in forger.Symbol.GetMembers().OfType<IMethodSymbol>())
        {
            declaredSignatures.Add(GetMethodSignatureKey(member));
        }

        foreach (var method in partialMethods)
        {
            var generatedMethod = GenerateMethod(method, forger, context);
            if (!string.IsNullOrEmpty(generatedMethod))
            {
                sb.AppendLine(generatedMethod);
            }
        }

        // Generate reverse methods for [ReverseForge]-annotated methods
        foreach (var method in partialMethods)
        {
            if (!HasReverseForgeAttribute(method))
                continue;

            // Guard: [ReverseForge] requires exactly one source parameter and a non-void return type
            if (method.Parameters.Length != 1 || method.ReturnsVoid)
                continue;

            // Build the reverse signature key to check if an explicit declaration exists
            var sourceType = method.Parameters[0].Type;
            var destType = method.ReturnType;
            var reverseKey = $"{method.Name}({GetNullabilityNormalizedDisplayString(destType)}):{GetNullabilityNormalizedDisplayString(sourceType)}";

            if (declaredSignatures.Contains(reverseKey))
                continue; // Explicit reverse declaration takes precedence

            var reverseMethod = GenerateReverseMethod(method, forger, context);
            if (!string.IsNullOrEmpty(reverseMethod))
            {
                sb.AppendLine(reverseMethod);
            }
        }

        sb.AppendLine("    }");

        if (!ns.IsGlobalNamespace)
        {
            sb.AppendLine("}");
        }

        return sb.ToString();
    }

    private static bool HasForgeIntoPattern(IMethodSymbol method)
    {
        // Check if this is a ForgeInto pattern (void return with [UseExistingValue] parameter)
        return method.Parameters.Any(p =>
            p.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "UseExistingValueAttribute" ||
                a.AttributeClass?.ToDisplayString() == "ForgeMap.UseExistingValueAttribute"));
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
    /// Generates a safe local variable name from a type name (camelCase, prefixed with @ if it
    /// collides with a C# keyword so that any valid type name yields a valid identifier).
    /// </summary>
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

    /// <summary>
    /// Generates a reverse forging method for a method annotated with [ReverseForge].
    /// Swaps source and destination types, reverses [ForgeProperty] mappings,
    /// and emits warnings for [ForgeFrom] and [ForgeWith] that cannot be auto-reversed.
    /// </summary>
    private string GenerateReverseMethod(
        IMethodSymbol forwardMethod,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        var forwardSourceType = forwardMethod.Parameters[0].Type as INamedTypeSymbol;
        var forwardDestType = forwardMethod.ReturnType as INamedTypeSymbol;

        if (forwardSourceType == null || forwardDestType == null)
            return string.Empty;

        // [ReverseForge] only supports class/struct/record object mappings.
        // Enum, collection, primitive, and delegate types use specialized forward code paths
        // that cannot be auto-reversed (would produce invalid code like `return new int { };`).
        if (!IsReversibleObjectType(forwardSourceType) || !IsReversibleObjectType(forwardDestType))
            return string.Empty;

        // In reverse: source is the forward dest, dest is the forward source
        var reverseSourceType = forwardDestType;
        var reverseDestType = forwardSourceType;

        // Get forward attributes
        var forwardIgnored = GetIgnoredProperties(forwardMethod);
        var forwardPropertyMappings = GetPropertyMappings(forwardMethod);
        var forwardResolverMappings = GetResolverMappings(forwardMethod);
        var forwardForgeWithMappings = GetForgeWithMappings(forwardMethod);

        // Build reverseIgnored: translate forward ignored dest names to reverse dest (= forward source) names.
        // forwardIgnored contains forward dest property names. In reverse, the dest is the forward source type.
        // When [ForgeProperty] renames are involved (e.g. ForgeProperty("BookTitle", "DisplayTitle")),
        // the forward ignored name "DisplayTitle" needs to map back to "BookTitle" in the reverse dest.
        var reverseIgnored = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ignoredName in forwardIgnored)
        {
            // Check if any forward mapping maps TO this ignored dest name
            var forwardSourceName = forwardPropertyMappings
                .Where(kvp => string.Equals(kvp.Key, ignoredName, StringComparison.Ordinal))
                .Select(kvp => kvp.Value)
                .FirstOrDefault();

            if (forwardSourceName != null && !forwardSourceName.Contains("."))
            {
                // The forward source name IS the reverse dest property name
                reverseIgnored.Add(forwardSourceName);
            }
            else
            {
                // No rename — same property name in both directions
                reverseIgnored.Add(ignoredName);
            }
        }

        // Build reverse property mappings: swap source/dest names
        var reversePropertyMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in forwardPropertyMappings)
        {
            // Forward: ForgeProperty(sourceA, destB) means destB = source.A
            // Reverse: ForgeProperty(destB, sourceA) means sourceA = source.B
            // But only if the source prop is a simple name (not a nested path)
            var forwardSourceProp = kvp.Value;
            var forwardDestProp = kvp.Key;

            if (!forwardSourceProp.Contains("."))
            {
                reversePropertyMappings[forwardSourceProp] = forwardDestProp;
            }
            // Nested paths can't be reversed (would need to unflatten)
        }

        // Emit FM0012 warnings for [ForgeFrom] properties
        foreach (var kvp in forwardResolverMappings)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ForgeFromCannotBeReversed,
                forwardMethod.Locations.FirstOrDefault(),
                kvp.Key);
        }

        // Build reverse [ForgeWith] mappings and emit FM0015 warnings where nested method lacks [ReverseForge]
        var reverseForgeWithMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kvp in forwardForgeWithMappings)
        {
            var forwardDestPropName = kvp.Key;
            var forgingMethodName = kvp.Value;

            // Find the source property that maps to this dest property
            string? reverseDestPropName = null;
            if (forwardPropertyMappings.TryGetValue(forwardDestPropName, out var mappedSourceProp)
                && !mappedSourceProp.Contains(".")) // Nested paths can't be used as reverse dest properties
            {
                reverseDestPropName = mappedSourceProp;
            }
            else
            {
                reverseDestPropName = forwardDestPropName; // same name by convention
            }

            // Check if the nested forging method has [ReverseForge].
            // Use type-based matching (like FindForgingMethod) to resolve the correct overload,
            // since forgers commonly overload Forge(...) for multiple type pairs.
            // The forward nested method takes forwardSourcePropType and returns forwardDestPropType.
            var forwardDestPropType = GetMappableProperties(forwardDestType)
                .FirstOrDefault(p => p.Name == forwardDestPropName)?.Type;
            var forwardSourcePropType = reverseDestPropName != null
                ? GetMappableProperties(forwardSourceType)
                    .FirstOrDefault(p => p.Name == reverseDestPropName)?.Type
                : null;

            IMethodSymbol? nestedMethod = null;
            if (forwardSourcePropType != null && forwardDestPropType != null)
            {
                // Find the forward nested method by its type signature
                nestedMethod = forger.Symbol.GetMembers(forgingMethodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m =>
                        m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1 &&
                        SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, forwardSourcePropType) &&
                        SymbolEqualityComparer.Default.Equals(m.ReturnType, forwardDestPropType));
            }

            // Fallback: if types couldn't be resolved, try name-only matching
            nestedMethod ??= forger.Symbol.GetMembers(forgingMethodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsPartialDefinition && !m.ReturnsVoid && m.Parameters.Length == 1);

            if (nestedMethod != null && HasReverseForgeAttribute(nestedMethod))
            {
                // Also verify the nested method's types are actually reversible —
                // if they fail IsReversibleObjectType, no reverse overload will be generated,
                // and calling it would cause a compile error in the consumer project.
                var nestedSourceType = nestedMethod.Parameters[0].Type as INamedTypeSymbol;
                var nestedDestType = nestedMethod.ReturnType as INamedTypeSymbol;

                if (nestedSourceType != null && nestedDestType != null &&
                    IsReversibleObjectType(nestedSourceType) && IsReversibleObjectType(nestedDestType))
                {
                    reverseForgeWithMappings[reverseDestPropName!] = forgingMethodName;
                }
                else
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ForgeWithLacksReverseForge,
                        forwardMethod.Locations.FirstOrDefault(),
                        forgingMethodName);
                }
            }
            else
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeWithLacksReverseForge,
                    forwardMethod.Locations.FirstOrDefault(),
                    forgingMethodName);
            }
        }

        // Now generate the reverse method body using the same GenerateForgeMethod pattern
        // but with reversed types and mappings
        var sb = new StringBuilder();
        var sourceParam = "source";

        // Method signature
        var accessibility = GetAccessibilityKeyword(forwardMethod.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} {reverseDestType.ToDisplayString()} {forwardMethod.Name}({reverseSourceType.ToDisplayString()} {sourceParam})");
        sb.AppendLine("        {");

        // Null check (for reference types and Nullable<T>)
        if (reverseSourceType.IsReferenceType ||
            reverseSourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var nullReturn = reverseDestType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam, nullReturn));
            sb.AppendLine();
        }

        // Get mappable properties
        var sourceProperties = GetMappableProperties(reverseSourceType);
        var destProperties = GetMappableProperties(reverseDestType);

        // Determine constructor strategy for the reverse destination
        var (chosenCtor, ctorParamMappings) = ResolveConstructor(
            reverseDestType, reverseSourceType, sourceProperties, reversePropertyMappings, context, forwardMethod);

        // Track which destination properties are covered by constructor parameters
        var ctorCoveredDestProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ctorParamMappings != null)
        {
            foreach (var mapping in ctorParamMappings)
                ctorCoveredDestProps.Add(mapping.DestPropertyName);
        }

        // Empty resolver mappings for reverse (ForgeFrom is not reversed)
        var emptyResolverMappings = new Dictionary<string, string>(StringComparer.Ordinal);
        // Don't pass reverseForgeWithMappings to GeneratePropertyAssignment (it can't find the reverse
        // partial method via FindForgingMethod since it's auto-generated). We'll handle them inline.
        var emptyForgeWithMappings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (chosenCtor != null && ctorParamMappings != null && chosenCtor.Parameters.Length > 0)
        {
            sb.AppendLine($"            var result = new {reverseDestType.ToDisplayString()}(");

            for (int i = 0; i < ctorParamMappings.Count; i++)
            {
                var mapping = ctorParamMappings[i];
                var separator = i < ctorParamMappings.Count - 1 ? "," : "";
                var expr = GenerateCtorParamExpression(
                    mapping.SourceExpression, mapping.SourcePropertyType, mapping.DestPropertyType);
                sb.AppendLine($"                {mapping.CtorParamName}: {expr}{separator}");
            }

            var remainingDestProps = destProperties
                .Where(p => p.SetMethod != null && !ctorCoveredDestProps.Contains(p.Name))
                .ToList();

            var initAssignments = new List<(string Name, string Expr)>();
            foreach (var destProp in remainingDestProps)
            {
                if (reverseIgnored.Contains(destProp.Name))
                    continue;

                var assignment = GenerateReversePropertyAssignment(
                    destProp, sourceParam, reverseSourceType, sourceProperties,
                    reversePropertyMappings, emptyResolverMappings, emptyForgeWithMappings,
                    reverseForgeWithMappings, reverseIgnored, forger, context, forwardMethod);

                if (assignment != null)
                    initAssignments.Add((destProp.Name, assignment));
            }

            if (initAssignments.Count > 0)
            {
                sb.AppendLine("            )");
                sb.AppendLine("            {");
                foreach (var (name, expr) in initAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");
            }
            else
            {
                sb.AppendLine("            );");
            }

            sb.AppendLine("            return result;");
        }
        else
        {
            sb.AppendLine($"            return new {reverseDestType.ToDisplayString()}");
            sb.AppendLine("            {");

            foreach (var destProp in destProperties)
            {
                var assignment = GenerateReversePropertyAssignment(
                    destProp, sourceParam, reverseSourceType, sourceProperties,
                    reversePropertyMappings, emptyResolverMappings, emptyForgeWithMappings,
                    reverseForgeWithMappings, reverseIgnored, forger, context, forwardMethod);

                if (assignment != null)
                    sb.AppendLine($"                {destProp.Name} = {assignment},");
            }

            sb.AppendLine("            };");
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a property assignment for a reverse method. Handles [ForgeWith] inline
    /// since the reverse forging methods aren't user-declared partial methods and can't be found
    /// via FindForgingMethod. For non-ForgeWith properties, delegates to GeneratePropertyAssignment.
    /// </summary>
    private string? GenerateReversePropertyAssignment(
        IPropertySymbol destProp,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        Dictionary<string, string> reverseForgeWithMappings,
        HashSet<string> ignoredProperties,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // [Ignore] takes precedence over all other mappings including [ForgeWith]
        if (ignoredProperties.Contains(destProp.Name))
            return null;

        // Check if this property has a reverse [ForgeWith] mapping
        if (reverseForgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
        {
            // Find the source property manually
            string? sourcePropName = null;
            if (propertyMappings.TryGetValue(destProp.Name, out var mappedSource))
                sourcePropName = mappedSource;
            else
            {
                var match = sourceProperties.FirstOrDefault(sp =>
                    string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                if (match != null)
                    sourcePropName = match.Name;
            }

            if (sourcePropName != null)
            {
                var sourcePropertyType = ResolvePathLeafType(sourcePropName, sourceType);
                if (sourcePropertyType != null)
                {
                    var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceType);
                    if (sourcePropertyType.IsReferenceType)
                    {
                        var localVarName = $"__forgeWith_{destProp.Name}";
                        var nullFallback = destProp.Type.IsValueType ? $"default({destProp.Type.ToDisplayString()})" : "null!";
                        return $"{sourceExpr} is {{ }} {localVarName} ? {forgingMethodName}({localVarName}) : {nullFallback}";
                    }
                    else
                    {
                        return $"{forgingMethodName}({sourceExpr})";
                    }
                }
            }

            return null;
        }

        // Fall through to regular property assignment
        return GeneratePropertyAssignment(
            destProp, sourceParam, sourceType, sourceProperties,
            propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method);
    }

    private string GenerateMethod(IMethodSymbol method, ForgerInfo forger, SourceProductionContext context)
    {
        // Validate method has at least one parameter (source)
        if (method.Parameters.Length == 0)
        {
            return string.Empty;
        }

        var sourceType = method.Parameters[0].Type;
        var destinationType = method.ReturnType;

        // Handle ForgeInto pattern
        var useExistingParam = method.Parameters.FirstOrDefault(p =>
            p.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "UseExistingValueAttribute" ||
                a.AttributeClass?.ToDisplayString() == "ForgeMap.UseExistingValueAttribute"));

        if (useExistingParam != null)
        {
            destinationType = useExistingParam.Type;
            return GenerateForgeIntoMethod(method, sourceType, destinationType as INamedTypeSymbol, forger, context);
        }

        // Check for enum forging scenarios
        var enumResult = TryGenerateEnumForgeMethod(method, sourceType, destinationType);
        if (enumResult != null)
        {
            ReportHooksNotSupportedIfPresent(method, context);
            return enumResult;
        }

        // Check for collection mapping (List<T>, T[], IEnumerable<T>, etc.)
        var sourceElementType = GetCollectionElementType(sourceType);
        var destElementType = GetCollectionElementType(destinationType);
        if (sourceElementType != null && destElementType != null && _config.GenerateCollectionMappings)
        {
            ReportHooksNotSupportedIfPresent(method, context);
            return GenerateCollectionForgeMethod(method, sourceType, destinationType, sourceElementType, destElementType, forger, context);
        }

        if (destinationType is not INamedTypeSymbol destNamedType)
        {
            return string.Empty;
        }

        return GenerateForgeMethod(method, sourceType as INamedTypeSymbol, destNamedType, forger, context);
    }

    /// <summary>
    /// Tries to generate an enum forging method. Returns null if source/dest are not enum/string combinations.
    /// Handles: enum→enum (cast by name), enum→string (.ToString()), string→enum (Enum.Parse).
    /// </summary>
    private string? TryGenerateEnumForgeMethod(IMethodSymbol method, ITypeSymbol sourceType, ITypeSymbol destinationType)
    {
        var isSourceEnum = sourceType.TypeKind == TypeKind.Enum;
        var isDestEnum = destinationType.TypeKind == TypeKind.Enum;
        var isSourceString = sourceType.SpecialType == SpecialType.System_String;
        var isDestString = destinationType.SpecialType == SpecialType.System_String;

        if (!isSourceEnum && !isDestEnum)
            return null;

        // At least one side must be enum; the other must be enum or string
        if (!isSourceEnum && !isSourceString)
            return null;
        if (!isDestEnum && !isDestString)
            return null;

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var destDisplay = destinationType.ToDisplayString();
        var sourceDisplay = sourceType.ToDisplayString();

        sb.AppendLine($"        {accessibility} partial {destDisplay} {method.Name}({sourceDisplay} {sourceParam})");
        sb.AppendLine("        {");

        // Add null handling for reference-type inputs (string)
        if (isSourceString)
        {
            sb.AppendLine(GenerateNullCheck(sourceParam, "default"));
            sb.AppendLine();
        }

        if (isSourceEnum && isDestEnum)
        {
            // enum → enum: parse by name for safety (handles mismatched underlying values)
            sb.AppendLine($"            return ({destDisplay})global::System.Enum.Parse(typeof({destDisplay}), {sourceParam}.ToString());");
        }
        else if (isSourceEnum && isDestString)
        {
            // enum → string: .ToString()
            sb.AppendLine($"            return {sourceParam}.ToString();");
        }
        else if (isSourceString && isDestEnum)
        {
            // string → enum: Enum.Parse with case-insensitive matching
            sb.AppendLine($"            return ({destDisplay})global::System.Enum.Parse(typeof({destDisplay}), {sourceParam}, true);");
        }

        sb.AppendLine("        }");
        return sb.ToString();
    }

    private string GenerateForgeMethod(
        IMethodSymbol method,
        INamedTypeSymbol? sourceType,
        INamedTypeSymbol destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        if (sourceType == null)
            return string.Empty;

        // Check for [ForgeAllDerived] — polymorphic dispatch
        var hasForgeAllDerived = HasForgeAllDerivedAttribute(method);

        if (hasForgeAllDerived)
        {
            // FM0023: [ForgeAllDerived] cannot be combined with [ConvertWith]
            if (HasConvertWithAttribute(method))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeAllDerivedWithConvertWith,
                    method.Locations.FirstOrDefault(),
                    method.Name);

                // Emit a minimal throwing body so the partial method still compiles
                var errAccessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
                var errSb = new StringBuilder();
                errSb.AppendLine($"        {errAccessibility} partial {destinationType.ToDisplayString()} {method.Name}({sourceType.ToDisplayString()} {method.Parameters[0].Name})");
                errSb.AppendLine("        {");
                errSb.AppendLine("            throw new global::System.NotSupportedException(\"[ForgeAllDerived] cannot be combined with [ConvertWith].\");");
                errSb.AppendLine("        }");
                return errSb.ToString();
            }
        }

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;

        // Get ignored properties from [Ignore] attributes
        var ignoredProperties = GetIgnoredProperties(method);

        // Get property mappings from [ForgeProperty] attributes
        var propertyMappings = GetPropertyMappings(method);

        // Get resolver mappings from [ForgeFrom] attributes
        var resolverMappings = GetResolverMappings(method);

        // Get [ForgeWith] mappings for nested object forging
        var forgeWithMappings = GetForgeWithMappings(method);

        // Merge inherited configuration from [IncludeBaseForge] attributes
        ResolveInheritedConfig(method, forger, context, ignoredProperties, propertyMappings, resolverMappings, forgeWithMappings);

        // Get [BeforeForge] and [AfterForge] hooks
        var beforeForgeHooks = GetBeforeForgeHooks(method);
        var afterForgeHooks = GetAfterForgeHooks(method);

        // Validate hooks and filter to only valid ones
        beforeForgeHooks = beforeForgeHooks
            .Where(h => ValidateBeforeForgeHook(h, sourceType, forger, context, method))
            .ToList();
        afterForgeHooks = afterForgeHooks
            .Where(h => ValidateAfterForgeHook(h, sourceType, destinationType, forger, context, method))
            .ToList();

        var hasAfterForge = afterForgeHooks.Count > 0;

        // Method signature
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} partial {destinationType.ToDisplayString()} {method.Name}({sourceType.ToDisplayString()} {sourceParam})");
        sb.AppendLine("        {");

        // Null check (only for reference types or nullable value types)
        if (sourceType.IsReferenceType || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            var nullReturn = destinationType.IsValueType ? "default" : "null!";
            sb.AppendLine(GenerateNullCheck(sourceParam, nullReturn));
            sb.AppendLine();
        }

        // [ForgeAllDerived] — polymorphic dispatch is-cascade
        if (hasForgeAllDerived)
        {
            var derivedMethods = DiscoverDerivedForgeMethods(method, sourceType, destinationType, forger);

            if (derivedMethods.Count == 0)
            {
                // FM0022: no derived forge methods found
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ForgeAllDerivedNoDerivedMethods,
                    method.Locations.FirstOrDefault(),
                    method.Name);
            }
            else
            {
                sb.AppendLine("            // Polymorphic dispatch — most-derived types checked first");

                // Normalize identifiers so that `@foo` and `foo` are treated as the same name in C#
                static string NormalizeIdentifier(string name) =>
                    name.Length > 0 && name[0] == '@' ? name.Substring(1) : name;

                var usedNames = new HashSet<string>(StringComparer.Ordinal)
                {
                    NormalizeIdentifier(sourceParam),
                    NormalizeIdentifier("result")
                };
                foreach (var derived in derivedMethods)
                {
                    var derivedSourceDisplay = derived.Parameters[0].Type.ToDisplayString();
                    var displayName = GenerateSafeVariableName(derived.Parameters[0].Type);
                    var baseName = NormalizeIdentifier(displayName);
                    var varName = baseName;
                    if (!usedNames.Add(varName))
                    {
                        var suffix = 2;
                        do { varName = baseName + suffix++; } while (!usedNames.Add(varName));
                    }
                    // Re-apply @ escaping if the original name needed it and no suffix was added
                    var finalName = (displayName.Length > 0 && displayName[0] == '@' && varName == baseName)
                        ? displayName
                        : varName;
                    sb.AppendLine($"            if ({sourceParam} is {derivedSourceDisplay} {finalName}) return {method.Name}({finalName});");
                }
                sb.AppendLine();
            }
        }

        // [BeforeForge] callbacks
        foreach (var hookName in beforeForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam});");
        }
        if (beforeForgeHooks.Count > 0)
            sb.AppendLine();

        // Get mappable properties
        var sourceProperties = GetMappableProperties(sourceType);
        var destProperties = GetMappableProperties(destinationType);

        // Determine constructor strategy
        var (chosenCtor, ctorParamMappings) = ResolveConstructor(
            destinationType, sourceType, sourceProperties, propertyMappings, context, method);

        // Track which destination properties are covered by constructor parameters
        var ctorCoveredDestProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ctorParamMappings != null)
        {
            foreach (var mapping in ctorParamMappings)
                ctorCoveredDestProps.Add(mapping.DestPropertyName);
        }

        if (chosenCtor != null && ctorParamMappings != null && chosenCtor.Parameters.Length > 0)
        {
            // Constructor mapping: generate new Dest(param1: expr1, param2: expr2) { Prop = value, ... }
            // Using object initializer syntax so init-only properties work too
            sb.AppendLine($"            var result = new {destinationType.ToDisplayString()}(");

            for (int i = 0; i < ctorParamMappings.Count; i++)
            {
                var mapping = ctorParamMappings[i];
                var separator = i < ctorParamMappings.Count - 1 ? "," : "";
                var expr = GenerateCtorParamExpression(
                    mapping.SourceExpression, mapping.SourcePropertyType, mapping.DestPropertyType);
                sb.AppendLine($"                {mapping.CtorParamName}: {expr}{separator}");
            }

            // Collect remaining property assignments for object initializer
            var remainingDestProps = destProperties
                .Where(p => p.SetMethod != null && !ctorCoveredDestProps.Contains(p.Name))
                .ToList();

            var initAssignments = new List<(string Name, string Expr)>();
            foreach (var destProp in remainingDestProps)
            {
                if (ignoredProperties.Contains(destProp.Name))
                    continue;

                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method);

                if (assignment != null)
                    initAssignments.Add((destProp.Name, assignment));
            }

            if (initAssignments.Count > 0)
            {
                sb.AppendLine("            )");
                sb.AppendLine("            {");
                foreach (var (name, expr) in initAssignments)
                    sb.AppendLine($"                {name} = {expr},");
                sb.AppendLine("            };");
            }
            else
            {
                sb.AppendLine("            );");
            }

            // [AfterForge] callbacks
            foreach (var hookName in afterForgeHooks)
            {
                sb.AppendLine($"            {hookName}({sourceParam}, result);");
            }

            sb.AppendLine("            return result;");
        }
        else if (hasAfterForge)
        {
            // When AfterForge hooks exist, we need a variable to pass to the hooks
            sb.AppendLine($"            var result = new {destinationType.ToDisplayString()}");
            sb.AppendLine("            {");

            foreach (var destProp in destProperties.Where(p => p.SetMethod != null))
            {
                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method);

                if (assignment != null)
                    sb.AppendLine($"                {destProp.Name} = {assignment},");
            }

            sb.AppendLine("            };");

            // [AfterForge] callbacks
            foreach (var hookName in afterForgeHooks)
            {
                sb.AppendLine($"            {hookName}({sourceParam}, result);");
            }

            sb.AppendLine("            return result;");
        }
        else
        {
            // Object initializer pattern (existing behavior)
            sb.AppendLine($"            return new {destinationType.ToDisplayString()}");
            sb.AppendLine("            {");

            foreach (var destProp in destProperties)
            {
                var assignment = GeneratePropertyAssignment(
                    destProp, sourceParam, sourceType, sourceProperties,
                    propertyMappings, resolverMappings, forgeWithMappings, ignoredProperties, forger, context, method);

                if (assignment != null)
                    sb.AppendLine($"                {destProp.Name} = {assignment},");
            }

            sb.AppendLine("            };");
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a property assignment expression string, or null if the property should be skipped.
    /// </summary>
    private string? GeneratePropertyAssignment(
        IPropertySymbol destProp,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        HashSet<string> ignoredProperties,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        // Skip ignored properties
        if (ignoredProperties.Contains(destProp.Name))
            return null;

        // Check if this property has a resolver from [ForgeFrom]
        if (resolverMappings.TryGetValue(destProp.Name, out var resolverMethodName))
        {
            return GenerateResolverExpression(
                destProp, resolverMethodName, sourceParam, sourceType,
                sourceProperties, propertyMappings, forger, context, method);
        }

        // Check if this property has a [ForgeWith] mapping for nested object forging
        if (forgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
        {
            return GenerateForgeWithExpression(
                destProp, forgingMethodName, sourceParam, sourceType,
                sourceProperties, propertyMappings, forger, context, method);
        }

        // Check if this property has a mapping from [ForgeProperty]
        if (propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
        {
            var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceType);
            var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceType);
            // When null-conditional lifts a non-nullable value type to Nullable<T>, cast back
            var isLiftedValueType = hasNullConditional && sourceLeafType != null && sourceLeafType.IsValueType && GetNullableUnderlyingType(sourceLeafType) == null;
            // Try compatible enum cast first — pass isLifted so it generates correct nullable handling
            if (sourceLeafType != null)
            {
                var enumCast = TryGenerateCompatibleEnumCast(sourceLeafType, destProp.Type, sourceExpr, isLifted: isLiftedValueType);
                if (enumCast != null)
                    return enumCast;
            }
            if (isLiftedValueType && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
                return $"({destProp.Type.ToDisplayString()})({sourceExpr})!";
            var nullForgiving = hasNullConditional && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
            return $"{sourceExpr}{nullForgiving}";
        }

        // Try to find matching source property by name (convention)
        var sourceProp = sourceProperties.FirstOrDefault(sp =>
            string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));

        if (sourceProp != null && CanAssign(sourceProp.Type, destProp.Type))
        {
            if (IsNullableToNonNullableValueType(sourceProp.Type, destProp.Type))
                return $"({destProp.Type.ToDisplayString()}){sourceParam}.{sourceProp.Name}!";
            else
                return $"{sourceParam}.{sourceProp.Name}";
        }

        // Compatible enum cast: EnumA -> EnumB (different namespaces, same members)
        if (sourceProp != null)
        {
            var enumCastExpr = TryGenerateCompatibleEnumCast(sourceProp.Type, destProp.Type, $"{sourceParam}.{sourceProp.Name}");
            if (enumCastExpr != null)
                return enumCastExpr;
        }

        // Try automatic flattening: destProp "CustomerName" → source.Customer.Name
        var flattenResult = TryAutoFlatten(destProp, sourceParam, sourceType);
        if (flattenResult != null)
            return flattenResult;

        return null;
    }

    /// <summary>
    /// Generates a resolver expression for [ForgeFrom].
    /// </summary>
    private string? GenerateResolverExpression(
        IPropertySymbol destProp,
        string resolverMethodName,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        string? sourcePropPath = null;
        ITypeSymbol? sourcePathLeafType = null;

        if (propertyMappings.TryGetValue(destProp.Name, out sourcePropPath))
        {
            sourcePathLeafType = ResolvePathLeafType(sourcePropPath, sourceType);
        }
        else
        {
            var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
            if (matchingSourceProp != null)
            {
                sourcePropPath = matchingSourceProp.Name;
                sourcePathLeafType = matchingSourceProp.Type;
            }
        }

        var resolverMethod = FindResolverMethod(forger.Symbol, resolverMethodName, sourceType, sourcePathLeafType);

        if (resolverMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ResolverMethodNotFound,
                method.Locations.FirstOrDefault(),
                resolverMethodName);
            return null;
        }

        var resolverParamType = resolverMethod.Parameters[0].Type;

        if (SymbolEqualityComparer.Default.Equals(resolverParamType, sourceType) ||
            CanAssign(sourceType, resolverParamType))
        {
            return $"{resolverMethodName}({sourceParam})";
        }
        else if (sourcePropPath != null && sourcePathLeafType != null &&
                 CanAssign(sourcePathLeafType, resolverParamType))
        {
            var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceType);
            var isLiftedValueType = hasNullConditional && sourcePathLeafType.IsValueType && GetNullableUnderlyingType(sourcePathLeafType) == null;
            if (IsNullableToNonNullableValueType(sourcePathLeafType, resolverParamType) || isLiftedValueType)
            {
                return $"{resolverMethodName}(({resolverParamType.ToDisplayString()}){sourceExpr}!)";
            }
            else
            {
                var isNullableExpr = hasNullConditional || sourcePathLeafType.NullableAnnotation == NullableAnnotation.Annotated;
                var nullForgiving = isNullableExpr && resolverParamType.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                return $"{resolverMethodName}({sourceExpr}{nullForgiving})";
            }
        }
        else
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.InvalidResolverSignature,
                method.Locations.FirstOrDefault(),
                resolverMethodName);
            return null;
        }
    }

    /// <summary>
    /// Generates a [ForgeWith] expression for nested object forging.
    /// </summary>
    private string? GenerateForgeWithExpression(
        IPropertySymbol destProp,
        string forgingMethodName,
        string sourceParam,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        ForgerInfo forger,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        string? forgeWithSourcePropName = null;
        if (propertyMappings.TryGetValue(destProp.Name, out var mappedSource))
        {
            forgeWithSourcePropName = mappedSource;
        }
        else
        {
            var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
            if (matchingSourceProp != null)
                forgeWithSourcePropName = matchingSourceProp.Name;
        }

        if (forgeWithSourcePropName != null)
        {
            var sourcePropertyType = ResolvePathLeafType(forgeWithSourcePropName, sourceType);
            if (sourcePropertyType != null)
            {
                var nestedForgeMethod = FindForgingMethod(forger.Symbol, forgingMethodName, sourcePropertyType, destProp.Type);
                if (nestedForgeMethod != null)
                {
                    var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, forgeWithSourcePropName, sourceType);
                    if (sourcePropertyType.IsReferenceType)
                    {
                        var localVarName = $"__forgeWith_{destProp.Name}";
                        var nullFallback = destProp.Type.IsValueType ? "default" : "null!";
                        return $"{sourceExpr} is {{ }} {localVarName} ? {forgingMethodName}({localVarName}) : {nullFallback}";
                    }
                    else
                    {
                        return $"{forgingMethodName}({sourceExpr})";
                    }
                }
            }
        }

        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.ResolverMethodNotFound,
            method.Locations.FirstOrDefault(),
            forgingMethodName);
        return null;
    }

    /// <summary>
    /// Generates a mapping expression for a constructor parameter, handling Nullable&lt;T&gt; to T.
    /// </summary>
    private static string GenerateCtorParamExpression(
        string sourceExpression,
        ITypeSymbol? sourcePropertyType,
        ITypeSymbol destPropertyType)
    {
        if (sourcePropertyType != null && IsNullableToNonNullableValueType(sourcePropertyType, destPropertyType))
            return sourceExpression.Contains("?.") ? $"({destPropertyType.ToDisplayString()})({sourceExpression})!" : $"({destPropertyType.ToDisplayString()}){sourceExpression}!";

        // Handle lifted value type from null-conditional: source.Customer?.Age is int?
        // even though Age is int — cast back to the destination type
        var isLifted = sourceExpression.Contains("?.") && sourcePropertyType != null
            && sourcePropertyType.IsValueType && GetNullableUnderlyingType(sourcePropertyType) == null;

        if (isLifted && destPropertyType.IsValueType && GetNullableUnderlyingType(destPropertyType) == null)
        {
            // Try compatible enum cast first — it needs the isLifted flag for correct codegen
            var enumCast = TryGenerateCompatibleEnumCast(sourcePropertyType!, destPropertyType, sourceExpression, isLifted: true);
            if (enumCast != null)
                return enumCast;
            return $"({destPropertyType.ToDisplayString()})({sourceExpression})!";
        }

        // Compatible enum cast for constructor parameters
        if (sourcePropertyType != null)
        {
            var enumCastExpr = TryGenerateCompatibleEnumCast(sourcePropertyType, destPropertyType, sourceExpression, isLifted: isLifted);
            if (enumCastExpr != null)
                return enumCastExpr;
        }

        return sourceExpression;
    }

    /// <summary>
    /// Tries automatic flattening: matches dest property "CustomerName" to source path "Customer.Name".
    /// Walks the source type hierarchy looking for concatenated property name matches.
    /// </summary>
    private string? TryAutoFlatten(IPropertySymbol destProp, string sourceParam, INamedTypeSymbol sourceType)
    {
        var destName = destProp.Name;
        var (expr, leafType) = TryAutoFlattenRecursive(destName, 0, sourceParam, sourceType);
        if (expr == null || leafType == null)
            return null;

        // Validate type compatibility between leaf and destination property
        if (!CanAssign(leafType, destProp.Type))
            return null;

        // Handle nullable-to-non-nullable value type conversion
        if (IsNullableToNonNullableValueType(leafType, destProp.Type))
            return expr.Contains("?.") ? $"({destProp.Type.ToDisplayString()})({expr})" : $"({destProp.Type.ToDisplayString()}){expr}";

        // Handle lifted value type from null-conditional: source.Customer?.Age is int? even
        // though Age is int. The ! operator suppresses warnings but doesn't cast, so emit
        // an explicit cast to the destination type.
        if (expr.Contains("?.") && leafType.IsValueType && GetNullableUnderlyingType(leafType) == null
            && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
            return $"({destProp.Type.ToDisplayString()})({expr})";

        return expr;
    }

    private (string? Expression, ITypeSymbol? LeafType) TryAutoFlattenRecursive(string destName, int startIndex, string currentExpr, INamedTypeSymbol currentType)
    {
        if (startIndex >= destName.Length)
            return (null, null);

        var properties = GetMappableProperties(currentType).ToList();

        foreach (var prop in properties)
        {
            var propName = prop.Name;
            if (destName.Length >= startIndex + propName.Length &&
                string.Equals(destName.Substring(startIndex, propName.Length), propName, _config.PropertyNameComparison))
            {
                var newStartIndex = startIndex + propName.Length;

                if (newStartIndex == destName.Length)
                {
                    // Full match - this property is the leaf
                    // Use null-conditional only if the chain already has ?. (ancestor was nullable)
                    string leafExpr;
                    if (currentExpr.Contains("?."))
                        leafExpr = $"{currentExpr}?.{propName}!";
                    else
                        leafExpr = $"{currentExpr}.{propName}";
                    return (leafExpr, prop.Type);
                }

                // Partial match - recurse into this property's type
                if (prop.Type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Class)
                {
                    var nullConditionalExpr = prop.Type.IsReferenceType
                        ? $"{currentExpr}?.{propName}"
                        : $"{currentExpr}.{propName}";
                    var result = TryAutoFlattenRecursive(destName, newStartIndex, nullConditionalExpr, namedType);
                    if (result.Expression != null)
                        return result;
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Resolves the constructor to use for destination type instantiation.
    /// Returns (null, null) if a parameterless constructor should be used (object initializer pattern).
    /// </summary>
    private (IMethodSymbol? Constructor, List<CtorParamMapping>? Mappings) ResolveConstructor(
        INamedTypeSymbol destinationType,
        INamedTypeSymbol sourceType,
        IEnumerable<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings,
        SourceProductionContext context,
        IMethodSymbol method)
    {
        var constructors = destinationType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .ToList();

        // If a parameterless constructor exists, prefer it (object initializer pattern)
        var parameterlessCtor = constructors.FirstOrDefault(c => c.Parameters.Length == 0);
        if (parameterlessCtor != null)
            return (null, null);

        if (constructors.Count == 0)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.DestinationTypeHasNoConstructor,
                method.Locations.FirstOrDefault(),
                destinationType.Name);
            return (null, null);
        }

        // Build a reverse map: dest property name → source expression
        var destToSourceExpr = BuildDestToSourceMap(sourceType, sourceProperties.ToList(), propertyMappings);
        var sourcePropertiesList = sourceProperties.ToList();

        // Score each constructor by how many parameters can be satisfied
        var scoredCtors = new List<(IMethodSymbol Ctor, List<CtorParamMapping> Mappings, int Score)>();

        foreach (var ctor in constructors)
        {
            var mappings = new List<CtorParamMapping>();
            var allMatched = true;

            foreach (var param in ctor.Parameters)
            {
                var paramName = param.Name;

                // Try case-insensitive match against destination property names first
                // (constructor parameter matching is always case-insensitive per spec)
                string? matchedDestPropName = null;
                string? sourceExpr = null;
                ITypeSymbol? sourcePropType = null;

                // Check if any dest prop matches this ctor param (case-insensitive)
                foreach (var kvp in destToSourceExpr)
                {
                    if (string.Equals(kvp.Key, paramName, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedDestPropName = kvp.Key;
                        sourceExpr = kvp.Value.Expression;
                        sourcePropType = kvp.Value.Type;
                        break;
                    }
                }

                // Also try direct source property match (case-insensitive)
                if (sourceExpr == null)
                {
                    var directMatch = sourcePropertiesList.FirstOrDefault(sp =>
                        string.Equals(sp.Name, paramName, StringComparison.OrdinalIgnoreCase));
                    if (directMatch != null)
                    {
                        matchedDestPropName = paramName;
                        sourceExpr = $"source.{directMatch.Name}";
                        sourcePropType = directMatch.Type;
                    }
                }

                if (sourceExpr != null && sourcePropType != null &&
                    (CanAssign(sourcePropType, param.Type) || IsCompatibleEnumPair(sourcePropType, param.Type)))
                {
                    mappings.Add(new CtorParamMapping(param.Name, matchedDestPropName!, sourceExpr, sourcePropType, param.Type));
                }
                else
                {
                    allMatched = false;
                    break;
                }
            }

            if (allMatched)
            {
                scoredCtors.Add((ctor, mappings, ctor.Parameters.Length));
            }
        }

        if (scoredCtors.Count == 0)
        {
            // No fully-matched ctor. Report FM0014 for the constructor with the most params.
            var bestCtor = constructors.OrderByDescending(c => c.Parameters.Length).First();
            foreach (var param in bestCtor.Parameters)
            {
                var found = destToSourceExpr.Keys.Any(k => string.Equals(k, param.Name, StringComparison.OrdinalIgnoreCase))
                    || sourcePropertiesList.Any(sp => string.Equals(sp.Name, param.Name, StringComparison.OrdinalIgnoreCase));
                if (!found)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ConstructorParameterNotMatched,
                        method.Locations.FirstOrDefault(),
                        param.Name,
                        destinationType.Name);
                }
            }
            return (null, null);
        }

        // Pick best (most parameters matched)
        var maxScore = scoredCtors.Max(s => s.Score);
        var bestMatches = scoredCtors.Where(s => s.Score == maxScore).ToList();

        if (bestMatches.Count > 1)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.AmbiguousConstructor,
                method.Locations.FirstOrDefault(),
                destinationType.Name);
            return (null, null);
        }

        return (bestMatches[0].Ctor, bestMatches[0].Mappings);
    }

    /// <summary>
    /// Builds a mapping from destination property name → source expression info.
    /// Includes [ForgeProperty] mappings and direct name matches.
    /// </summary>
    private Dictionary<string, (string Expression, ITypeSymbol? Type)> BuildDestToSourceMap(
        INamedTypeSymbol sourceType,
        List<IPropertySymbol> sourceProperties,
        Dictionary<string, string> propertyMappings)
    {
        var map = new Dictionary<string, (string Expression, ITypeSymbol? Type)>(StringComparer.OrdinalIgnoreCase);

        // Add all direct source property matches by name
        foreach (var sp in sourceProperties)
        {
            map[sp.Name] = ($"source.{sp.Name}", sp.Type);
        }

        // Overlay [ForgeProperty] mappings (dest name → source path)
        foreach (var kvp in propertyMappings)
        {
            var destPropName = kvp.Key;
            var sourcePath = kvp.Value;
            var leafType = ResolvePathLeafType(sourcePath, sourceType);

            if (sourcePath.Contains("."))
            {
                var (expr, _) = GenerateSourceExpressionWithNullInfo("source", sourcePath, sourceType);
                map[destPropName] = (expr, leafType);
            }
            else
            {
                map[destPropName] = ($"source.{sourcePath}", leafType);
            }
        }

        return map;
    }

    private sealed class CtorParamMapping
    {
        public CtorParamMapping(string ctorParamName, string destPropertyName, string sourceExpression, ITypeSymbol? sourcePropertyType, ITypeSymbol destPropertyType)
        {
            CtorParamName = ctorParamName;
            DestPropertyName = destPropertyName;
            SourceExpression = sourceExpression;
            SourcePropertyType = sourcePropertyType;
            DestPropertyType = destPropertyType;
        }

        public string CtorParamName { get; }
        public string DestPropertyName { get; }
        public string SourceExpression { get; }
        public ITypeSymbol? SourcePropertyType { get; }
        public ITypeSymbol DestPropertyType { get; }
    }

    private HashSet<string> GetIgnoredProperties(IMethodSymbol method)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);

        if (_ignoreAttributeSymbol == null)
            return ignored;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _ignoreAttributeSymbol))
                continue;

            // The [Ignore] attribute takes params string[] propertyNames
            if (attr.ConstructorArguments.Length > 0)
            {
                var arg = attr.ConstructorArguments[0];
                if (arg.Kind == TypedConstantKind.Array)
                {
                    foreach (var item in arg.Values)
                    {
                        if (item.Value is string propName)
                        {
                            ignored.Add(propName);
                        }
                    }
                }
            }
        }

        return ignored;
    }

    /// <summary>
    /// Gets property mappings from [ForgeProperty] attributes.
    /// Returns a dictionary mapping destination property name to source property path.
    /// </summary>
    private Dictionary<string, string> GetPropertyMappings(IMethodSymbol method)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_forgePropertyAttributeSymbol == null)
            return mappings;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _forgePropertyAttributeSymbol))
                continue;

            // [ForgeProperty(sourceProperty, destinationProperty)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var sourceProperty = attr.ConstructorArguments[0].Value as string;
                var destinationProperty = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(sourceProperty) && !string.IsNullOrEmpty(destinationProperty))
                {
                    mappings[destinationProperty!] = sourceProperty!;
                }
            }
        }

        return mappings;
    }

    /// <summary>
    /// Gets resolver mappings from [ForgeFrom] attributes.
    /// Returns a dictionary mapping destination property name to resolver method name.
    /// </summary>
    private Dictionary<string, string> GetResolverMappings(IMethodSymbol method)
    {
        var resolvers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_forgeFromAttributeSymbol == null)
            return resolvers;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _forgeFromAttributeSymbol))
                continue;

            // [ForgeFrom(destinationProperty, resolverMethodName)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[0].Value as string;
                var resolverMethodName = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(destinationProperty) && !string.IsNullOrEmpty(resolverMethodName))
                {
                    resolvers[destinationProperty!] = resolverMethodName!;
                }
            }
        }

        return resolvers;
    }

    /// <summary>
    /// Gets [ForgeWith] mappings.
    /// Returns a dictionary mapping destination property name to forging method name.
    /// </summary>
    private Dictionary<string, string> GetForgeWithMappings(IMethodSymbol method)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);

        if (_forgeWithAttributeSymbol == null)
            return mappings;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _forgeWithAttributeSymbol))
                continue;

            // [ForgeWith(destinationProperty, forgingMethodName)]
            if (attr.ConstructorArguments.Length >= 2)
            {
                var destinationProperty = attr.ConstructorArguments[0].Value as string;
                var forgingMethodName = attr.ConstructorArguments[1].Value as string;

                if (!string.IsNullOrEmpty(destinationProperty) && !string.IsNullOrEmpty(forgingMethodName))
                {
                    mappings[destinationProperty!] = forgingMethodName!;
                }
            }
        }

        return mappings;
    }

    /// <summary>
    /// Gets [IncludeBaseForge] attribute data from a method.
    /// Returns a list of (BaseSourceType, BaseDestinationType, AttributeData) tuples.
    /// </summary>
    private List<(INamedTypeSymbol BaseSourceType, INamedTypeSymbol BaseDestType, AttributeData Attribute)> GetIncludeBaseForgeAttributes(IMethodSymbol method)
    {
        var result = new List<(INamedTypeSymbol, INamedTypeSymbol, AttributeData)>();

        if (_includeBaseForgeAttributeSymbol == null)
            return result;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _includeBaseForgeAttributeSymbol))
                continue;

            // [IncludeBaseForge(typeof(BaseSource), typeof(BaseDest))]
            if (attr.ConstructorArguments.Length >= 2 &&
                attr.ConstructorArguments[0].Value is INamedTypeSymbol baseSourceType &&
                attr.ConstructorArguments[1].Value is INamedTypeSymbol baseDestType)
            {
                result.Add((baseSourceType, baseDestType, attr));
            }
        }

        return result;
    }

    /// <summary>
    /// Finds a forge method in the forger class that maps the given source type to the given destination type.
    /// Used for [IncludeBaseForge] resolution.
    /// </summary>
    private static IMethodSymbol? FindBaseForgeMethod(INamedTypeSymbol forgerType, INamedTypeSymbol baseSourceType, INamedTypeSymbol baseDestType)
    {
        var methods = forgerType.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.IsPartialDefinition);

        // Prefer return-style: non-void return, single parameter (source), return type = dest
        // Use stable ordering by name for deterministic resolution when multiple candidates exist
        var returnStyle = methods
            .Where(m =>
                !m.ReturnsVoid &&
                m.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, baseSourceType) &&
                SymbolEqualityComparer.Default.Equals(m.ReturnType, baseDestType))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (returnStyle != null)
            return returnStyle;

        // Fall back to ForgeInto-style: void return, two parameters where the destination
        // parameter is marked with [UseExistingValue] and matches baseDestType
        return methods
            .Where(m =>
                m.ReturnsVoid &&
                m.Parameters.Length == 2 &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, baseSourceType) &&
                m.Parameters.Any(p =>
                    SymbolEqualityComparer.Default.Equals(p.Type, baseDestType) &&
                    p.GetAttributes().Any(a =>
                    {
                        var attrClass = a.AttributeClass;
                        if (attrClass == null)
                            return false;
                        var name = attrClass.Name;
                        if (string.Equals(name, "UseExistingValueAttribute", StringComparison.Ordinal))
                            return true;
                        var fullName = attrClass.ToDisplayString();
                        return string.Equals(fullName, "ForgeMap.UseExistingValueAttribute", StringComparison.Ordinal);
                    })))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Checks whether <paramref name="derived"/> is assignable to <paramref name="baseType"/>
    /// (i.e. is the same type, a subclass, or implements the interface).
    /// </summary>
    private static bool DerivesFrom(INamedTypeSymbol derived, INamedTypeSymbol baseType)
    {
        INamedTypeSymbol? current = derived;
        while (current != null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }

        // Also check interface implementation
        if (baseType.TypeKind == TypeKind.Interface)
        {
            foreach (var iface in derived.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(iface, baseType))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves inherited configuration from [IncludeBaseForge] attributes.
    /// Merges base config into the given collections; explicit attributes on the derived method take precedence.
    /// Supports chaining through multiple inheritance levels.
    /// </summary>
    private void ResolveInheritedConfig(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context,
        HashSet<string> ignoredProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings)
    {
        ResolveInheritedConfig(method, forger, context, ignoredProperties, propertyMappings, resolverMappings, forgeWithMappings, new HashSet<string>());
    }

    private void ResolveInheritedConfig(
        IMethodSymbol method,
        ForgerInfo forger,
        SourceProductionContext context,
        HashSet<string> ignoredProperties,
        Dictionary<string, string> propertyMappings,
        Dictionary<string, string> resolverMappings,
        Dictionary<string, string> forgeWithMappings,
        HashSet<string> visited)
    {
        var includeBaseForges = GetIncludeBaseForgeAttributes(method);
        if (includeBaseForges.Count == 0)
            return;

        var sourceType = method.Parameters[0].Type as INamedTypeSymbol;

        // For ForgeInto methods (void return with [UseExistingValue] param), derive destType
        // from the [UseExistingValue] parameter instead of ReturnType.
        INamedTypeSymbol? destType;
        if (method.ReturnsVoid)
        {
            var useExistingParam = method.Parameters.FirstOrDefault(p =>
                p.GetAttributes().Any(a =>
                    a.AttributeClass?.Name == "UseExistingValueAttribute" ||
                    a.AttributeClass?.ToDisplayString() == "ForgeMap.UseExistingValueAttribute"));
            destType = useExistingParam?.Type as INamedTypeSymbol;
        }
        else
        {
            destType = method.ReturnType as INamedTypeSymbol;
        }

        if (sourceType == null || destType == null)
            return;

        // Collect the set of properties explicitly configured on the derived method
        // so we can detect and report FM0021 overrides
        var explicitIgnored = new HashSet<string>(ignoredProperties, StringComparer.Ordinal);
        var explicitPropertyMappings = new HashSet<string>(propertyMappings.Keys, StringComparer.Ordinal);
        var explicitResolverMappings = new HashSet<string>(resolverMappings.Keys, StringComparer.Ordinal);
        var explicitForgeWithMappings = new HashSet<string>(forgeWithMappings.Keys, StringComparer.Ordinal);

        foreach (var (baseSourceType, baseDestType, attrData) in includeBaseForges)
        {
            // Cycle detection: build a key from (source, dest) type pair
            var pairKey = $"{baseSourceType.ToDisplayString()}->{baseDestType.ToDisplayString()}";
            if (!visited.Add(pairKey))
                continue; // Already visited this pair — skip to avoid infinite recursion

            // Validate: source type must derive from base source type
            if (!DerivesFrom(sourceType, baseSourceType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeTypeMismatch,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    sourceType.ToDisplayString(), baseSourceType.ToDisplayString());
                continue;
            }

            // Validate: dest type must derive from base dest type
            if (!DerivesFrom(destType, baseDestType))
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeTypeMismatch,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    destType.ToDisplayString(), baseDestType.ToDisplayString());
                continue;
            }

            // Find the base forge method
            var baseMethod = FindBaseForgeMethod(forger.Symbol, baseSourceType, baseDestType);
            if (baseMethod == null)
            {
                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.IncludeBaseForgeMethodNotFound,
                    attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                    baseSourceType.ToDisplayString(), baseDestType.ToDisplayString());
                continue;
            }

            // Recursively resolve the base method's inherited config first (chaining support)
            var baseIgnored = GetIgnoredProperties(baseMethod);
            var basePropertyMappings = GetPropertyMappings(baseMethod);
            var baseResolverMappings = GetResolverMappings(baseMethod);
            var baseForgeWithMappings = GetForgeWithMappings(baseMethod);
            ResolveInheritedConfig(baseMethod, forger, context, baseIgnored, basePropertyMappings, baseResolverMappings, baseForgeWithMappings, visited);

            // Merge base [Ignore] into derived
            foreach (var propName in baseIgnored)
            {
                // FM0021: explicit attribute overrides inherited
                if (explicitIgnored.Contains(propName) || explicitPropertyMappings.Contains(propName) ||
                    explicitResolverMappings.Contains(propName) || explicitForgeWithMappings.Contains(propName))
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.IncludeBaseForgeOverridden,
                        attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                        propName);
                    continue;
                }
                // First-wins: skip if already configured by a previous [IncludeBaseForge]
                if (ignoredProperties.Contains(propName) || propertyMappings.ContainsKey(propName) ||
                    resolverMappings.ContainsKey(propName) || forgeWithMappings.ContainsKey(propName))
                    continue;
                ignoredProperties.Add(propName);
            }

            // Merge base [ForgeProperty] into derived
            foreach (var kvp in basePropertyMappings)
            {
                if (explicitIgnored.Contains(kvp.Key) || explicitPropertyMappings.Contains(kvp.Key) ||
                    explicitResolverMappings.Contains(kvp.Key) || explicitForgeWithMappings.Contains(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.IncludeBaseForgeOverridden,
                        attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                        kvp.Key);
                    continue;
                }
                if (ignoredProperties.Contains(kvp.Key) || propertyMappings.ContainsKey(kvp.Key) ||
                    resolverMappings.ContainsKey(kvp.Key) || forgeWithMappings.ContainsKey(kvp.Key))
                    continue;
                propertyMappings[kvp.Key] = kvp.Value;
            }

            // Merge base [ForgeFrom] into derived
            foreach (var kvp in baseResolverMappings)
            {
                if (explicitIgnored.Contains(kvp.Key) || explicitPropertyMappings.Contains(kvp.Key) ||
                    explicitResolverMappings.Contains(kvp.Key) || explicitForgeWithMappings.Contains(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.IncludeBaseForgeOverridden,
                        attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                        kvp.Key);
                    continue;
                }
                if (ignoredProperties.Contains(kvp.Key) || propertyMappings.ContainsKey(kvp.Key) ||
                    resolverMappings.ContainsKey(kvp.Key) || forgeWithMappings.ContainsKey(kvp.Key))
                    continue;
                resolverMappings[kvp.Key] = kvp.Value;
            }

            // Merge base [ForgeWith] into derived
            foreach (var kvp in baseForgeWithMappings)
            {
                if (explicitIgnored.Contains(kvp.Key) || explicitPropertyMappings.Contains(kvp.Key) ||
                    explicitResolverMappings.Contains(kvp.Key) || explicitForgeWithMappings.Contains(kvp.Key))
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.IncludeBaseForgeOverridden,
                        attrData.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault(),
                        kvp.Key);
                    continue;
                }
                if (ignoredProperties.Contains(kvp.Key) || propertyMappings.ContainsKey(kvp.Key) ||
                    resolverMappings.ContainsKey(kvp.Key) || forgeWithMappings.ContainsKey(kvp.Key))
                    continue;
                forgeWithMappings[kvp.Key] = kvp.Value;
            }
        }
    }

    /// <summary>
    /// Gets [BeforeForge] hook method names in declaration order.
    /// </summary>
    private List<string> GetBeforeForgeHooks(IMethodSymbol method)
    {
        var hooks = new List<string>();

        if (_beforeForgeAttributeSymbol == null)
            return hooks;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _beforeForgeAttributeSymbol))
                continue;

            if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string methodName)
            {
                hooks.Add(methodName);
            }
        }

        return hooks;
    }

    /// <summary>
    /// Gets [AfterForge] hook method names in declaration order.
    /// </summary>
    private List<string> GetAfterForgeHooks(IMethodSymbol method)
    {
        var hooks = new List<string>();

        if (_afterForgeAttributeSymbol == null)
            return hooks;

        foreach (var attr in method.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _afterForgeAttributeSymbol))
                continue;

            if (attr.ConstructorArguments.Length >= 1 && attr.ConstructorArguments[0].Value is string methodName)
            {
                hooks.Add(methodName);
            }
        }

        return hooks;
    }

    /// <summary>
    /// Validates a BeforeForge hook method. Must be void with a single parameter matching the source type.
    /// </summary>
    private bool ValidateBeforeForgeHook(
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
            return false;
        }

        // Prefer exact type match over assignable match
        var exactMatch = candidates.FirstOrDefault(m =>
            SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType));

        if (exactMatch != null || candidates.Count == 1)
            return true;

        // Multiple assignable candidates with no exact match — ambiguous
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.HookMethodInvalid,
            method.Locations.FirstOrDefault(),
            hookMethodName);
        return false;
    }

    /// <summary>
    /// Validates an AfterForge hook method. Must be void with two parameters: source type and destination type.
    /// </summary>
    private bool ValidateAfterForgeHook(
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
            return false;
        }

        // Prefer exact type match over assignable match
        var exactMatch = candidates.FirstOrDefault(m =>
            SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, sourceType) &&
            SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, destType));

        if (exactMatch != null || candidates.Count == 1)
            return true;

        // Multiple assignable candidates with no exact match — ambiguous
        ReportDiagnosticIfNotSuppressed(context,
            DiagnosticDescriptors.HookMethodInvalid,
            method.Locations.FirstOrDefault(),
            hookMethodName);
        return false;
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
    /// Generates the source expression for a property, handling nested paths.
    /// Returns a tuple of (expression, hasNullConditional).
    /// </summary>
    private (string Expression, bool HasNullConditional) GenerateSourceExpressionWithNullInfo(string sourceParam, string sourcePath, INamedTypeSymbol sourceType)
    {
        if (!sourcePath.Contains("."))
        {
            return ($"{sourceParam}.{sourcePath}", false);
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
                expression.Append($".{part}?");
                hasNullConditional = true;
            }
            else
            {
                expression.Append($".{part}");
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

    private string GenerateForgeIntoMethod(
        IMethodSymbol method,
        ITypeSymbol sourceType,
        INamedTypeSymbol? destinationType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        if (destinationType == null)
            return string.Empty;

        var sourceNamedType = sourceType as INamedTypeSymbol;
        if (sourceNamedType == null)
            return string.Empty;

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;
        var destParam = method.Parameters.First(p =>
            p.GetAttributes().Any(a =>
                a.AttributeClass?.Name == "UseExistingValueAttribute")).Name;

        // Get ignored properties from [Ignore] attributes
        var ignoredProperties = GetIgnoredProperties(method);

        // Get property mappings from [ForgeProperty] attributes
        var propertyMappings = GetPropertyMappings(method);

        // Get resolver mappings from [ForgeFrom] attributes
        var resolverMappings = GetResolverMappings(method);

        // Get [ForgeWith] mappings for nested object forging
        var forgeWithMappings = GetForgeWithMappings(method);

        // Merge inherited configuration from [IncludeBaseForge] attributes
        ResolveInheritedConfig(method, forger, context, ignoredProperties, propertyMappings, resolverMappings, forgeWithMappings);

        // Get [BeforeForge] and [AfterForge] hooks
        var beforeForgeHooks = GetBeforeForgeHooks(method);
        var afterForgeHooks = GetAfterForgeHooks(method);

        // Validate hooks and filter to only valid ones
        beforeForgeHooks = beforeForgeHooks
            .Where(h => ValidateBeforeForgeHook(h, sourceType, forger, context, method))
            .ToList();
        afterForgeHooks = afterForgeHooks
            .Where(h => ValidateAfterForgeHook(h, sourceType, destinationType, forger, context, method))
            .ToList();

        // Method signature
        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        sb.AppendLine($"        {accessibility} partial void {method.Name}({sourceType.ToDisplayString()} {sourceParam}, {destinationType.ToDisplayString()} {destParam})");
        sb.AppendLine("        {");

        // Null checks
        sb.AppendLine($"            if ({destParam} == null) throw new global::System.ArgumentNullException(nameof({destParam}));");
        if (sourceType.IsReferenceType || sourceType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            sb.AppendLine(GenerateNullCheck(sourceParam, null));
        }
        sb.AppendLine();

        // [BeforeForge] callbacks
        foreach (var hookName in beforeForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam});");
        }
        if (beforeForgeHooks.Count > 0)
            sb.AppendLine();

        // Get mappable properties
        var sourceProperties = GetMappableProperties(sourceNamedType);
        var destProperties = GetMappableProperties(destinationType).Where(p => p.SetMethod != null);

        foreach (var destProp in destProperties)
        {
            // Skip ignored properties
            if (ignoredProperties.Contains(destProp.Name))
                continue;

            // Check if this property has a resolver from [ForgeFrom]
            if (resolverMappings.TryGetValue(destProp.Name, out var resolverMethodName))
            {
                // Find the source property path for this destination (if mapped via [ForgeProperty])
                string? sourcePropPath = null;
                ITypeSymbol? sourcePathLeafType = null;

                if (propertyMappings.TryGetValue(destProp.Name, out sourcePropPath))
                {
                    // Resolve the leaf type for the full path (handles nested paths like "Customer.Name")
                    sourcePathLeafType = ResolvePathLeafType(sourcePropPath, sourceNamedType);
                }
                else
                {
                    // Try to find by same name
                    var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                        string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                    if (matchingSourceProp != null)
                    {
                        sourcePropPath = matchingSourceProp.Name;
                        sourcePathLeafType = matchingSourceProp.Type;
                    }
                }

                // Find the resolver method - pass leaf type directly for proper overload selection
                var resolverMethod = FindResolverMethod(forger.Symbol, resolverMethodName, sourceType, sourcePathLeafType);

                if (resolverMethod == null)
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.ResolverMethodNotFound,
                        method.Locations.FirstOrDefault(),
                        resolverMethodName);
                    continue;
                }

                var resolverParamType = resolverMethod.Parameters[0].Type;
                string resolverCall;

                if (SymbolEqualityComparer.Default.Equals(resolverParamType, sourceType) ||
                    CanAssign(sourceType, resolverParamType))
                {
                    resolverCall = $"{resolverMethodName}({sourceParam})";
                }
                else if (sourcePropPath != null && sourcePathLeafType != null &&
                         CanAssign(sourcePathLeafType, resolverParamType))
                {
                    // Pass the source property value - use null-info to handle nullable expressions
                    var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropPath, sourceNamedType);

                    // Handle Nullable<T> to T conversion with explicit cast
                    // Also handle lifted value types from null-conditional (e.g., source.Customer?.Age becomes int?)
                    var isLiftedValueType = hasNullConditional && sourcePathLeafType.IsValueType && GetNullableUnderlyingType(sourcePathLeafType) == null;
                    if (IsNullableToNonNullableValueType(sourcePathLeafType, resolverParamType) || isLiftedValueType)
                    {
                        resolverCall = $"{resolverMethodName}(({resolverParamType.ToDisplayString()}){sourceExpr}!)";
                    }
                    else
                    {
                        // Add null-forgiving if expression is nullable (from null-conditional or nullable ref type)
                        // but resolver param is non-nullable
                        var isNullableExpr = hasNullConditional || sourcePathLeafType.NullableAnnotation == NullableAnnotation.Annotated;
                        var nullForgiving = isNullableExpr && resolverParamType.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                        resolverCall = $"{resolverMethodName}({sourceExpr}{nullForgiving})";
                    }
                }
                else
                {
                    ReportDiagnosticIfNotSuppressed(context,
                        DiagnosticDescriptors.InvalidResolverSignature,
                        method.Locations.FirstOrDefault(),
                        resolverMethodName);
                    continue;
                }

                sb.AppendLine($"            {destParam}.{destProp.Name} = {resolverCall};");
                continue;
            }

            // Check if this property has a [ForgeWith] mapping for nested object forging
            if (forgeWithMappings.TryGetValue(destProp.Name, out var forgingMethodName))
            {
                string? forgeWithSourcePropName = null;
                if (propertyMappings.TryGetValue(destProp.Name, out var mappedSource))
                {
                    forgeWithSourcePropName = mappedSource;
                }
                else
                {
                    var matchingSourceProp = sourceProperties.FirstOrDefault(sp =>
                        string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));
                    if (matchingSourceProp != null)
                        forgeWithSourcePropName = matchingSourceProp.Name;
                }

                if (forgeWithSourcePropName != null)
                {
                    var sourcePropertyType = ResolvePathLeafType(forgeWithSourcePropName, sourceNamedType);
                    if (sourcePropertyType != null)
                    {
                        var nestedForgeMethod = FindForgingMethod(forger.Symbol, forgingMethodName, sourcePropertyType, destProp.Type);
                        if (nestedForgeMethod != null)
                        {
                            var (sourceExpr, _) = GenerateSourceExpressionWithNullInfo(sourceParam, forgeWithSourcePropName, sourceNamedType);
                            if (sourcePropertyType.IsReferenceType)
                            {
                                var localVarName = $"__forgeWith_{destProp.Name}";
                                sb.AppendLine($"            if ({sourceExpr} is {{ }} {localVarName})");
                                sb.AppendLine($"                {destParam}.{destProp.Name} = {forgingMethodName}({localVarName});");
                                sb.AppendLine($"            else");
                                var nullAssign = destProp.Type.IsValueType ? "default" : "null!";
                                sb.AppendLine($"                {destParam}.{destProp.Name} = {nullAssign};");
                            }
                            else
                            {
                                sb.AppendLine($"            {destParam}.{destProp.Name} = {forgingMethodName}({sourceExpr});");
                            }
                            continue;
                        }
                    }
                }

                ReportDiagnosticIfNotSuppressed(context,
                    DiagnosticDescriptors.ResolverMethodNotFound,
                    method.Locations.FirstOrDefault(),
                    forgingMethodName);
                continue;
            }

            // Check if this property has a mapping from [ForgeProperty]
            if (propertyMappings.TryGetValue(destProp.Name, out var sourcePropName))
            {
                var (sourceExpr, hasNullConditional) = GenerateSourceExpressionWithNullInfo(sourceParam, sourcePropName, sourceNamedType);
                var sourceLeafType = ResolvePathLeafType(sourcePropName, sourceNamedType);
                // When null-conditional lifts a non-nullable value type to Nullable<T>, cast back
                var isLiftedValueType = hasNullConditional && sourceLeafType != null && sourceLeafType.IsValueType && GetNullableUnderlyingType(sourceLeafType) == null;
                // Try compatible enum cast first — pass isLifted so it generates correct nullable handling
                if (sourceLeafType != null)
                {
                    var enumCast = TryGenerateCompatibleEnumCast(sourceLeafType, destProp.Type, sourceExpr, isLifted: isLiftedValueType);
                    if (enumCast != null)
                    {
                        sb.AppendLine($"            {destParam}.{destProp.Name} = {enumCast};");
                        continue;
                    }
                }
                if (isLiftedValueType && destProp.Type.IsValueType && GetNullableUnderlyingType(destProp.Type) == null)
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = ({destProp.Type.ToDisplayString()})({sourceExpr})!;");
                }
                else
                {
                    // Add null-forgiving operator if we used null-conditional and dest is non-nullable
                    var nullForgiving = hasNullConditional && destProp.Type.NullableAnnotation != NullableAnnotation.Annotated ? "!" : "";
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {sourceExpr}{nullForgiving};");
                }
                continue;
            }

            var sourceProp = sourceProperties.FirstOrDefault(sp =>
                string.Equals(sp.Name, destProp.Name, _config.PropertyNameComparison));

            if (sourceProp != null && CanAssign(sourceProp.Type, destProp.Type))
            {
                // Handle Nullable<T> to T conversion using explicit cast which throws if null
                if (IsNullableToNonNullableValueType(sourceProp.Type, destProp.Type))
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = ({destProp.Type.ToDisplayString()}){sourceParam}.{sourceProp.Name}!;");
                }
                else
                {
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {sourceParam}.{sourceProp.Name};");
                }
            }
            else if (sourceProp != null)
            {
                // Compatible enum cast: EnumA -> EnumB (different namespaces, same members)
                var enumCastExpr = TryGenerateCompatibleEnumCast(sourceProp.Type, destProp.Type, $"{sourceParam}.{sourceProp.Name}");
                if (enumCastExpr != null)
                    sb.AppendLine($"            {destParam}.{destProp.Name} = {enumCastExpr};");
            }
        }

        // [AfterForge] callbacks
        if (afterForgeHooks.Count > 0)
            sb.AppendLine();
        foreach (var hookName in afterForgeHooks)
        {
            sb.AppendLine($"            {hookName}({sourceParam}, {destParam});");
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a collection forging method (List&lt;T&gt;, T[], IEnumerable&lt;T&gt;, etc.).
    /// </summary>
    private string GenerateCollectionForgeMethod(
        IMethodSymbol method,
        ITypeSymbol sourceCollectionType,
        ITypeSymbol destCollectionType,
        ITypeSymbol sourceElementType,
        ITypeSymbol destElementType,
        ForgerInfo forger,
        SourceProductionContext context)
    {
        // Validate that an element forging method exists
        var elementForgeMethod = FindForgingMethod(forger.Symbol, method.Name, sourceElementType, destElementType);
        if (elementForgeMethod == null)
        {
            ReportDiagnosticIfNotSuppressed(context,
                DiagnosticDescriptors.ResolverMethodNotFound,
                method.Locations.FirstOrDefault(),
                $"{method.Name}({sourceElementType.ToDisplayString()})");
            return string.Empty;
        }

        var sb = new StringBuilder();
        var sourceParam = method.Parameters[0].Name;

        var accessibility = GetAccessibilityKeyword(method.DeclaredAccessibility);
        var destTypeDisplay = destCollectionType.ToDisplayString();
        var sourceTypeDisplay = sourceCollectionType.ToDisplayString();

        sb.AppendLine($"        {accessibility} partial {destTypeDisplay} {method.Name}({sourceTypeDisplay} {sourceParam})");
        sb.AppendLine("        {");
        sb.AppendLine(GenerateNullCheck(sourceParam, "null!"));
        sb.AppendLine();

        // Determine the destination collection kind
        if (destCollectionType is IArrayTypeSymbol)
        {
            // T[] target — arrays need count up front; safe because array sources have .Length
            // and list sources have .Count; IEnumerable sources use .Count() (LINQ)
            var destElemDisplay = destElementType.ToDisplayString();
            var lengthExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
            sb.AppendLine($"            var result = new {destElemDisplay}[{lengthExpr}];");
            sb.AppendLine($"            var i = 0;");
            sb.AppendLine($"            foreach (var item in {sourceParam})");
            sb.AppendLine("            {");
            sb.AppendLine($"                result[i++] = {method.Name}(item);");
            sb.AppendLine("            }");
            sb.AppendLine("            return result;");
        }
        else if (destCollectionType is INamedTypeSymbol destNamedType)
        {
            var originalDef = destNamedType.OriginalDefinition.ToDisplayString();

            if (originalDef == "System.Collections.Generic.IEnumerable<T>")
            {
                // IEnumerable<T> - lazy Select projection
                sb.AppendLine($"            return {sourceParam}.Select(item => {method.Name}(item));");
            }
            else
            {
                // List<T>, IList<T>, ICollection<T>, IReadOnlyList<T>, IReadOnlyCollection<T> -> return List<T>
                var destElemDisplay = destElementType.ToDisplayString();

                // Avoid double-enumeration: only pre-size when source has a cheap Count property.
                // For IEnumerable<T> sources, allocate without capacity to avoid calling Count().
                if (HasCheapCount(sourceCollectionType))
                {
                    var countExpr = GetCollectionLengthExpression(sourceCollectionType, sourceParam);
                    sb.AppendLine($"            var result = new global::System.Collections.Generic.List<{destElemDisplay}>({countExpr});");
                }
                else
                {
                    sb.AppendLine($"            var result = new global::System.Collections.Generic.List<{destElemDisplay}>();");
                }

                sb.AppendLine($"            foreach (var item in {sourceParam})");
                sb.AppendLine("            {");
                sb.AppendLine($"                result.Add({method.Name}(item));");
                sb.AppendLine("            }");
                sb.AppendLine("            return result;");
            }
        }

        sb.AppendLine("        }");

        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the collection type has a cheap .Count or .Length property (not LINQ's Count() extension).
    /// </summary>
    private static bool HasCheapCount(ITypeSymbol sourceType)
    {
        if (sourceType is IArrayTypeSymbol)
            return true;

        if (sourceType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();
            return originalDef == "System.Collections.Generic.List<T>" ||
                   originalDef == "System.Collections.Generic.ICollection<T>" ||
                   originalDef == "System.Collections.Generic.IReadOnlyCollection<T>" ||
                   originalDef == "System.Collections.Generic.IList<T>" ||
                   originalDef == "System.Collections.Generic.IReadOnlyList<T>";
        }

        return false;
    }

    /// <summary>
    /// Gets the appropriate length/count expression for a collection source type.
    /// </summary>
    private static string GetCollectionLengthExpression(ITypeSymbol sourceType, string sourceParam)
    {
        if (sourceType is IArrayTypeSymbol)
            return $"{sourceParam}.Length";

        if (sourceType is INamedTypeSymbol namedType && namedType.IsGenericType)
        {
            var originalDef = namedType.OriginalDefinition.ToDisplayString();
            // Types with Count property
            if (originalDef == "System.Collections.Generic.List<T>" ||
                originalDef == "System.Collections.Generic.ICollection<T>" ||
                originalDef == "System.Collections.Generic.IReadOnlyCollection<T>" ||
                originalDef == "System.Collections.Generic.IList<T>" ||
                originalDef == "System.Collections.Generic.IReadOnlyList<T>")
            {
                return $"{sourceParam}.Count";
            }
        }

        // Fallback to LINQ Count()
        return $"{sourceParam}.Count()";
    }

    /// <summary>
    /// Gets the element type if the given type is a supported collection type.
    /// Returns null if the type is not a collection.
    /// </summary>
    private ITypeSymbol? GetCollectionElementType(ITypeSymbol type)
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
            var supportedCollections = new[]
            {
                "System.Collections.Generic.List<T>",
                "System.Collections.Generic.IList<T>",
                "System.Collections.Generic.ICollection<T>",
                "System.Collections.Generic.IEnumerable<T>",
                "System.Collections.Generic.IReadOnlyList<T>",
                "System.Collections.Generic.IReadOnlyCollection<T>"
            };

            if (supportedCollections.Contains(originalDef))
            {
                return namedType.TypeArguments[0];
            }
        }

        return null;
    }

    private static IEnumerable<IPropertySymbol> GetMappableProperties(INamedTypeSymbol? type)
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

    private static bool CanAssign(ITypeSymbol source, ITypeSymbol dest)
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
    private static bool AreCompatibleEnums(ITypeSymbol source, ITypeSymbol dest)
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
    private static bool IsCompatibleEnumPair(ITypeSymbol source, ITypeSymbol dest)
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
    private static string? TryGenerateCompatibleEnumCast(ITypeSymbol sourceType, ITypeSymbol destType, string sourceExpr, bool isLifted = false)
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
    private static ITypeSymbol? GetNullableUnderlyingType(ITypeSymbol type)
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
    private static bool IsNullableToNonNullableValueType(ITypeSymbol source, ITypeSymbol dest)
    {
        var sourceUnderlying = GetNullableUnderlyingType(source);
        if (sourceUnderlying == null)
            return false;

        var destUnderlying = GetNullableUnderlyingType(dest);
        if (destUnderlying != null)
            return false;

        return SymbolEqualityComparer.Default.Equals(sourceUnderlying, dest);
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
}
