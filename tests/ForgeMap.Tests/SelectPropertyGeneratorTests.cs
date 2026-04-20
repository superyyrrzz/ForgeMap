using Microsoft.CodeAnalysis;
using Xunit;

namespace ForgeMap.Tests;

public class SelectPropertyGeneratorTests
{
    [Fact]
    public void Generator_SelectProperty_ListOfEntityToListOfPrimitive_EmitsSelect()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<string> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Name))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains(".Select(", generated);
        Assert.Contains(".Name", generated);
        Assert.Contains("ToList", generated);
    }

    [Fact]
    public void Generator_SelectProperty_ArrayDestination_EmitsToArray()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public int Id { get; set; } }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public int[] Tags { get; set; } = System.Array.Empty<int>(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Id))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("ToArray", generated);
    }

    [Fact]
    public void Generator_SelectProperty_HashSetDestination_EmitsHashSetCtor()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public HashSet<string> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Name))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("HashSet", generated);
    }

    [Fact]
    public void Generator_SelectProperty_ConflictsWithConvertWith_EmitsFM0058()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<string> Tags { get; set; } = new(); }

public static class Conv { public static List<string> Do(List<Tag> t) => new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags),
        SelectProperty = nameof(Tag.Name),
        ConvertWith = nameof(Conv.Do))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0058");
    }

    [Fact]
    public void Generator_SelectProperty_SourceNotEnumerable_EmitsFM0055()
    {
        var source = @"
using ForgeMap;

public class SourceType { public string Tag { get; set; } = string.Empty; }
public class DestType { public System.Collections.Generic.List<string> Tag { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tag), nameof(DestType.Tag), SelectProperty = ""Length"")]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0055");
    }

    [Fact]
    public void Generator_SelectProperty_MemberNotFound_EmitsFM0056()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<string> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = ""DoesNotExist"")]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0056");
    }

    [Fact]
    public void Generator_SelectProperty_DestinationNotEnumerable_EmitsFM0073()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public string Tags { get; set; } = string.Empty; }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Name))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0073");
    }

    [Fact]
    public void Generator_SelectProperty_IncompatibleElementType_EmitsFM0057()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public Tag Self => this; public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<int> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Self))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0057");
    }

    [Fact]
    public void Generator_SelectProperty_EnumToString_ComposesCoercion()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public enum Color { Red, Green, Blue }
public class Tag { public Color Color { get; set; } }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<string> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Color))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains(".ToString()", generated);
    }

    [Fact]
    public void Generator_SelectProperty_ConflictsWithForgeWith_EmitsFM0072()
    {
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class Tag { public string Name { get; set; } = string.Empty; }
public class SourceType { public List<Tag> Tags { get; set; } = new(); }
public class DestType { public List<string> Tags { get; set; } = new(); }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(nameof(SourceType.Tags), nameof(DestType.Tags), SelectProperty = nameof(Tag.Name))]
    [ForgeWith(nameof(DestType.Tags), nameof(ProvideTags))]
    public partial DestType Forge(SourceType source);

    private static List<string> ProvideTags(SourceType s) => new();
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0072");
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
