using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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

        var emitter = new ForgeCodeEmitter(compilation);

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

            // Find all partial methods that need implementation
            var generatedSource = emitter.GenerateForger(forger, context);
            if (!string.IsNullOrEmpty(generatedSource))
            {
                var hintName = $"{forger.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)).Replace(".", "_")}.g.cs";
                context.AddSource(hintName, generatedSource);
            }
        }
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
