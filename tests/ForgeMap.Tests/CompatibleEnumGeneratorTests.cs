using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

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
        return TestHelper.RunGenerator(source);
    }
}
