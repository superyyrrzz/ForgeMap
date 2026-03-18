using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TypeForge.Generator;
using Xunit;
using System.Diagnostics;

namespace TypeForge.Tests;

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
            using TypeForge;

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

                [TypeForge]
                public partial class TestForger
                {
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
            using TypeForge;

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

                [TypeForge]
                public partial class TestForger
                {
                    [Ignore(nameof(DestDto.Secret))]
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
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.DoesNotContain("Secret = source.Secret", generatedCode);
    }

    [Fact]
    public void Generator_IgnoreMultipleProperties_ExcludesAllSpecified()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
                public partial class TestForger
                {
                    [Ignore(nameof(DestDto.PasswordHash), nameof(DestDto.SecurityStamp))]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

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
            using TypeForge;

            namespace TestNamespace
            {
                [TypeForge]
                public class NonPartialForger
                {
                    public partial DestDto Forge(SourceEntity source);
                }

                public class SourceEntity { public int Id { get; set; } }
                public class DestDto { public int Id { get; set; } }
            }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        var error = diagnostics.FirstOrDefault(d => d.Id == "TF0001");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void Generator_ForgeInto_GeneratesCorrectCode()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
                public partial class TestForger
                {
                    public partial void ForgeInto(SourceEntity source, [UseExistingValue] DestDto destination);
                }
            }
            """;

        // Act
        var (diagnostics, generatedTrees) = RunGenerator(source);

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

    internal static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Get references to the TypeForge.Abstractions assembly
        var abstractionsAssembly = typeof(TypeForgeAttribute).Assembly;
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            MetadataReference.CreateFromFile(abstractionsAssembly.Location)
        };

        // Add reference to System.Runtime
        var runtimeAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "System.Runtime");
        if (runtimeAssembly != null)
        {
            references.Add(MetadataReference.CreateFromFile(runtimeAssembly.Location));
        }

        // Add reference to netstandard
        var netstandardAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "netstandard");
        if (netstandardAssembly != null)
        {
            references.Add(MetadataReference.CreateFromFile(netstandardAssembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new TypeForgeGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();
        var generatedTrees = runResult.GeneratedTrees.ToList();

        return (diagnostics.ToList(), generatedTrees);
    }
}

#region v0.2 Generator Tests

public class ForgePropertyGeneratorTests
{
    [Fact]
    public void Generator_ForgeProperty_GeneratesCorrectMapping()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
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
            using TypeForge;

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

                [TypeForge]
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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class ForgeFromGeneratorTests
{
    [Fact]
    public void Generator_ForgeFrom_GeneratesResolverCall()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
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
            using TypeForge;

            namespace TestNamespace
            {
                public class SourceEntity { public int Id { get; set; } }
                public class DestDto { public int Value { get; set; } }

                [TypeForge]
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
        var error = diagnostics.FirstOrDefault(d => d.Id == "TF0008");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    [Fact]
    public void Generator_ForgePropertyAndForgeFrom_WorkTogether()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class NullableHandlingGeneratorTests
{
    [Fact]
    public void Generator_NullableToNonNullable_GeneratesCast()
    {
        // Arrange
        var source = """
            using TypeForge;
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

                [TypeForge]
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
            using TypeForge;
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

                [TypeForge]
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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region v0.3 Generator Tests

public class ForgeWithGeneratorTests
{
    [Fact]
    public void Generator_ForgeWith_GeneratesNestedForgeCall()
    {
        // Arrange
        var source = """
            using TypeForge;

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

                [TypeForge]
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
            using TypeForge;

            namespace TestNamespace
            {
                public class AddressEntity { public string Street { get; set; } }
                public class AddressDto { public string Street { get; set; } }
                public class UserEntity { public int Id { get; set; } public AddressEntity Address { get; set; } }
                public class UserDto { public int Id { get; set; } public AddressDto Address { get; set; } }

                [TypeForge]
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
        var error = diagnostics.FirstOrDefault(d => d.Id == "TF0008");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class CollectionForgingGeneratorTests
{
    [Fact]
    public void Generator_CollectionForge_List_GeneratesCorrectCode()
    {
        // Arrange
        var source = """
            using TypeForge;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [TypeForge]
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
            using TypeForge;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [TypeForge]
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
            using TypeForge;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class ItemEntity { public int Id { get; set; } }
                public class ItemDto { public int Id { get; set; } }

                [TypeForge]
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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region v0.4 Generator Tests

public class EnumForgingGeneratorTests
{
    [Fact]
    public void Generator_EnumToEnum_GeneratesParseByName()
    {
        var source = """
            using TypeForge;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }
                public enum StatusDto { Active, Inactive }

                [TypeForge]
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
            using TypeForge;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }

                [TypeForge]
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
            using TypeForge;

            namespace TestNamespace
            {
                public enum Status { Active, Inactive }

                [TypeForge]
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
        Assert.Contains("true", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class ConstructorMappingGeneratorTests
{
    [Fact]
    public void Generator_RecordType_GeneratesConstructorCall()
    {
        var source = """
            using TypeForge;
            using System;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                }

                public record DestRecord(string Id, string Name);

                [TypeForge]
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
            using TypeForge;

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

                [TypeForge]
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
        Assert.Contains("result.Name = source.Name;", generatedCode);
        Assert.Contains("result.Total = source.Total;", generatedCode);
    }

    [Fact]
    public void Generator_ConstructorParameterNotMatched_ReportsError()
    {
        var source = """
            using TypeForge;

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

                [TypeForge]
                public partial class TestForger
                {
                    public partial DestWithCtor Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var error = diagnostics.FirstOrDefault(d => d.Id == "TF0014");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class AutoFlatteningGeneratorTests
{
    [Fact]
    public void Generator_AutoFlatten_GeneratesNestedAccess()
    {
        var source = """
            using TypeForge;

            namespace TestNamespace
            {
                public class AddressInfo
                {
                    public string City { get; set; }
                }

                public class Company
                {
                    public string Name { get; set; }
                    public AddressInfo Address { get; set; }
                }

                public class Employee
                {
                    public int Id { get; set; }
                    public Company Company { get; set; }
                }

                public class EmployeeDto
                {
                    public int Id { get; set; }
                    public string CompanyName { get; set; }
                    public string CompanyAddressCity { get; set; }
                }

                [TypeForge]
                public partial class TestForger
                {
                    public partial EmployeeDto Forge(Employee source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("CompanyName =", generatedCode);
        Assert.Contains("CompanyAddressCity =", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion
