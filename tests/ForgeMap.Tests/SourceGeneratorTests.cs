using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

/// <summary>
/// Tests that verify the source generator produces correct code.
/// </summary>
public class SourceGeneratorTests
{
    [Fact]
    public void Generator_SimplePropertyMapping_GeneratesCorrectCode()
    {
        // Arrange
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
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = TestHelper.RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"Generated code:\n{generatedCode}");

        Assert.Contains("partial", generatedCode);
        Assert.Contains("DestDto", generatedCode);
        Assert.Contains("Forge", generatedCode);
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("if (source == null) return null!", generatedCode);
    }

    [Fact]
    public void Generator_IgnoreAttribute_ExcludesProperties()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Secret { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Secret { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(DestDto.Secret))]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = TestHelper.RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.DoesNotContain("Secret = source.Secret", generatedCode);
    }

    [Fact]
    public void Generator_IgnoreMultipleProperties_ExcludesAllSpecified()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string PasswordHash { get; set; }
                    public string SecurityStamp { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                    public string PasswordHash { get; set; }
                    public string SecurityStamp { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(DestDto.PasswordHash), nameof(DestDto.SecurityStamp))]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = TestHelper.RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.DoesNotContain("PasswordHash = source.PasswordHash", generatedCode);
        Assert.DoesNotContain("SecurityStamp = source.SecurityStamp", generatedCode);
    }

    [Fact]
    public void Generator_NonPartialClass_ReportsError()
    {
        // Arrange
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                [ForgeMap]
                public class NonPartialForger
                {
                    public partial DestDto Forge(SourceEntity source);
                }

                public class SourceEntity { public int Id { get; set; } }
                public class DestDto { public int Id { get; set; } }
            }
            """;

        // Act
        var (diagnostics, _) = TestHelper.RunGenerator(source);

        // Assert
        var error = diagnostics.FirstOrDefault(d => d.Id == "FM0001");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void Generator_ForgeInto_GeneratesCorrectCode()
    {
        // Arrange
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
                    public partial void ForgeInto(SourceEntity source, [UseExistingValue] DestDto destination);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = TestHelper.RunGenerator(source);

        // Assert
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Debug.WriteLine($"ForgeInto generated code:\n{generatedCode}");

        Assert.Contains("partial void ForgeInto", generatedCode);
        Assert.Contains("destination.Id = source.Id;", generatedCode);
        Assert.Contains("destination.Name = source.Name;", generatedCode);
        Assert.Contains("if (destination == null) throw new global::System.ArgumentNullException", generatedCode);
        Assert.Contains("if (source == null) return;", generatedCode);
    }

}