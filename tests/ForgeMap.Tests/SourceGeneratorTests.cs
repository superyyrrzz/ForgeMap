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
        var (diagnostics, _) = RunGenerator(source);

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

        // Get references to the ForgeMap.Abstractions assembly
        var abstractionsAssembly = typeof(ForgeMapAttribute).Assembly;
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

        var generator = new ForgeMapGenerator();

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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

public class AutoFlatteningGeneratorTests
{
    [Fact]
    public void Generator_AutoFlatten_GeneratesNestedAccess()
    {
        var source = """
            using ForgeMap;

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

                [ForgeMap]
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

#region v0.5 Generator Tests

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
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region v0.6 Generator Tests

public class HookGeneratorTests
{
    [Fact]
    public void Generator_BeforeForge_GeneratesCallBeforeMapping()
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
                    [BeforeForge(nameof(Validate))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Validate(SourceEntity source) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Validate() should appear before property mapping
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(validateIndex < idAssignIndex, "BeforeForge should be called before property mapping");
    }

    [Fact]
    public void Generator_AfterForge_GeneratesCallAfterMapping()
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
                    [AfterForge(nameof(Enrich))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Enrich() should appear after property mapping
        var enrichIndex = generatedCode.IndexOf("Enrich(source, result)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex > idAssignIndex, "AfterForge should be called after property mapping");
    }

    [Fact]
    public void Generator_BeforeAndAfterForge_GeneratesCorrectOrder()
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
                    [BeforeForge(nameof(Validate))]
                    [AfterForge(nameof(Enrich))]
                    public partial DestDto Forge(SourceEntity source);

                    private static void Validate(SourceEntity source) { }
                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        var nullCheckIndex = generatedCode.IndexOf("source == null", StringComparison.Ordinal);
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        var enrichIndex = generatedCode.IndexOf("Enrich(source, result)", StringComparison.Ordinal);
        var returnIndex = generatedCode.IndexOf("return result;", StringComparison.Ordinal);

        // Verify all elements are present in generated code
        Assert.True(nullCheckIndex >= 0, "Null check should be in generated code");
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");
        Assert.True(returnIndex >= 0, "Return statement should be in generated code");

        // Verify execution order: null check → BeforeForge → mapping → AfterForge → return
        Assert.True(nullCheckIndex < validateIndex, "Null check before BeforeForge");
        Assert.True(validateIndex < idAssignIndex, "BeforeForge before mapping");
        Assert.True(idAssignIndex < enrichIndex, "Mapping before AfterForge");
        Assert.True(enrichIndex < returnIndex, "AfterForge before return");
    }

    [Fact]
    public void Generator_HookMethodNotFound_ReportsFM0016()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge("NonExistentMethod")]
                    public partial DestDto Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var hookError = diagnostics.FirstOrDefault(d => d.Id == "FM0016");
        Assert.NotNull(hookError);
        Assert.Equal(DiagnosticSeverity.Error, hookError.Severity);
    }

    [Fact]
    public void Generator_AfterForge_InvalidSignature_ReportsFM0016()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                }

                public class DestDto
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [AfterForge(nameof(BadHook))]
                    public partial DestDto Forge(SourceEntity source);

                    // Wrong signature: AfterForge needs (source, dest), not just (source)
                    private static void BadHook(SourceEntity source) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var hookError = diagnostics.FirstOrDefault(d => d.Id == "FM0016");
        Assert.NotNull(hookError);
        Assert.Equal(DiagnosticSeverity.Error, hookError.Severity);
    }

    [Fact]
    public void Generator_ForgeInto_WithHooks_GeneratesCorrectCode()
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
                    [BeforeForge(nameof(Validate))]
                    [AfterForge(nameof(Enrich))]
                    public partial void ForgeInto(SourceEntity source, [UseExistingValue] DestDto destination);

                    private static void Validate(SourceEntity source) { }
                    private static void Enrich(SourceEntity source, DestDto dest) { }
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Verify hooks are present
        Assert.Contains("Validate(source)", generatedCode);
        Assert.Contains("Enrich(source, destination)", generatedCode);

        // Verify order: null checks → BeforeForge → mapping → AfterForge
        var nullCheckIndex = generatedCode.IndexOf("source == null", StringComparison.Ordinal);
        var validateIndex = generatedCode.IndexOf("Validate(source)", StringComparison.Ordinal);
        var idAssignIndex = generatedCode.IndexOf("destination.Id = source.Id", StringComparison.Ordinal);
        var enrichIndex = generatedCode.IndexOf("Enrich(source, destination)", StringComparison.Ordinal);

        Assert.True(nullCheckIndex >= 0, "Null check should be in generated code");
        Assert.True(validateIndex >= 0, "BeforeForge call should be in generated code");
        Assert.True(idAssignIndex >= 0, "Property mapping should be in generated code");
        Assert.True(enrichIndex >= 0, "AfterForge call should be in generated code");

        Assert.True(nullCheckIndex < validateIndex);
        Assert.True(validateIndex < idAssignIndex);
        Assert.True(idAssignIndex < enrichIndex);
    }

    [Fact]
    public void Generator_HooksOnEnumForge_ReportsFM0018()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public enum SourceEnum { A, B }
                public enum DestEnum { A, B }

                [ForgeMap]
                public partial class TestForger
                {
                    [BeforeForge(nameof(Validate))]
                    public partial DestEnum Forge(SourceEnum source);

                    private static void Validate(SourceEnum source) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0018");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void Generator_HooksOnCollectionForge_ReportsFM0018()
    {
        var source = """
            using ForgeMap;
            using System.Collections.Generic;

            namespace TestNamespace
            {
                public class SourceItem
                {
                    public int Id { get; set; }
                }

                public class DestItem
                {
                    public int Id { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [AfterForge(nameof(LogList))]
                    public partial List<DestItem> Forge(List<SourceItem> source);

                    public partial DestItem Forge(SourceItem source);

                    private static void LogList(List<SourceItem> source, List<DestItem> dest) { }
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var warning = diagnostics.FirstOrDefault(d => d.Id == "FM0018");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region v1.1 Generator Tests — Inherited Property Resolution

public class InheritedPropertyResolutionTests
{
    [Fact]
    public void Generator_InheritedProperties_IncludesBaseClassProperties()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public string Uid { get; set; }
                    public string Name { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Stem { get; set; }
                    public string Kind { get; set; }
                }

                public class DerivedDto
                {
                    public string Uid { get; set; }
                    public string Name { get; set; }
                    public string Stem { get; set; }
                    public string Kind { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Uid = source.Uid,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("Stem = source.Stem,", generatedCode);
        Assert.Contains("Kind = source.Kind,", generatedCode);
    }

    [Fact]
    public void Generator_InheritedProperties_BaseFirstOrdering()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Value { get; set; }
                }

                public class DerivedDto
                {
                    public int Id { get; set; }
                    public string Value { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Base property (Id) should appear before derived property (Value)
        var idIndex = generatedCode.IndexOf("Id = source.Id", StringComparison.Ordinal);
        var valueIndex = generatedCode.IndexOf("Value = source.Value", StringComparison.Ordinal);
        Assert.True(idIndex >= 0, "Id property assignment not found in generated code");
        Assert.True(valueIndex >= 0, "Value property assignment not found in generated code");
        Assert.True(idIndex < valueIndex, "Base property (Id) should appear before derived property (Value)");
    }

    [Fact]
    public void Generator_InheritedProperties_ShadowedPropertyUsesDerived()
    {
        // The base declares Label as object; derived shadows it as string.
        // If the generator incorrectly uses the base declaration, the generated
        // code would fail because object cannot be assigned to string without a cast.
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                    public object Label { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public new string Label { get; set; }
                    public string Extra { get; set; }
                }

                public class DerivedDto
                {
                    public int Id { get; set; }
                    public string Label { get; set; }
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        // Label must appear — if generator used base object type, it would be skipped
        // due to type mismatch (object vs string)
        Assert.Contains("Label = source.Label,", generatedCode);
        Assert.Contains("Extra = source.Extra,", generatedCode);
    }

    [Fact]
    public void Generator_InheritedProperties_MultiLevelHierarchy()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class GrandparentEntity
                {
                    public int Id { get; set; }
                }

                public class ParentEntity : GrandparentEntity
                {
                    public string Name { get; set; }
                }

                public class ChildEntity : ParentEntity
                {
                    public string Detail { get; set; }
                }

                public class ChildDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Detail { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial ChildDto Forge(ChildEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("Detail = source.Detail,", generatedCode);
    }

    [Fact]
    public void Generator_InheritedProperties_DestinationInheritance()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Detail { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Detail { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DerivedDto Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("Detail = source.Detail,", generatedCode);
    }

    [Fact]
    public void Generator_InheritedProperties_DestinationGetOnlyProperty_IsIgnored()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Detail { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }

                    // This inherited property is get-only and must not be assigned by the generator.
                    public string Computed => $"{Name}-{Id}";
                }

                public class DerivedDto : BaseDto
                {
                    public string Detail { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial DerivedDto Forge(SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        Assert.Contains("Name = source.Name,", generatedCode);
        Assert.Contains("Detail = source.Detail,", generatedCode);
        Assert.DoesNotContain("Computed =", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region v1.1 Generator Tests — IncludeBaseForge

public class IncludeBaseForgeTests
{
    [Fact]
    public void Generator_IncludeBaseForge_InheritsIgnore()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(BaseDto.AuditTrail))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // The derived method should NOT have AuditTrail assignment (inherited [Ignore])
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");
        Assert.DoesNotContain("AuditTrail", derivedMethodLines);

        // But should have Id, Name, Extra
        Assert.Contains("Id = source.Id", derivedMethodLines);
        Assert.Contains("Name = source.Name", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_InheritsForgeProperty()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public string Uid { get; set; }
                    public string Name { get; set; }
                }

                public class BaseDto
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty(nameof(BaseEntity.Uid), nameof(BaseDto.Id))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // Should have Id = source.Uid (inherited [ForgeProperty])
        Assert.Contains("Id = source.Uid", derivedMethodLines);
        Assert.Contains("Name = source.Name", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_InheritsForgeFrom()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public decimal Subtotal { get; set; }
                    public decimal TaxRate { get; set; }
                }

                public class BaseDto
                {
                    public decimal TotalWithTax { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeFrom(nameof(BaseDto.TotalWithTax), nameof(CalculateTotal))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);

                    private static decimal CalculateTotal(BaseEntity source)
                        => source.Subtotal * (1 + source.TaxRate);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // Should have TotalWithTax resolved via CalculateTotal (inherited [ForgeFrom])
        Assert.Contains("CalculateTotal", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_InheritsForgeWith()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class AddressEntity
                {
                    public string Street { get; set; }
                }

                public class AddressDto
                {
                    public string Street { get; set; }
                }

                public class BaseEntity
                {
                    public int Id { get; set; }
                    public AddressEntity Address { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public AddressDto Address { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial AddressDto Forge(AddressEntity source);

                    [ForgeWith(nameof(BaseDto.Address), nameof(Forge))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // Should have Address mapped via Forge (inherited [ForgeWith])
        Assert.Contains("Forge(", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_ExplicitOverridesInherited()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Status { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string Status { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string StatusCode { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(BaseDto.Status))]
                    public partial BaseDto Forge(BaseEntity source);

                    // Status is NOT ignored here — the explicit [ForgeProperty] overrides the inherited [Ignore]
                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    [ForgeProperty(nameof(DerivedEntity.StatusCode), nameof(DerivedDto.Status))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // Status should be mapped via explicit ForgeProperty (not ignored)
        Assert.Contains("Status = source.StatusCode", derivedMethodLines);

        // FM0021 info diagnostic should be reported for the override
        var infoDiags = diagnostics.Where(d => d.Id == "FM0021").ToList();
        Assert.NotEmpty(infoDiags);
    }

    [Fact]
    public void Generator_IncludeBaseForge_Chaining()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class MiddleEntity : BaseEntity
                {
                    public string MiddleProp { get; set; }
                    public string InternalFlag { get; set; }
                }

                public class MiddleDto : BaseDto
                {
                    public string MiddleProp { get; set; }
                    public string InternalFlag { get; set; }
                }

                public class LeafEntity : MiddleEntity
                {
                    public string LeafProp { get; set; }
                }

                public class LeafDto : MiddleDto
                {
                    public string LeafProp { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(BaseDto.AuditTrail))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    [Ignore(nameof(MiddleDto.InternalFlag))]
                    public partial MiddleDto Forge(MiddleEntity source);

                    // Inherits AuditTrail ignore (from base) and InternalFlag ignore (from middle)
                    [IncludeBaseForge(typeof(MiddleEntity), typeof(MiddleDto))]
                    public partial LeafDto Forge(LeafEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var leafMethodLines = GetMethodBody(lines, "LeafDto Forge(TestNamespace.LeafEntity");

        // Should NOT have AuditTrail (inherited from base via middle)
        Assert.DoesNotContain("AuditTrail", leafMethodLines);
        // Should NOT have InternalFlag (inherited from middle)
        Assert.DoesNotContain("InternalFlag", leafMethodLines);
        // Should have the rest
        Assert.Contains("Id = source.Id", leafMethodLines);
        Assert.Contains("MiddleProp = source.MiddleProp", leafMethodLines);
        Assert.Contains("LeafProp = source.LeafProp", leafMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_FM0019_BaseMethodNotFound()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    // No base forge method for BaseEntity → BaseDto exists!
                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        var errorDiags = diagnostics.Where(d => d.Id == "FM0019").ToList();
        Assert.Single(errorDiags);
        Assert.Equal(DiagnosticSeverity.Error, errorDiags[0].Severity);
    }

    [Fact]
    public void Generator_IncludeBaseForge_FM0020_TypeMismatch()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                }

                // UnrelatedEntity does NOT derive from BaseEntity
                public class UnrelatedEntity
                {
                    public int Id { get; set; }
                    public string Extra { get; set; }
                }

                public class UnrelatedDto
                {
                    public int Id { get; set; }
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    public partial BaseDto Forge(BaseEntity source);

                    // UnrelatedEntity does not derive from BaseEntity → FM0020
                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial UnrelatedDto Forge(UnrelatedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        var errorDiags = diagnostics.Where(d => d.Id == "FM0020").ToList();
        Assert.NotEmpty(errorDiags);
        Assert.Equal(DiagnosticSeverity.Error, errorDiags[0].Severity);
    }

    [Fact]
    public void Generator_IncludeBaseForge_CombinedIgnoreAndForgeProperty()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public string Uid { get; set; }
                    public string Name { get; set; }
                    public string Secret { get; set; }
                }

                public class BaseDto
                {
                    public string Id { get; set; }
                    public string Name { get; set; }
                    public string Secret { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [Ignore(nameof(BaseDto.Secret))]
                    [ForgeProperty(nameof(BaseEntity.Uid), nameof(BaseDto.Id))]
                    public partial BaseDto Forge(BaseEntity source);

                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // Should have inherited ForgeProperty (Id = source.Uid)
        Assert.Contains("Id = source.Uid", derivedMethodLines);
        // Should NOT have Secret (inherited Ignore)
        Assert.DoesNotContain("Secret", derivedMethodLines);
        // Should have Name and Extra
        Assert.Contains("Name = source.Name", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    [Fact]
    public void Generator_IncludeBaseForge_ForgeIntoPattern_InheritsIgnore()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class BaseDto
                {
                    public int Id { get; set; }
                    public string Name { get; set; }
                    public string AuditTrail { get; set; }
                }

                public class DerivedEntity : BaseEntity
                {
                    public string Extra { get; set; }
                }

                public class DerivedDto : BaseDto
                {
                    public string Extra { get; set; }
                }

                [ForgeMap]
                public partial class TestForger
                {
                    // Base mapping uses ForgeInto pattern (void + [UseExistingValue])
                    [Ignore(nameof(BaseDto.AuditTrail))]
                    public partial void ForgeInto(BaseEntity source, [UseExistingValue] BaseDto dest);

                    // Derived uses return-style but inherits from ForgeInto base
                    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        var lines = generatedCode.Split('\n');
        var derivedMethodLines = GetMethodBody(lines, "DerivedDto Forge(TestNamespace.DerivedEntity");

        // AuditTrail should be ignored (inherited from ForgeInto base)
        Assert.DoesNotContain("AuditTrail", derivedMethodLines);
        // Should have Id, Name, Extra
        Assert.Contains("Id = source.Id", derivedMethodLines);
        Assert.Contains("Name = source.Name", derivedMethodLines);
        Assert.Contains("Extra = source.Extra", derivedMethodLines);
    }

    /// <summary>
    /// Extracts the body of a generated method by finding its signature line and collecting
    /// everything up to the matching closing brace.
    /// </summary>
    private static string GetMethodBody(string[] lines, string signatureFragment)
    {
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(signatureFragment))
            {
                start = i;
                break;
            }
        }

        if (start == -1)
            return string.Empty;

        // Collect lines until we find the closing brace at the same indent level
        int braceDepth = 0;
        var sb = new System.Text.StringBuilder();
        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i];
            sb.AppendLine(line);
            foreach (char c in line)
            {
                if (c == '{') braceDepth++;
                if (c == '}') braceDepth--;
            }
            if (braceDepth == 0 && i > start)
                break;
        }
        return sb.ToString();
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region Compatible Enum Auto-Cast Tests

public class CompatibleEnumGeneratorTests
{
    [Fact]
    public void Generator_CompatibleEnums_DifferentNamespaces_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        // Should emit cast, not direct assignment
        Assert.Contains("(Dest.Priority)(int)source.Priority", generatedCode);
        Assert.DoesNotContain("Priority = source.Priority,", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_DifferentValues_NoCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Status { Active = 0, Inactive = 1 }

                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Status Status { get; set; }
                }
            }

            namespace Dest
            {
                public enum Status { Active = 0, Inactive = 2 }

                public class DestDto
                {
                    public int Id { get; set; }
                    public Status Status { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("Id = source.Id,", generatedCode);
        // Different values: should NOT emit cast — Status property should be skipped
        Assert.DoesNotContain("(Dest.Status)(int)source.Status", generatedCode);
        Assert.DoesNotContain("Status = source.Status,", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_DifferentMemberCount_NoCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Color { Red, Green, Blue }

                public class SourceEntity
                {
                    public Color Color { get; set; }
                }
            }

            namespace Dest
            {
                public enum Color { Red, Green }

                public class DestDto
                {
                    public Color Color { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Different member count: no cast, property skipped
        Assert.DoesNotContain("(Dest.Color)(int)source.Color", generatedCode);
        Assert.DoesNotContain("Color = source.Color,", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_NullableSourceToNonNullableDest_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public Priority? Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public Priority Priority { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Nullable<EnumA> -> EnumB: should emit cast using the nullable source's Value with ! to suppress CS8629
        Assert.Contains("(Dest.Priority)(int)source.Priority!.Value", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_NonNullableToNullableDest_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public Priority Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public Priority? Priority { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // EnumA -> Nullable<EnumB>: should emit cast
        Assert.Contains("(Dest.Priority?)(int)source.Priority", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_NullableToNullable_EmitsCastWithNullPropagation()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public Priority? Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public Priority? Priority { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Nullable<EnumA> -> Nullable<EnumB>: should propagate null via pattern match (single evaluation)
        // Assert on structure, not the temporary variable name (implementation detail)
        Assert.Contains("source.Priority is { }", generatedCode);
        Assert.Contains("(Dest.Priority?)(int)", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_DifferentMemberNames_NoCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Status { Active, Inactive }

                public class SourceEntity
                {
                    public Status Status { get; set; }
                }
            }

            namespace Dest
            {
                public enum Status { Enabled, Disabled }

                public class DestDto
                {
                    public Status Status { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Different member names: no cast, property skipped
        Assert.DoesNotContain("(Dest.Status)(int)source.Status", generatedCode);
        Assert.DoesNotContain("Status = source.Status,", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_ForgeInto_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                    public partial void ForgeInto(Source.SourceEntity source, Dest.DestDto dest);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // ForgeInto path should also emit compatible enum cast
        Assert.Contains("(Dest.Priority)(int)source.Priority", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_LongUnderlyingType_EmitsCastWithLong()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum BigId : long { A = 0, B = 1, C = 2 }

                public class SourceEntity
                {
                    public BigId BigId { get; set; }
                }
            }

            namespace Dest
            {
                public enum BigId : long { A = 0, B = 1, C = 2 }

                public class DestDto
                {
                    public BigId BigId { get; set; }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Should use 'long' not 'int' for underlying type
        Assert.Contains("(Dest.BigId)(long)source.BigId", generatedCode);
        Assert.DoesNotContain("(Dest.BigId)(int)source.BigId", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_CtorParam_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }

                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }

                public class DestDto
                {
                    public int Id { get; }
                    public Priority Priority { get; }
                    public DestDto(int id, Priority priority)
                    {
                        Id = id;
                        Priority = priority;
                    }
                }
            }

            namespace Mappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Ctor param path should emit compatible enum cast
        Assert.Contains("(Dest.Priority)(int)source.Priority", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_ForgeProperty_EmitsCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Priority SourcePriority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }
                public class DestDto
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace TestMappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty("SourcePriority", "Priority")]
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // [ForgeProperty] path should emit compatible enum cast
        Assert.Contains("(Dest.Priority)(int)", generatedCode);
        Assert.Contains("source.SourcePriority", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_DifferentUnderlyingTypes_NoCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority : byte { Low, Medium, High }
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority : int { Low, Medium, High }
                public class DestDto
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace TestMappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Different underlying types (byte vs int) should NOT produce a compatible enum cast
        Assert.DoesNotContain("(Dest.Priority)(byte)", generatedCode);
        Assert.DoesNotContain("(Dest.Priority)(int)", generatedCode);
    }

    [Fact]
    public void Generator_CompatibleEnums_ForgeProperty_NestedNullConditional_EmitsCorrectCast()
    {
        var source = """
            using ForgeMap;

            namespace Source
            {
                public enum Priority { Low, Medium, High }
                public class Customer
                {
                    public Priority Priority { get; set; }
                }
                public class SourceEntity
                {
                    public int Id { get; set; }
                    public Customer? Customer { get; set; }
                }
            }

            namespace Dest
            {
                public enum Priority { Low, Medium, High }
                public class DestDto
                {
                    public int Id { get; set; }
                    public Priority Priority { get; set; }
                }
            }

            namespace TestMappers
            {
                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeProperty("Customer.Priority", "Priority")]
                    public partial Dest.DestDto Forge(Source.SourceEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Lifted enum from null-conditional should use .HasValue/.Value or !.Value pattern
        Assert.Contains("(Dest.Priority)(int)", generatedCode);
        // Must NOT directly cast the lifted null-conditional expression without
        // parenthesizing it first — source.Customer?.Priority is Priority? at runtime,
        // so casting it directly (without .Value/.HasValue) is incorrect.
        Assert.DoesNotContain("(int)source.Customer?.Priority", generatedCode);
        // The expression must be wrapped in parens to break the ?. chain
        Assert.Contains("(source.Customer?.Priority)", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion

#region ForgeAllDerived Tests

public class ForgeAllDerivedTests
{
    [Fact]
    public void ForgeAllDerived_BasicPolymorphicDispatch_GeneratesIsCascade()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedAEntity : BaseEntity { public string Extra { get; set; } }
                public class DerivedBEntity : BaseEntity { public int Score { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class DerivedADto : BaseDto { public string Extra { get; set; } }
                public class DerivedBDto : BaseDto { public int Score { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedADto Forge(DerivedAEntity source);
                    public partial DerivedBDto Forge(DerivedBEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Should contain polymorphic dispatch is-checks
        Assert.Contains("source is TestNamespace.DerivedAEntity", generatedCode);
        Assert.Contains("source is TestNamespace.DerivedBEntity", generatedCode);
        Assert.Contains("return Forge(", generatedCode);

        // Should still contain base fallback mapping
        Assert.Contains("Id = source.Id,", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_MultiLevelInheritance_MostDerivedFirst()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class ChildEntity : BaseEntity { public string Name { get; set; } }
                public class GrandChildEntity : ChildEntity { public int Level { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class ChildDto : BaseDto { public string Name { get; set; } }
                public class GrandChildDto : BaseDto { public int Level { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial ChildDto Forge(ChildEntity source);
                    public partial GrandChildDto Forge(GrandChildEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // GrandChild (depth 2) must appear before Child (depth 1)
        var grandChildIndex = generatedCode.IndexOf("source is TestNamespace.GrandChildEntity", StringComparison.Ordinal);
        var childIndex = generatedCode.IndexOf("source is TestNamespace.ChildEntity", StringComparison.Ordinal);
        Assert.True(grandChildIndex >= 0, "GrandChild is-check should be generated");
        Assert.True(childIndex >= 0, "Child is-check should be generated");
        Assert.True(grandChildIndex < childIndex, "GrandChild should be checked before Child (most-derived first)");
    }

    [Fact]
    public void ForgeAllDerived_NoDerivedMethods_EmitsFM0022Warning()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class BaseDto { public int Id { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Single(generatedTrees);
        var warnings = diagnostics.Where(d => d.Id == "FM0022").ToList();
        Assert.Single(warnings);
        Assert.Equal(DiagnosticSeverity.Warning, warnings[0].Severity);
    }

    [Fact]
    public void ForgeAllDerived_CombinedWithConvertWith_EmitsFM0023Error()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class BaseDto { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { }
                public class DerivedDto : BaseDto { }

                public class MyConverter : ITypeConverter<BaseEntity, BaseDto>
                {
                    public BaseDto Convert(BaseEntity source) => new BaseDto { Id = source.Id };
                }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    [ConvertWith(typeof(MyConverter))]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, _) = RunGenerator(source);

        var errors = diagnostics.Where(d => d.Id == "FM0023").ToList();
        Assert.Single(errors);
        Assert.Equal(DiagnosticSeverity.Error, errors[0].Severity);
    }

    [Fact]
    public void ForgeAllDerived_OnlyMatchesSameMethodName()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Name { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);

                    // Different method name — should NOT be discovered as a derived forge method
                    public partial DerivedDto MapDerived(DerivedEntity source);

                    // Same name — should be discovered
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();
        Assert.Contains("source is TestNamespace.DerivedEntity", generatedCode);
        // No FM0022 warning since Forge(DerivedEntity) was found
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0022"));
    }

    [Fact]
    public void ForgeAllDerived_SameDepthAlphabetical_DeterministicOrdering()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class ZebraEntity : BaseEntity { public string Stripe { get; set; } }
                public class AlphaEntity : BaseEntity { public string First { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class ZebraDto : BaseDto { public string Stripe { get; set; } }
                public class AlphaDto : BaseDto { public string First { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial ZebraDto Forge(ZebraEntity source);
                    public partial AlphaDto Forge(AlphaEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);

        var generatedCode = generatedTrees[0].GetText().ToString();

        // Both at depth 1 — should be alphabetical: AlphaEntity before ZebraEntity
        var alphaIndex = generatedCode.IndexOf("source is TestNamespace.AlphaEntity", StringComparison.Ordinal);
        var zebraIndex = generatedCode.IndexOf("source is TestNamespace.ZebraEntity", StringComparison.Ordinal);
        Assert.True(alphaIndex >= 0, "Alpha is-check should be generated");
        Assert.True(zebraIndex >= 0, "Zebra is-check should be generated");
        Assert.True(alphaIndex < zebraIndex, "Alpha should come before Zebra (alphabetical at same depth)");
    }

    [Fact]
    public void ForgeAllDerived_DispatchCommentIsPresent()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Name { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);
        var generatedCode = generatedTrees[0].GetText().ToString();

        Assert.Contains("Polymorphic dispatch", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_AbstractDestination_GeneratesDispatchOnly()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedAEntity : BaseEntity { public string Extra { get; set; } }
                public class DerivedBEntity : BaseEntity { public int Score { get; set; } }

                public abstract class BaseDto { public int Id { get; set; } }
                public class DerivedADto : BaseDto { public string Extra { get; set; } }
                public class DerivedBDto : BaseDto { public int Score { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedADto Forge(DerivedAEntity source);
                    public partial DerivedBDto Forge(DerivedBEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);
        var generatedCode = generatedTrees[0].GetText().ToString();

        // Should contain dispatch is-checks
        Assert.Contains("source is TestNamespace.DerivedAEntity", generatedCode);
        Assert.Contains("source is TestNamespace.DerivedBEntity", generatedCode);
        Assert.Contains("return Forge(", generatedCode);

        // Should throw NotSupportedException as fallback
        Assert.Contains("throw new global::System.NotSupportedException(", generatedCode);
        Assert.Contains("non-instantiable destination type", generatedCode);

        // Should NOT contain base-type object initializer (no "new BaseDto" for abstract type)
        Assert.DoesNotContain("new TestNamespace.BaseDto", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_InterfaceDestination_GeneratesDispatchOnly()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public interface IBaseDto { int Id { get; set; } }
                public class DerivedDto : IBaseDto { public int Id { get; set; } public string Name { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial IBaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);
        var generatedCode = generatedTrees[0].GetText().ToString();

        // Should contain dispatch
        Assert.Contains("source is TestNamespace.DerivedEntity", generatedCode);

        // Should throw NotSupportedException as fallback
        Assert.Contains("throw new global::System.NotSupportedException(", generatedCode);

        // Should NOT attempt to instantiate the interface
        Assert.DoesNotContain("new TestNamespace.IBaseDto", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_AbstractDestination_EmitsFM0024Warning()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public abstract class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Name { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Single(generatedTrees);
        var fm0024 = diagnostics.Where(d => d.Id == "FM0024").ToList();
        Assert.Single(fm0024);
        Assert.Equal(DiagnosticSeverity.Warning, fm0024[0].Severity);
        // Should NOT have FM0004 (no accessible constructor)
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0004"));
    }

    [Fact]
    public void ForgeAllDerived_AbstractDestination_NoDerived_EmitsFM0022Warning()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }

                public abstract class BaseDto { public int Id { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Single(generatedTrees);
        // FM0022 still emitted with abstract-specific message
        var fm0022 = diagnostics.Where(d => d.Id == "FM0022").ToList();
        Assert.Single(fm0022);
        Assert.Contains("no base-type fallback", fm0022[0].GetMessage());
        // FM0024 NOT emitted (no derived methods found, so no dispatch)
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0024"));
        // FM0004 NOT emitted
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0004"));

        var generatedCode = generatedTrees[0].GetText().ToString();
        // Should still have throw fallback
        Assert.Contains("throw new global::System.NotSupportedException(", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_AbstractDestination_NoFM0004Error()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Extra { get; set; } }

                public abstract class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Extra { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        // No errors at all
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // FM0004 specifically should not appear
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0004"));
    }

    [Fact]
    public void ForgeAllDerived_AbstractDestination_ThrowExceptionNullHandling()
    {
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public abstract class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Name { get; set; } }

                [ForgeMap(NullHandling = NullHandling.ThrowException)]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);
        var generatedCode = generatedTrees[0].GetText().ToString();

        // ThrowException null handling: should throw ArgumentNullException for null source
        Assert.Contains("throw new global::System.ArgumentNullException(nameof(source))", generatedCode);
        // Still has dispatch + NotSupportedException fallback
        Assert.Contains("source is TestNamespace.DerivedEntity", generatedCode);
        Assert.Contains("throw new global::System.NotSupportedException(", generatedCode);
    }

    [Fact]
    public void ForgeAllDerived_ConcreteDestination_UnchangedBehavior()
    {
        // Regression test: concrete destinations should still produce base-type mapping fallback
        var source = """
            using ForgeMap;

            namespace TestNamespace
            {
                public class BaseEntity { public int Id { get; set; } }
                public class DerivedEntity : BaseEntity { public string Name { get; set; } }

                public class BaseDto { public int Id { get; set; } }
                public class DerivedDto : BaseDto { public string Name { get; set; } }

                [ForgeMap]
                public partial class TestForger
                {
                    [ForgeAllDerived]
                    public partial BaseDto Forge(BaseEntity source);
                    public partial DerivedDto Forge(DerivedEntity source);
                }
            }
            """;

        var (diagnostics, generatedTrees) = RunGenerator(source);

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(generatedTrees);
        var generatedCode = generatedTrees[0].GetText().ToString();

        // Should have dispatch
        Assert.Contains("source is TestNamespace.DerivedEntity", generatedCode);
        // Should have base-type mapping fallback (concrete — NOT abstract)
        Assert.Contains("Id = source.Id,", generatedCode);
        // Should NOT have NotSupportedException throw
        Assert.DoesNotContain("NotSupportedException", generatedCode);
        // Should NOT have FM0024
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0024"));
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return SourceGeneratorTests.RunGenerator(source);
    }
}

#endregion
