using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class CollectionForgingGeneratorTests
{
    [Fact]
    public void Generator_CollectionForge_List_GeneratesCorrectCode()
    {
        // Arrange
        var source = """
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial ItemDto Forge(ItemEntity source);
                    public partial List<ItemDto> Forge(List<ItemEntity> source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("new global::System.Collections.Generic.List<TestNamespace.ItemDto>", generatedCode);
        Assert.Contains("foreach (var item in source)", generatedCode);
        Assert.Contains("result.Add(Forge(item))", generatedCode);
        Assert.Contains("if (source == null) return null!", generatedCode);
    }

    [Fact]
    public void Generator_CollectionForge_Array_GeneratesCorrectCode()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial ItemDto Forge(ItemEntity source);
                    public partial ItemDto[] Forge(ItemEntity[] source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("new TestNamespace.ItemDto[source.Length]", generatedCode);
        Assert.Contains("result[i++] = Forge(item)", generatedCode);
    }

    [Fact]
    public void Generator_CollectionForge_IEnumerable_GeneratesLazySelect()
    {
        // Arrange
        var source = """
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial ItemDto Forge(ItemEntity source);
                    public partial IEnumerable<ItemDto> Forge(IEnumerable<ItemEntity> source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("source.Select(item => Forge(item))", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
