using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using static ForgeMap.Generator.TypeAnalysisHelper;

namespace ForgeMap.Generator;

/// <summary>
/// Emits generated forging code for ForgeMap forger classes.
/// </summary>
internal sealed partial class ForgeCodeEmitter
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
    private readonly INamedTypeSymbol? _useExistingValueAttributeSymbol;
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
        _useExistingValueAttributeSymbol = compilation.GetTypeByMetadataName("ForgeMap.UseExistingValueAttribute");
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
            NullPropertyHandling = _assemblyDefaults.NullPropertyHandling,
            AutoWireNestedMappings = _assemblyDefaults.AutoWireNestedMappings,
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
                case "NullPropertyHandling":
                    config.NullPropertyHandling = (int)named.Value.Value!;
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
                case "AutoWireNestedMappings":
                    config.AutoWireNestedMappings = (bool)named.Value.Value!;
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
        var useExistingParam = GetUseExistingValueParameter(method);

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
}
