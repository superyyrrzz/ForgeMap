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
        var serviceCollectionSymbol = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");
        if (serviceCollectionSymbol == null)
            return;

        var serviceLifetimeSymbol = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.ServiceLifetime");
        if (serviceLifetimeSymbol == null)
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
        sb.AppendLine("        /// Registers all classes marked with [ForgeMap] with the specified lifetime.");
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

            var fullyQualifiedName = $"global::{forger.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted))}";

            // Prefer explicit factory for IServiceProvider or IServiceScopeFactory single-param ctors;
            // otherwise register by implementation type and let the container resolve the constructor
            var needsFactory = false;
            string? factoryExpr = null;

            if (iServiceProviderSymbol != null && HasCtorWithSingleParam(forger.Symbol, iServiceProviderSymbol))
            {
                needsFactory = true;
                factoryExpr = $"sp => new {fullyQualifiedName}(sp)";
            }
            else if (iServiceScopeFactorySymbol != null && HasCtorWithSingleParam(forger.Symbol, iServiceScopeFactorySymbol))
            {
                needsFactory = true;
                factoryExpr = $"sp => new {fullyQualifiedName}(sp.GetRequiredService<global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>())";
            }

            if (needsFactory)
            {
                sb.AppendLine($"            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({fullyQualifiedName}), {factoryExpr}, lifetime));");
            }
            else
            {
                sb.AppendLine($"            services.Add(new global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor(typeof({fullyQualifiedName}), typeof({fullyQualifiedName}), lifetime));");
            }
        }

        sb.AppendLine();
        sb.AppendLine("            return services;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        context.AddSource("ForgeMapServiceCollectionExtensions.g.cs", sb.ToString());
    }

    private static bool HasCtorWithSingleParam(INamedTypeSymbol type, ITypeSymbol paramType)
    {
        return type.Constructors.Any(c =>
            !c.IsStatic &&
            c.DeclaredAccessibility == Accessibility.Public &&
            c.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, paramType));
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
