using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;
using static ForgeMap.Tests.TestHelper;

namespace ForgeMap.Tests;

public class ExtractWrapPropertyGeneratorTests
{
    // ---------- [ExtractProperty] ----------

    [Fact]
    public void Extract_ReferenceSource_ReferenceReturn_EmitsNullGuardedReturn()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public int Id { get; set; } public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial string? ForgeScope(ClientScope source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (source == null) return null!;", generated);
        Assert.Contains("return source.Scope;", generated);
    }

    [Fact]
    public void Extract_ReferenceSource_ValueReturn_EmitsDefaultReturn()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public int Id { get; set; } public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Id))]
    public partial int ForgeId(ClientScope source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (source == null) return default;", generated);
        Assert.Contains("return source.Id;", generated);
    }

    [Fact]
    public void Extract_ValueTypeSource_NoNullGuard()
    {
        var source = @"
using ForgeMap;

public readonly struct LabelToken
{
    public LabelToken(string label) { Label = label; }
    public string Label { get; }
}

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(LabelToken.Label))]
    public partial string ForgeLabel(LabelToken source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("if (source == null)", generated);
        Assert.Contains("return source.Label;", generated);
    }

    [Fact]
    public void Extract_DateTimeOffsetToDateTime_AppliesCoercion()
    {
        var source = @"
using System;
using ForgeMap;

public class AuditEntry { public DateTimeOffset At { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(AuditEntry.At))]
    public partial DateTime ForgeAt(AuditEntry source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("UtcDateTime", generated);
    }

    [Fact]
    public void Extract_StringToEnum_AppliesCoercion()
    {
        var source = @"
using ForgeMap;

public enum Color { Red, Green, Blue }
public class Tag { public string? Code { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(Tag.Code))]
    public partial Color ForgeColor(Tag source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("Enum.Parse", generated);
    }

    [Fact]
    public void Extract_PropertyNotFound_EmitsFM0066()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(""DoesNotExist"")]
    public partial string ForgeMissing(ClientScope source);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0066");
    }

    [Fact]
    public void Extract_TypeIncompatible_EmitsFM0067()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public int Id { get; set; } public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial int ForgeBad(ClientScope source);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0067");
    }

    [Fact]
    public void Extract_VoidReturn_NoBodyEmitted_DefersToCS8795()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial void ForgeVoid(ClientScope source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        // Void-returning partials are filtered out before the FM0070 site; the generator
        // emits no body for ForgeVoid, so no implementation appears in generated text.
        // (User then sees CS8795 from the C# compiler — the spec-compliant outcome.)
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("ForgeVoid", generated);
    }

    [Fact]
    public void Extract_TwoParameters_EmitsFM0070()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial string ForgeBadArity(ClientScope source, ClientScope extra);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0070");
    }

    [Fact]
    public void Extract_CombinedWithConvertWith_EmitsFM0065()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    [ConvertWith(""member"")]
    public partial string? ForgeBoth(ClientScope source);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0065");
    }

    // ---------- [WrapProperty] ----------

    [Fact]
    public void Wrap_SettableProperty_EmitsObjectInitializer()
    {
        var source = @"
using ForgeMap;

public class ClientScope { public int Id { get; set; } public string? Scope { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(ClientScope.Scope))]
    public partial ClientScope? ForgeScope(string source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("if (source == null) return null!;", generated);
        Assert.Contains("new global::ClientScope { Scope = source }", generated);
    }

    [Fact]
    public void Wrap_ConstructorOnly_EmitsConstructorCall()
    {
        var source = @"
using ForgeMap;

public class ImmutableScope
{
    public ImmutableScope(string scope) { Scope = scope; }
    public string Scope { get; }
}

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(ImmutableScope.Scope))]
    public partial ImmutableScope? ForgeImmutable(string source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::ImmutableScope(scope: source)", generated);
    }

    [Fact]
    public void Wrap_ValueTypePrimitive_NoNullGuard()
    {
        var source = @"
using ForgeMap;

public class Tag { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tag.Id))]
    public partial Tag ForgeTag(int source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("if (source == null)", generated);
        Assert.Contains("new global::Tag { Id = source }", generated);
    }

    [Fact]
    public void Wrap_PropertyNotFound_EmitsFM0068()
    {
        var source = @"
using ForgeMap;

public class Tag { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(""DoesNotExist"")]
    public partial Tag ForgeMissing(string source);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0068");
    }

    [Fact]
    public void Wrap_TypeIncompatible_EmitsFM0069()
    {
        var source = @"
using ForgeMap;

public class Tag { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tag.Id))]
    public partial Tag ForgeBad(System.Guid source);
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0069");
    }

    [Fact]
    public void Wrap_RequiredMembersBlocking_EmitsFM0071()
    {
        var source = @"
using ForgeMap;

public class Tagged
{
    public required string Scope { get; set; }
    public required string Owner { get; set; }
}

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tagged.Scope))]
    public partial Tagged ForgeTagged(string source);
}";
        var (diagnostics, _) = RunGenerator(source);
        // Owner is required and unsatisfied — FM0071, not FM0068.
        Assert.Contains(diagnostics, d => d.Id == "FM0071");
        Assert.DoesNotContain(diagnostics, d => d.Id == "FM0068");
    }

    [Fact]
    public void Wrap_SetsRequiredMembersOnCtor_TrustsContract()
    {
        var source = @"
using System.Diagnostics.CodeAnalysis;
using ForgeMap;

public class Tagged
{
    [SetsRequiredMembers]
    public Tagged(string scope) { Scope = scope; Owner = """"; }
    public required string Scope { get; set; }
    public required string Owner { get; set; }
}

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tagged.Scope))]
    public partial Tagged ForgeTagged(string source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::Tagged(scope: source)", generated);
    }

    [Fact]
    public void Wrap_VoidReturn_NoBodyEmitted_DefersToCS8795()
    {
        var source = @"
using ForgeMap;

public class Tag { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tag.Id))]
    public partial void ForgeVoid(int source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        // Void-returning partials are filtered out before the FM0070 site; the generator
        // emits no body for ForgeVoid (the C# compiler then reports CS8795).
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.DoesNotContain("ForgeVoid", generated);
    }

    [Fact]
    public void Wrap_CombinedWithForgeFrom_EmitsFM0065()
    {
        var source = @"
using ForgeMap;

public class Tag { public int Id { get; set; } }

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(Tag.Id))]
    [ForgeFrom(nameof(Tag.Id), nameof(Resolve))]
    public partial Tag ForgeBoth(int source);

    private static int Resolve(int s) => s;
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0065");
    }

    [Fact]
    public void Wrap_GetOnlyMember_PreferConstructorPath()
    {
        // Auto + get-only → constructor path.
        var source = @"
using ForgeMap;

public class GetOnly
{
    public GetOnly(string scope) { Scope = scope; }
    public string Scope { get; }
}

[ForgeMap]
public partial class M
{
    [WrapProperty(nameof(GetOnly.Scope))]
    public partial GetOnly? ForgeGetOnly(string source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::GetOnly(scope: source)", generated);
        Assert.DoesNotContain("{ Scope = source }", generated);
    }

    [Fact]
    public void Wrap_PreferParameterless_PicksInitializerEvenWhenCtorExists()
    {
        var source = @"
using ForgeMap;

public class Both
{
    public Both() { }
    public Both(string scope) { Scope = scope; }
    public string Scope { get; set; } = """";
}

[ForgeMap(ConstructorPreference = ConstructorPreference.PreferParameterless)]
public partial class M
{
    [WrapProperty(nameof(Both.Scope))]
    public partial Both? ForgeBoth(string source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::Both { Scope = source }", generated);
        Assert.DoesNotContain("(scope: source)", generated);
    }

    // ---------- Composition ----------

    [Fact]
    public void Extract_AutoWiresIntoParentCollectionMapping()
    {
        // Parent forger maps List<ClientScope> -> List<string>.
        // With [ExtractProperty(\"Scope\")] declared, the parent should auto-wire it as the
        // per-element converter.
        var source = @"
using System.Collections.Generic;
using ForgeMap;

public class ClientScope { public string Scope { get; set; } = """"; }
public class Client { public List<ClientScope> Scopes { get; set; } = new(); }
public class ClientDto { public List<string> Scopes { get; set; } = new(); }

[ForgeMap]
public partial class ClientMapper
{
    public partial ClientDto Forge(Client source);

    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial string ForgeScope(ClientScope source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // The parent should call ForgeScope(__x) per element.
        Assert.Contains("ForgeScope(", generated);
    }

    [Fact]
    public void Extract_DoesNotPropagateThroughReverseForge()
    {
        // [ReverseForge] auto-generation is suppressed for [ExtractProperty] partials.
        // The user must declare [WrapProperty] for the inverse direction.
        var source = @"
using ForgeMap;

public class Tag { public string Name { get; set; } = """"; }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(Tag.Name))]
    [ReverseForge]
    public partial string ForgeName(Tag source);
}";
        var (diagnostics, trees) = RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generated = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // The forward partial must be implemented; the reverse must NOT exist.
        Assert.Contains("partial string ForgeName(global::Tag source)", generated);
        Assert.DoesNotContain("partial global::Tag ForgeName(string", generated);
    }
}
