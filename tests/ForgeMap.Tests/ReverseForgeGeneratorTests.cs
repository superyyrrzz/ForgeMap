using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ReverseForgeGeneratorTests
{
    [Fact]
    public void Generator_ReverseForge_GeneratesBothDirections()
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
                    [ReverseForge]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Forward method (partial implementation)
        Assert.Contains("partial TestNamespace.DestDto Forge(TestNamespace.SourceEntity source)", generatedCode);
        // Reverse method (non-partial, auto-generated)
        Assert.Contains("TestNamespace.SourceEntity Forge(TestNamespace.DestDto source)", generatedCode);
    }

    [Fact]
    public void Generator_ReverseForge_WithForgeProperty_SwapsMapping()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BookEntity
                {
                    public int Id { get; set; }
                    public string BookTitle { get; set; }
                }

                public class BookDto
                {
                    public int Id { get; set; }
                    public string DisplayTitle { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ReverseForge]
                    [ForgeProperty(nameof(BookEntity.BookTitle), nameof(BookDto.DisplayTitle))]
                    public partial BookDto Forge(BookEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Forward: DisplayTitle = source.BookTitle
        Assert.Contains("DisplayTitle = source.BookTitle,", generatedCode);
        // Reverse: BookTitle = source.DisplayTitle
        Assert.Contains("BookTitle = source.DisplayTitle,", generatedCode);
    }

    [Fact]
    public void Generator_ReverseForge_WithForgeFrom_EmitsWarning()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public decimal Subtotal { get; set; }
                    public decimal TaxRate { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public decimal TotalWithTax { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ReverseForge]
                    [ForgeFrom(nameof(DestDto.TotalWithTax), nameof(CalculateTotal))]
                    public partial DestDto Forge(SourceEntity source);

                    private static decimal CalculateTotal(SourceEntity source)
                        => source.Subtotal * (1 + source.TaxRate);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        // Should emit FM0012 warning for ForgeFrom
        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0012");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Generator_ReverseForge_ExplicitReverseTakesPrecedence()
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
                    [ReverseForge]
                    public partial DestDto Forge(SourceEntity source);

                    // Explicit reverse - should take precedence
                    public partial SourceEntity Forge(DestDto source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Should have both methods, but only one reverse implementation (the explicit one)
        // Count occurrences of reverse method signature (partial, since it's explicitly declared)
        var reverseMethodCount = generatedCode.Split(new[] { "partial TestNamespace.SourceEntity Forge(TestNamespace.DestDto source)" }, System.StringSplitOptions.None).Length - 1;
        Assert.Equal(1, reverseMethodCount);

        // Ensure no auto-generated (non-partial) reverse method was emitted
        // Remove all partial occurrences and check that the non-partial signature doesn't appear
        var withoutPartial = generatedCode.Replace("partial TestNamespace.SourceEntity Forge(TestNamespace.DestDto source)", "");
        Assert.DoesNotContain("TestNamespace.SourceEntity Forge(TestNamespace.DestDto source)", withoutPartial);
    }

    [Fact]
    public void Generator_ReverseForge_WithForgeWith_LacksReverseForge_EmitsWarning()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class AddressEntity { public string Street { get; set; } }
                public class AddressDto { public string Street { get; set; } }

                public class UserEntity
                {
                    public int Id { get; set; }
                    public AddressEntity Address { get; set; }
                }

                public class UserDto
                {
                    public int Id { get; set; }
                    public AddressDto Address { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    // Nested method does NOT have [ReverseForge]
                    public partial AddressDto Forge(AddressEntity source);

                    [ReverseForge]
                    [ForgeWith(nameof(UserDto.Address), nameof(Forge))]
                    public partial UserDto Forge(UserEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        // Should emit FM0015 warning
        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0015");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
