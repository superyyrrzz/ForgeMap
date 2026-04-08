using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class StringToEnumGeneratorTests
{
    [Fact]
    public void Generator_StringToEnum_Parse_GeneratesEnumParse()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse(typeof(global::Priority), source.Priority, true)", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_TryParse_GeneratesTryParse()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap(StringToEnum = StringToEnumConversion.TryParse)]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.TryParse", generatedCode);
        Assert.Contains("global::Priority", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_None_DoesNotAutoConvert()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap(StringToEnum = StringToEnumConversion.None)]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        // With StringToEnum = None, the string→enum path is skipped;
        // the property names match but types are incompatible, so no auto-conversion
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("Enum.Parse", generatedCode);
        Assert.DoesNotContain("Enum.TryParse", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_NullableSource_GeneratesNullHandling()
    {
        var source = @"
#nullable enable
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string? Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_ToNullableDest_GeneratesNullableCast()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority? Priority { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse", generatedCode);
        Assert.Contains("global::Priority?", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_ReverseForge_GeneratesToString()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ReverseForge]
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Forward: string → enum via Enum.Parse
        Assert.Contains("Enum.Parse", generatedCode);
        // Reverse: enum → string via .ToString()
        Assert.Contains(".ToString()", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_ExplicitForgeProperty_TakesPrecedence()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } public string Name { get; set; } }
public class Dest { public Priority Priority { get; set; } public string Name { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ForgeFrom(nameof(Dest.Priority), nameof(ResolvePriority))]
    public partial Dest Forge(Source source);

    private static Priority ResolvePriority(Source s) => Priority.Low;
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Should use the resolver, not auto-parse
        Assert.Contains("ResolvePriority", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_CtorParam_GeneratesParseInCtor()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public int Id { get; set; } public string Priority { get; set; } }
public class Dest
{
    public Dest(int id, Priority priority) { Id = id; Priority = priority; }
    public int Id { get; }
    public Priority Priority { get; }
}

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_AssemblyDefaults_Propagates()
    {
        var source = @"
using ForgeMap;

[assembly: ForgeMapDefaults(StringToEnum = StringToEnumConversion.TryParse)]

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } }
public class Dest { public Priority Priority { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.TryParse", generatedCode);
    }

    [Fact]
    public void Generator_EnumToString_InPropertyAssignment_GeneratesToString()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public Priority Priority { get; set; } public int Id { get; set; } }
public class Dest { public string Priority { get; set; } public int Id { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains(".ToString()", generatedCode);
    }

    [Fact]
    public void Generator_StringToEnum_ForgeInto_GeneratesEnumParse()
    {
        var source = @"
using ForgeMap;

public enum Priority { Low, Medium, High }
public class Source { public string Priority { get; set; } public int Id { get; set; } }
public class Dest { public Priority Priority { get; set; } public int Id { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial void ForgeInto(Source source, [UseExistingValue] Dest target);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse", generatedCode);
        Assert.Contains("typeof(global::Priority)", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
