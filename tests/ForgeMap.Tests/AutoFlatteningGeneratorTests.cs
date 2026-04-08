using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

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
        return TestHelper.RunGenerator(source);
    }
}
