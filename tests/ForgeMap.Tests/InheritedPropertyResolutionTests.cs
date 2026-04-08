using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

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
        return TestHelper.RunGenerator(source);
    }
}
