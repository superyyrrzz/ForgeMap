using Microsoft.CodeAnalysis;
using Xunit;
using static ForgeMap.Tests.TestHelper;

namespace ForgeMap.Tests;

public class ConditionalAssignmentGeneratorTests
{
    [Fact]
    public void Condition_OnForgeInto_EmitsGuardedAssignment()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(IsNotNull))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IsNotNull(string? v) => v is not null;
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (IsNotNull(source.Name))", generated);
        Assert.Contains("destination.Name = source.Name", generated);
    }

    [Fact]
    public void SkipWhen_OnForgeInto_EmitsNegatedGuard()
    {
        var source = @"
using ForgeMap;

public class Src { public int Id { get; set; } }
public class Dst { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Id), nameof(Dst.Id), SkipWhen = nameof(IdIsZero))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IdIsZero(Src s) => s.Id == 0;
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (!IdIsZero(source))", generated);
        Assert.Contains("destination.Id = source.Id", generated);
    }

    [Fact]
    public void Condition_OnForge_EmitsPostConstructionGuard()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Protocol { get; set; } }
public class Dst { public string Protocol { get; set; } = ""default""; }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Protocol), nameof(Dst.Protocol), Condition = nameof(IsNotNull))]
    public partial Dst Forge(Src source);

    private static bool IsNotNull(string? v) => v is not null;
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("var result = new", generated);
        Assert.Contains("if (IsNotNull(source.Protocol))", generated);
        Assert.Contains("result.Protocol = source.Protocol", generated);
    }

    [Fact]
    public void Condition_BothConditionAndSkipWhen_EmitsFM0060()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name),
        Condition = nameof(IsNotNull), SkipWhen = nameof(IsEmptySrc))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IsNotNull(string? v) => v is not null;
    private static bool IsEmptySrc(Src s) => s.Name is null;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0060");
    }

    [Fact]
    public void Condition_PredicateNotFound_EmitsFM0061()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = ""DoesNotExist"")]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0061");
    }

    [Fact]
    public void Condition_PredicateWrongReturnType_EmitsFM0061()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(NotABool))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static int NotABool(string? v) => 0;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0061");
    }

    [Fact]
    public void Condition_OnInitOnlyProperty_EmitsFM0062()
    {
        var source = @"
using ForgeMap;

public class Src { public string Name { get; set; } = """"; }
public class Dst { public string Name { get; init; } = """"; }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(IsNotEmpty))]
    public partial Dst Forge(Src source);

    private static bool IsNotEmpty(string v) => v.Length > 0;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0062");
    }

    [Fact]
    public void Condition_OnConstructorParameter_EmitsFM0062()
    {
        var source = @"
using ForgeMap;

public class Src { public string Name { get; set; } = """"; }
public class Dst
{
    public Dst(string name) { Name = name; }
    public string Name { get; }
}

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(IsNotEmpty))]
    public partial Dst Forge(Src source);

    private static bool IsNotEmpty(string v) => v.Length > 0;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0062");
    }

    [Fact]
    public void Condition_WithForgeFrom_EmitsFM0063()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(IsNotNull))]
    [ForgeFrom(nameof(Dst.Name), nameof(Resolve))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IsNotNull(string? v) => v is not null;
    private static string? Resolve(Src s) => s.Name;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0063");
    }

    [Fact]
    public void Condition_WithSelectProperty_FM0063_SuppressedByFM0072()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = """"; }
public class Src { public List<Tag>? Tags { get; set; } }
public class Dst { public List<string>? Tags { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Tags), nameof(Dst.Tags),
        SelectProperty = nameof(Tag.Name),
        Condition = nameof(HasItems))]
    [ForgeFrom(nameof(Dst.Tags), nameof(Resolve))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool HasItems(List<Tag>? t) => t is { Count: > 0 };
    private static List<string> Resolve(Src s) => new();
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0072");
        Assert.DoesNotContain(diagnostics, d => d.Id == "FM0063");
    }

    [Fact]
    public void Condition_ComposedWithConvertWith_GuardWrapsConverter()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Raw { get; set; } }
public class Dst { public int Cooked { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Raw), nameof(Dst.Cooked),
        Condition = nameof(IsNumber),
        ConvertWith = nameof(Parse))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IsNumber(string? v) => int.TryParse(v, out _);
    private static int Parse(string s) => int.Parse(s);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (IsNumber(source.Raw))", generated);
        Assert.Contains("Parse(", generated);
    }

    [Fact]
    public void SkipWhen_OnIgnoredProperty_NoDiagnostic_NoEmit()
    {
        var source = @"
using ForgeMap;

public class Src { public int Id { get; set; } }
public class Dst { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [Ignore(nameof(Dst.Id))]
    [ForgeProperty(nameof(Src.Id), nameof(Dst.Id), SkipWhen = nameof(IdIsZero))]
    public partial void ForgeInto(Src source, [UseExistingValue] Dst destination);

    private static bool IdIsZero(Src s) => s.Id == 0;
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("IdIsZero", generated);
        Assert.DoesNotContain("destination.Id = source.Id", generated);
    }

    [Fact]
    public void Condition_DoesNotPropagateThroughReverseForge()
    {
        var source = @"
using ForgeMap;

public class Src { public string? Name { get; set; } }
public class Dst { public string? Name { get; set; } }

[ForgeMap]
public partial class M
{
    [ForgeProperty(nameof(Src.Name), nameof(Dst.Name), Condition = nameof(IsNotNull))]
    [ReverseForge]
    public partial Dst Forge(Src source);
    public partial Src ForgeReverse(Dst source);

    private static bool IsNotNull(string? v) => v is not null;
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (IsNotNull(source.Name))", generated);
        var reverseStart = generated.IndexOf("ForgeReverse");
        Assert.True(reverseStart > 0, "Generated code should contain ForgeReverse body");
        var reverseBody = generated.Substring(reverseStart);
        Assert.DoesNotContain("IsNotNull(", reverseBody);
    }
}
