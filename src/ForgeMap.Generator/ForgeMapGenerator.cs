using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeMap.Generator;

/// <summary>
/// Incremental source generator for ForgeMap that generates type transformation code at compile time.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ForgeMapGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Register syntax provider to find classes with [ForgeMap] attribute
        var forgerClasses = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ForgeMap.ForgeMapAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (context, _) => GetForgerInfo(context))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Combine with compilation
        var compilationAndForgers = context.CompilationProvider.Combine(forgerClasses.Collect());

        // Generate source
        context.RegisterSourceOutput(compilationAndForgers, static (spc, source) =>
        {
            var (compilation, forgers) = source;
            Execute(compilation, forgers, spc);
        });
    }

    private static ForgerInfo? GetForgerInfo(GeneratorAttributeSyntaxContext context)
    {
        if (context.TargetNode is not ClassDeclarationSyntax classDeclaration)
            return null;

        if (context.TargetSymbol is not INamedTypeSymbol classSymbol)
            return null;

        // Check if the class is partial
        var isPartial = classDeclaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword));

        return new ForgerInfo(
            classSymbol,
            classDeclaration,
            isPartial,
            context.Attributes[0]);
    }

    private static void Execute(
        Compilation compilation,
        ImmutableArray<ForgerInfo> forgers,
        SourceProductionContext context)
    {
        if (forgers.IsDefaultOrEmpty)
            return;

        // Read assembly-level [ForgeMapDefaults] attribute if present
        var assemblyDefaults = ReadAssemblyDefaults(compilation);
        var emitter = new ForgeCodeEmitter(compilation, assemblyDefaults);
        var validForgers = new List<ForgerInfo>();

        foreach (var forger in forgers)
        {
            // Report diagnostic if class is not partial
            if (!forger.IsPartial)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.ClassMustBePartial,
                    forger.ClassDeclaration.Identifier.GetLocation(),
                    forger.Symbol.Name));
                continue;
            }

            validForgers.Add(forger);

            // Find all partial methods that need implementation
            var generatedSource = emitter.GenerateForger(forger, context);
            if (!string.IsNullOrEmpty(generatedSource))
            {
                var hintName = $"{forger.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)).Replace(".", "_").Replace("<", "_").Replace(">", "_").Replace(",", "_").Replace(" ", "")}.g.cs";
                context.AddSource(hintName, generatedSource);
            }
        }

        // Generate DI extension method if IServiceCollection is available
        GenerateDiExtensions(compilation, validForgers, context);
    }

    private static ForgerConfig ReadAssemblyDefaults(Compilation compilation)
    {
        var config = new ForgerConfig();

        var defaultsAttrSymbol = compilation.GetTypeByMetadataName("ForgeMap.ForgeMapDefaultsAttribute");
        if (defaultsAttrSymbol == null)
            return config;

        var assemblyAttr = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, defaultsAttrSymbol));

        if (assemblyAttr == null)
            return config;

        foreach (var named in assemblyAttr.NamedArguments)
        {
            switch (named.Key)
            {
                case "NullHandling":
                    config.NullHandling = (int)named.Value.Value!;
                    break;
                case "PropertyMatching":
                    config.PropertyMatching = (int)named.Value.Value!;
                    break;
                case "GenerateCollectionMappings":
                    config.GenerateCollectionMappings = (bool)named.Value.Value!;
                    break;
                case "NullPropertyHandling":
                    config.NullPropertyHandling = (int)named.Value.Value!;
                    break;
                case "AutoWireNestedMappings":
                    config.AutoWireNestedMappings = (bool)named.Value.Value!;
                    break;
                case "StringToEnum":
                    config.StringToEnum = (int)named.Value.Value!;
                    break;
            }
        }

        return config;
    }

    private static void GenerateDiExtensions(
        Compilation compilation,
        List<ForgerInfo> forgers,
        SourceProductionContext context)
    {
        // Only generate if the user references Microsoft.Extensions.DependencyInjection.Abstractions
        if (compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.IServiceCollection") == null)
            return;

        if (compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ServiceLifetime") == null)
            return;

        if (compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions") == null)
            return;

        var iServiceProviderSymbol = compilation.GetTypeByMetadataName("System.IServiceProvider");
        var iServiceScopeFactorySymbol = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceScopeFactory");

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated />");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection");
        sb.AppendLine("{");
        sb.AppendLine("    public static class ForgeMapServiceCollectionExtensions");
        sb.AppendLine("    {");
        sb.AppendLine("        /// <summary>");
        sb.AppendLine("        /// Registers concrete, non-generic, top-level [ForgeMap] classes with the specified lifetime.");
        sb.AppendLine("        /// </summary>");
        sb.AppendLine("        public static global::Microsoft.Extensions.DependencyInjection.IServiceCollection AddForgeMaps(");
        sb.AppendLine("            this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,");
        sb.AppendLine("            global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)");
        sb.AppendLine("        {");
        sb.AppendLine("            if (services == null) throw new global::System.ArgumentNullException(nameof(services));");
        sb.AppendLine();
        foreach (var forger in forgers)
        {
            if (forger.Symbol.IsAbstract || forger.Symbol.IsGenericType)
                continue;

            // Only register public or internal top-level types
            if (forger.Symbol.DeclaredAccessibility != Accessibility.Public &&
                forger.Symbol.DeclaredAccessibility != Accessibility.Internal)
                continue;
            if (forger.Symbol.ContainingType != null)
                continue;

            var fullyQualifiedName = $"global::{forger.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))}";

            // Use explicit factory only when the sole public ctor takes IServiceProvider or IServiceScopeFactory;
            // otherwise register by implementation type and let the container resolve the constructor
            var needsFactory = false;
            string? factoryExpr = null;
            var publicCtors = forger.Symbol.Constructors.Where(c => !c.IsStatic && c.DeclaredAccessibility == Accessibility.Public).ToList();

            // Skip types with no public constructors — DI container cannot activate them
            if (publicCtors.Count == 0)
                continue;

            if (publicCtors.Count == 1 && publicCtors[0].Parameters.Length == 1)
            {
                var paramType = publicCtors[0].Parameters[0].Type;
                if (iServiceProviderSymbol != null && SymbolEqualityComparer.Default.Equals(paramType, iServiceProviderSymbol))
                {
                    needsFactory = true;
                    factoryExpr = $"sp => new {fullyQualifiedName}(sp)";
                }
                else if (iServiceScopeFactorySymbol != null && SymbolEqualityComparer.Default.Equals(paramType, iServiceScopeFactorySymbol))
                {
                    needsFactory = true;
                    factoryExpr = $"sp => new {fullyQualifiedName}(sp.GetRequiredService<global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>())";
                }
            }

            if (needsFactory)
            {
                sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({fullyQualifiedName}), {factoryExpr}, lifetime));");
            }
            else
            {
                sb.AppendLine($"            global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAdd(services, new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({fullyQualifiedName}), typeof({fullyQualifiedName}), lifetime));");
            }
        }

        sb.AppendLine();
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ForgeMapServiceCollectionExtensions.g.cs", sb.ToString());
    }

}

internal sealed class ForgerInfo
{
    public ForgerInfo(
        INamedTypeSymbol symbol,
        ClassDeclarationSyntax classDeclaration,
        bool isPartial,
        AttributeData forgeMapAttribute)
    {
        Symbol = symbol;
        ClassDeclaration = classDeclaration;
        IsPartial = isPartial;
        ForgeMapAttribute = forgeMapAttribute;
    }

    public INamedTypeSymbol Symbol { get; }
    public ClassDeclarationSyntax ClassDeclaration { get; }
    public bool IsPartial { get; }
    public AttributeData ForgeMapAttribute { get; }
}
