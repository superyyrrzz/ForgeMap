using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ForgePropertyGeneratorTests
{
    [Fact]
    public void Generator_ForgeProperty_GeneratesCorrectMapping()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public string OrderId { get; set; }
                    public decimal SubTotal { get; set; }
                }

                public class DestDto
                {
                    public string Id { get; set; }
                    public decimal Amount { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty(nameof(SourceEntity.OrderId), nameof(DestDto.Id))]
                    [ForgeProperty(nameof(SourceEntity.SubTotal), nameof(DestDto.Amount))]
                    public partial DestDto Forge(SourceEntity source);
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
        Assert.Contains("Amount = source.SubTotal,", generatedCode);
    }

    [Fact]
    public void Generator_ForgeProperty_NestedPath_GeneratesNullConditional()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class CustomerInfo
                {
                    public string Name { get; set; }
                }

                public class OrderEntity
                {
                    public int Id { get; set; }
                    public CustomerInfo Customer { get; set; }
                }

                public class OrderDto
                {
                    public int Id { get; set; }
                    public string CustomerName { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty("Customer.Name", nameof(OrderDto.CustomerName))]
                    public partial OrderDto Forge(OrderEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("CustomerName = source.Customer?.Name!", generatedCode);
        Assert.Contains("Id = source.Id,", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
