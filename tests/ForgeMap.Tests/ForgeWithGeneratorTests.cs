using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ForgeWithGeneratorTests
{
    [Fact]
    public void Generator_ForgeWith_GeneratesNestedForgeCall()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class AddressEntity
                {
                    public string Street { get; set; }
                    public string City { get; set; }
                }

                public class AddressDto
                {
                    public string Street { get; set; }
                    public string City { get; set; }
                }

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
                    public partial AddressDto Forge(AddressEntity source);

                    [ForgeWith(nameof(UserDto.Address), nameof(Forge))]
                    public partial UserDto Forge(UserEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Should contain null-guarded nested forge call using pattern matching
        Assert.Contains("Address =", generatedCode);
        Assert.Contains("source.Address is { } __forgeWith_Address", generatedCode);
        Assert.Contains("Forge(__forgeWith_Address)", generatedCode);
        Assert.Contains("Id = source.Id,", generatedCode);
    }

    [Fact]
    public void Generator_ForgeWith_MissingMethod_ReportsError()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class AddressEntity { public string Street { get; set; } }
                public class AddressDto { public string Street { get; set; } }
                public class UserEntity { public int Id { get; set; } public AddressEntity Address { get; set; } }
                public class UserDto { public int Id { get; set; } public AddressDto Address { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeWith(nameof(UserDto.Address), "NonExistentMethod")]
                    public partial UserDto Forge(UserEntity source);
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

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
