using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

/// <summary>
/// Tests for NullPropertyHandling (v1.2): nullable ref → non-nullable ref property assignment strategies.
/// </summary>
public class NullPropertyHandlingTests
{
    [Fact]
    public void NullForgiving_Default_AppendsExclamation()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // Default NullForgiving = append !
        Assert.Contains("Name = source.Name!", generatedCode);
    }

    [Fact]
    public void NullForgiving_Explicit_AppendsExclamation()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.NullForgiving)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Name = source.Name!", generatedCode);
    }

    [Fact]
    public void SkipNull_GeneratesIfGuard()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // SkipNull: should use var result pattern with if guard
        Assert.Contains("var result = new", generatedCode);
        Assert.Contains("is { }", generatedCode);
        Assert.Contains("result.Name =", generatedCode);
    }

    [Fact]
    public void CoalesceToDefault_String_GeneratesEmptyString()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains(@"source.Name ?? """"", generatedCode);
    }

    [Fact]
    public void CoalesceToDefault_List_GeneratesNewList()
    {
        var source = """
            #nullable enable
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class Source
                {
                    public List<string>? Tags { get; set; }
                }

                public class Dest
                {
                    public List<string> Tags { get; set; } = new();
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("source.Tags ?? new", generatedCode);
    }

    [Fact]
    public void CoalesceToDefault_Array_GeneratesArrayEmpty()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string[]? Items { get; set; }
                }

                public class Dest
                {
                    public string[] Items { get; set; } = System.Array.Empty<string>();
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("source.Items ?? global::System.Array.Empty<string>()", generatedCode);
    }

    [Fact]
    public void ThrowException_GeneratesNullCoalesceThrow()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.ThrowException)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("?? throw new global::System.ArgumentNullException", generatedCode);
    }

    [Fact]
    public void PerProperty_Override_RespectsAttributeConfig()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? FirstName { get; set; }
                    public string? LastName { get; set; }
                }

                public class Dest
                {
                    public string FirstName { get; set; } = "";
                    public string LastName { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.NullForgiving)]
                public partial class TestForger
                {
                    [ForgeProperty("FirstName", "FirstName", NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // FirstName should use CoalesceToDefault (per-property override)
        Assert.Contains(@"source.FirstName ?? """"", generatedCode);
        // LastName should use NullForgiving (forger-level default)
        Assert.Contains("LastName = source.LastName!", generatedCode);
    }

    [Fact]
    public void AssemblyLevel_Default_IsRespected()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            [assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.ThrowException)]

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("?? throw new global::System.ArgumentNullException", generatedCode);
    }

    [Fact]
    public void ForgerLevel_Override_TakesPrecedenceOverAssembly()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            [assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.ThrowException)]

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // Forger-level CoalesceToDefault should override assembly-level ThrowException
        Assert.Contains(@"source.Name ?? """"", generatedCode);
        Assert.DoesNotContain("?? throw", generatedCode);
    }

    [Fact]
    public void FM0007_ReportedForNullableRefToNonNullableRef()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // FM0007 should be reported for nullable-ref to non-nullable-ref mappings
        var fm0007 = diagnostics.Where(d => d.Id == "FM0007").ToList();
        Assert.NotEmpty(fm0007);
    }

    [Fact]
    public void FM0007_NotReportedForForgeFromResolverProperty()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeFrom(nameof(Dest.Name), nameof(ResolveName))]
                    public partial Dest Forge(Source source);

                    private static string ResolveName(Source s) => s.Name ?? "default";
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        // FM0007 should NOT be reported for resolver properties
        var fm0007 = diagnostics.Where(d => d.Id == "FM0007").ToList();
        Assert.Empty(fm0007);
    }

    [Fact]
    public void ForgeInto_NullForgiving_AppendsExclamation()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial void ForgeInto(Source source, [UseExistingValue] Dest dest);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("dest.Name = source.Name!;", generatedCode);
    }

    [Fact]
    public void ForgeInto_SkipNull_GeneratesIfGuard()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
                public partial class TestForger
                {
                    public partial void ForgeInto(Source source, [UseExistingValue] Dest dest);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("is { }", generatedCode);
        Assert.Contains("dest.Name =", generatedCode);
    }

    [Fact]
    public void ForgeInto_CoalesceToDefault_GeneratesCoalesce()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    public partial void ForgeInto(Source source, [UseExistingValue] Dest dest);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains(@"dest.Name = source.Name ?? """";", generatedCode);
    }

    [Fact]
    public void ForgeInto_ThrowException_GeneratesThrow()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.ThrowException)]
                public partial class TestForger
                {
                    public partial void ForgeInto(Source source, [UseExistingValue] Dest dest);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("?? throw new global::System.ArgumentNullException", generatedCode);
    }

    [Fact]
    public void NonNullableToNonNullable_NoNullPropertyHandlingApplied()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string Name { get; set; } = "";
                }

                public class Dest
                {
                    public string Name { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.ThrowException)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // Non-nullable to non-nullable should NOT trigger NullPropertyHandling
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.DoesNotContain("?? throw", generatedCode);
        Assert.DoesNotContain("source.Name!", generatedCode);
    }

    [Fact]
    public void ConstructorMapping_SkipNull_FallsBackToNullForgiving()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? Name { get; set; }
                }

                public class Dest
                {
                    public string Name { get; }
                    public Dest(string name) { Name = name; }
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // Constructor params can't be skipped, so should fall back to NullForgiving
        Assert.Contains("source.Name!", generatedCode);
    }

    [Fact]
    public void ForgeProperty_ExplicitMapping_NullPropertyHandling()
    {
        var source = """
            #nullable enable
            using ForgeMap;

            namespace TestNamespace
            {
                public class Source
                {
                    public string? FullName { get; set; }
                }

                public class Dest
                {
                    public string DisplayName { get; set; } = "";
                }

                [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
                public partial class TestForger
                {
                    [ForgeProperty("FullName", "DisplayName")]
                    public partial Dest Forge(Source source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains(@"source.FullName ?? """"", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var abstractionsAssembly = typeof(ForgeMapAttribute).Assembly;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(abstractionsAssembly.Location)
        };

        var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "System.Runtime");
        if (runtimeAssembly != null)
            references.Add(MetadataReference.CreateFromFile(runtimeAssembly.Location));

        var netstandardAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "netstandard");
        if (netstandardAssembly != null)
            references.Add(MetadataReference.CreateFromFile(netstandardAssembly.Location));

        // Add System.Collections for List<T> support
        var collectionsAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "System.Collections");
        if (collectionsAssembly != null)
            references.Add(MetadataReference.CreateFromFile(collectionsAssembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ForgeMap.Generator.ForgeMapGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Verify the output compilation has no errors (Comment 1 fix)
        var compilationErrors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.Empty(compilationErrors);

        var runResult = driver.GetRunResult();
        var generatedTrees = runResult.GeneratedTrees.ToList();

        return (diagnostics.ToList(), generatedTrees);
    }
}
