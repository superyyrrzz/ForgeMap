using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class NullableHandlingGeneratorTests
{
    [Fact]
    public void Generator_NullableToNonNullable_GeneratesCast()
    {
        // Arrange
        var source = """
            using ForgeMap;
            using System;

            namespace TestNamespace
            {
                public class Source
                {
                    public DateTime? ShippedAt { get; set; }
                    public int? Quantity { get; set; }
                }

                public class Dest
                {
                    public DateTime ShippedAt { get; set; }
                    public int Quantity { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        // Check for cast pattern - (Type)source.Property! format
        Assert.Contains("ShippedAt =", generatedCode);
        Assert.Contains(")source.ShippedAt!", generatedCode);  // Should have cast (ending with ')') and null-forgiving operator
        Assert.Contains("Quantity =", generatedCode);
        Assert.Contains(")source.Quantity!", generatedCode);  // Should have cast (ending with ')') and null-forgiving operator
    }

    [Fact]
    public void Generator_NonNullableToNullable_GeneratesDirectAssignment()
    {
        // Arrange
        var source = """
            using ForgeMap;
            using System;

            namespace TestNamespace
            {
                public class Source
                {
                    public DateTime ShippedAt { get; set; }
                    public int Quantity { get; set; }
                }

                public class Dest
                {
                    public DateTime? ShippedAt { get; set; }
                    public int? Quantity { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest Forge(Source source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("ShippedAt = source.ShippedAt,", generatedCode);
        Assert.Contains("Quantity = source.Quantity,", generatedCode);
        // Should NOT contain cast for non-nullable to nullable
        Assert.DoesNotContain("(global::System.DateTime?)", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
