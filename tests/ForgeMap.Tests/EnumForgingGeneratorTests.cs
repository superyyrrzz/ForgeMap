using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class EnumForgingGeneratorTests
{
    [Fact]
    public void Generator_EnumToEnum_GeneratesParseByName()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }
                public enum StatusDto { Active, Inactive }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial StatusDto Forge(Status source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Enum.Parse", generatedCode);
        Assert.Contains("source.ToString()", generatedCode);
    }

    [Fact]
    public void Generator_EnumToString_GeneratesToString()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial string Forge(Status source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("return source.ToString();", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_GeneratesEnumParse()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Status Forge(string source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Enum.Parse", generatedCode);
        Assert.Contains("source, true)", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
