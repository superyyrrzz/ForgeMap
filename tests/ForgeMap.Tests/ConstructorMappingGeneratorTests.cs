using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ConstructorMappingGeneratorTests
{
    [Fact]
    public void Generator_RecordType_GeneratesConstructorCall()
    {
        var source = """
            using ForgeMap;
            using System;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                }

                public record DestRecord(string Id, string Name);

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DestRecord Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("new TestNamespace.DestRecord(", generatedCode);
        Assert.Contains("Id:", generatedCode);
        Assert.Contains("Name:", generatedCode);
    }

    [Fact]
    public void Generator_HybridType_GeneratesCtorPlusSetters()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public decimal Total { get; set; }
                }

                public class HybridDest
                {
                    public HybridDest(int id) { Id = id; }
                    public int Id { get; }
                    public string Name { get; set; }
                    public decimal Total { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial HybridDest Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("new TestNamespace.HybridDest(", generatedCode);
        Assert.Contains("id:", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("Total = source.Total,", generatedCode);
    }

    [Fact]
    public void Generator_ConstructorParameterNotMatched_ReportsError()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                }

                public class DestWithCtor
                {
                    public DestWithCtor(int id, string unmatchedParam) { }
                    public int Id { get; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DestWithCtor Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var error = diagnostics.FirstOrDefault(d => d.Id == "FM0014");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
