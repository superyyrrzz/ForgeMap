using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class HookGeneratorTests
{
    [Fact]
    public void Generator_BeforeForge_GeneratesCallBeforeMapping()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(Validate))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Validate(SourceEntity source) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Validate() should appear before property mapping
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(validateIndex < idAssignIndex, "BeforeForge should be called before property mapping");
    }

    [Fact]
    public void Generator_AfterForge_GeneratesCallAfterMapping()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [AfterForge(nameof(Enrich))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Enrich() should appear after property mapping
        var enrichIndex = generatedCode.IndexOf("Enrich(source, result)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex > idAssignIndex, "AfterForge should be called after property mapping");
    }

    [Fact]
    public void Generator_BeforeAndAfterForge_GeneratesCorrectOrder()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(Validate))]
                    [AfterForge(nameof(Enrich))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Validate(SourceEntity source) { }
                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        var nullCheckIndex = generatedCode.IndexOf("source == null", StringComparison.Ordinal);
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        var enrichIndex = generatedCode.IndexOf("Enrich(source, result)", StringComparison.Ordinal);
        var returnIndex = generatedCode.IndexOf("return result;", StringComparison.Ordinal);

        // Verify all elements are present in generated code
        Assert.True(nullCheckIndex >= 0, "Null check should be in generated code");
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");
        Assert.True(returnIndex >= 0, "Return statement should be in generated code");

        // Verify execution order: null check → BeforeForge → mapping → AfterForge → return
        Assert.True(nullCheckIndex < validateIndex, "Null check before BeforeForge");
        Assert.True(validateIndex < idAssignIndex, "BeforeForge before mapping");
        Assert.True(idAssignIndex < enrichIndex, "Mapping before AfterForge");
        Assert.True(enrichIndex < returnIndex, "AfterForge before return");
    }

    [Fact]
    public void Generator_HookMethodNotFound_ReportsFM0016()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge("NonExistentMethod")]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var hookError = diagnostics.FirstOrDefault(d => d.Id == "FM0016");
        Assert.NotNull(hookError);
        Assert.Equal(DiagnosticSeverity.Error, hookError.Severity);
    }

    [Fact]
    public void Generator_AfterForge_InvalidSignature_ReportsFM0043()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [AfterForge(nameof(BadHook))]
                    public partial DestDto Forge(SourceEntity source);

                    // Wrong signature: AfterForge needs (source, dest), not just (source)
                    private static void BadHook(SourceEntity source) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var hookError = diagnostics.FirstOrDefault(d => d.Id == "FM0043");
        Assert.NotNull(hookError);
        Assert.Equal(DiagnosticSeverity.Error, hookError.Severity);
    }

    [Fact]
    public void Generator_ForgeInto_WithHooks_GeneratesCorrectCode()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(Validate))]
                    [AfterForge(nameof(Enrich))]
                    public partial void ForgeInto(SourceEntity source, [UseExistingValue] DestDto destination);

                    private static void Validate(SourceEntity source) { }
                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Verify hooks are present
        Assert.Contains("Validate(source)", generatedCode);
        Assert.Contains("Enrich(source, destination)", generatedCode);

        // Verify order: null checks → BeforeForge → mapping → AfterForge
        var nullCheckIndex = generatedCode.IndexOf("source == null", StringComparison.Ordinal);
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("destination.Id = source.Id", StringComparison.Ordinal);
        var enrichIndex = generatedCode.IndexOf("Enrich(source, destination)", StringComparison.Ordinal);

        Assert.True(nullCheckIndex >= 0, "Null check should be in generated code");
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");

        Assert.True(nullCheckIndex < validateIndex);
        Assert.True(validateIndex < idAssignIndex);
        Assert.True(idAssignIndex < enrichIndex);
    }

    [Fact]
    public void Generator_HooksOnEnumForge_ReportsFM0018()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public enum SourceEnum { A, B }
                public enum DestEnum { A, B }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(Validate))]
                    public partial DestEnum Forge(SourceEnum source);

                    private static void Validate(SourceEnum source) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0018");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Generator_HooksOnCollectionForge_ReportsFM0018()
    {
        var source = """
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class SourceItem
                {
                    public int Id { get; set; }
                }

                public class DestItem
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(LogList))]
                    public partial List<DestItem> Forge(List<SourceItem> source);

                    public partial DestItem Forge(SourceItem source);

                    private static void LogList(List<SourceItem> source) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0018");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Generator_AfterForgeOnCollectionForge_ReportsFM0045()
    {
        var source = """
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class SourceItem
                {
                    public int Id { get; set; }
                }

                public class DestItem
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [AfterForge(nameof(LogList))]
                    public partial List<DestItem> Forge(List<SourceItem> source);

                    public partial DestItem Forge(SourceItem source);

                    private static void LogList(List<SourceItem> source, List<DestItem> dest) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var error = diagnostics.FirstOrDefault(d => d.Id == "FM0045");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
