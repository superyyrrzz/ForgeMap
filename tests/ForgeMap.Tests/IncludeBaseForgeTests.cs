using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

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
        return TestHelper.RunGenerator(source);
    }
}
