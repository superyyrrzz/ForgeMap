using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ConvertWithGeneratorTests
{
    [Fact]
    public void Generator_ConvertWith_TypeBased_GeneratesConverterCall()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(MyConverter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::MyConverter().Convert(source)", generatedCode);
    }

    [Fact]
    public void Generator_ConvertWith_MemberBased_GeneratesFieldCall()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    private readonly MyConverter _converter = new MyConverter();

    [ConvertWith(nameof(_converter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("this._converter.Convert(source)", generatedCode);
    }

    [Fact]
    public void Generator_ConvertWith_TypeDoesNotImplementInterface_EmitsFM0034()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class BadConverter { } // Does NOT implement ITypeConverter

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(BadConverter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0034");
    }

    [Fact]
    public void Generator_ConvertWith_NoParameterlessCtor_EmitsFM0035()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class CtorConverter : ITypeConverter<SourceType, DestType>
{
    private readonly string _prefix;
    public CtorConverter(string prefix) { _prefix = prefix; }
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(CtorConverter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0035");
    }

    [Fact]
    public void Generator_ConvertWith_CombinedWithForgeProperty_EmitsFM0036()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } public string Name { get; set; } }
public class DestType { public int Id { get; set; } public string Label { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(MyConverter))]
    [ForgeProperty(""Name"", ""Label"")]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0036");
    }

    [Fact]
    public void Generator_ConvertWith_MemberNotFound_EmitsFM0037()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(""_nonExistent"")]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0037");
    }

    [Fact]
    public void Generator_ConvertWith_MemberWrongType_EmitsFM0037()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

[ForgeMap]
public partial class TestForger
{
    private readonly string _converter = ""not a converter"";

    [ConvertWith(nameof(_converter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0037");
    }

    [Fact]
    public void Generator_ConvertWith_NullHandling_ThrowException_GeneratesThrow()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap(NullHandling = NullHandling.ThrowException)]
public partial class TestForger
{
    [ConvertWith(typeof(MyConverter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("throw new global::System.ArgumentNullException(nameof(source))", generatedCode);
        Assert.Contains("new global::MyConverter().Convert(source)", generatedCode);
    }

    [Fact]
    public void Generator_ConvertWith_TypeBased_WithoutDi_UsesNewInstantiation()
    {
        // When no DI member exists, converter should be instantiated via new()
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(MyConverter))]
    public partial DestType Forge(SourceType source);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Without DI, new() instantiation is used
        Assert.Contains("new global::MyConverter().Convert(source)", generatedCode);
        // Should NOT contain DI resolution patterns
        Assert.DoesNotContain("GetService", generatedCode);
        Assert.DoesNotContain("GetRequiredService", generatedCode);
    }

    [Fact]
    public void Generator_AfterForgeWithConvertWith_ReportsFM0044()
    {
        var source = @"
using ForgeMap;

public class SourceType { public int Id { get; set; } }
public class DestType { public int Id { get; set; } }

public class MyConverter : ITypeConverter<SourceType, DestType>
{
    public DestType Convert(SourceType source) => new DestType { Id = source.Id };
}

[ForgeMap]
public partial class TestForger
{
    [ConvertWith(typeof(MyConverter))]
    [AfterForge(nameof(PostProcess))]
    public partial DestType Forge(SourceType source);

    private static void PostProcess(SourceType source, DestType dest) { }
}";

        var (diagnostics, _) = RunGenerator(source);

        var error = diagnostics.FirstOrDefault(d => d.Id == "FM0044");
        Assert.NotNull(error);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);

        // Should not emit stray FM0018 for the AfterForge that's already reported as FM0044
        Assert.DoesNotContain(diagnostics, d => d.Id == "FM0018");
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
