using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ForgeFromGeneratorTests
{
    [Fact]
    public void Generator_ForgeFrom_GeneratesResolverCall()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class OrderEntity
                {
                    public decimal Subtotal { get; set; }
                    public decimal TaxRate { get; set; }
                }

                public class OrderDto
                {
                    public decimal TotalWithTax { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeFrom(nameof(OrderDto.TotalWithTax), nameof(CalculateTotal))]
                    public partial OrderDto Forge(OrderEntity source);

                    private static decimal CalculateTotal(OrderEntity source)
                        => source.Subtotal * (1 + source.TaxRate);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("TotalWithTax = CalculateTotal(source),", generatedCode);
    }

    [Fact]
    public void Generator_ForgeFrom_MissingResolver_ReportsError()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity { public int Id { get; set; } }
                public class DestDto { public int Value { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeFrom(nameof(DestDto.Value), "NonExistentMethod")]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        var error = diagnostics.FirstOrDefault(d => d.Id == "FM0008");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void Generator_ForgePropertyAndForgeFrom_WorkTogether()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class OrderEntity
                {
                    public string OrderId { get; set; }
                    public DateTime PlacedAt { get; set; }
                    public decimal Subtotal { get; set; }
                    public decimal TaxRate { get; set; }
                }

                public class OrderDto
                {
                    public string Id { get; set; }
                    public DateTime OrderDate { get; set; }
                    public decimal TotalWithTax { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty(nameof(OrderEntity.OrderId), nameof(OrderDto.Id))]
                    [ForgeProperty(nameof(OrderEntity.PlacedAt), nameof(OrderDto.OrderDate))]
                    [ForgeFrom(nameof(OrderDto.TotalWithTax), nameof(CalculateTotal))]
                    public partial OrderDto Forge(OrderEntity source);

                    private static decimal CalculateTotal(OrderEntity source)
                        => source.Subtotal * (1 + source.TaxRate);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.OrderId,", generatedCode);
        Assert.Contains("OrderDate = source.PlacedAt,", generatedCode);
        Assert.Contains("TotalWithTax = CalculateTotal(source),", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
