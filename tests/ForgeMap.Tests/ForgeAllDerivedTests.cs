using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

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
        return TestHelper.RunGenerator(source);
    }
}
