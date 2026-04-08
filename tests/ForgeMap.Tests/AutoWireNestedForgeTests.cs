using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class AutoWireNestedForgeTests
{
    [Fact]
    public void AutoWire_BasicNestedProperty_GeneratesForgeCall()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // The outer Forge should auto-wire Inner via the inner Forge method using __autoWire_ pattern
        Assert.Contains("__autoWire_Inner", generatedCode);
        Assert.Contains("Forge(__autoWire_Inner)", generatedCode);
    }

    [Fact]
    public void AutoWire_OptOut_DoesNotAutoWire()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap(AutoWireNestedMappings = false)]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // With auto-wire disabled, Inner should NOT be auto-wired — no Forge call for Inner
        // The property is simply omitted from the initializer (generator doesn't emit FM0006)
        Assert.DoesNotContain("__autoWire_Inner", generatedCode);
        // But the inner Forge method should still generate for InnerSource -> InnerDest
        Assert.Contains("Value = source.Value", generatedCode);
    }

    [Fact]
    public void AutoWire_ExplicitForgeWithTakesPrecedence()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial InnerDest CustomInnerForge(InnerSource source);

        [ForgeWith(""Inner"", nameof(CustomInnerForge))]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should use explicit ForgeWith (CustomInnerForge) via __forgeWith_ pattern, not __autoWire_
        Assert.Contains("CustomInnerForge(__forgeWith_Inner)", generatedCode);
        Assert.DoesNotContain("__autoWire_Inner", generatedCode);
        // No FM0025 ambiguity — explicit wins
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0025"));
    }

    [Fact]
    public void AutoWire_AmbiguousMultipleMethods_FM0025()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial InnerDest ForgeAlternate(InnerSource source);
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        // Should get FM0025 for ambiguous auto-wire
        Assert.Contains(diagnostics, d => d.Id == "FM0025" && d.GetMessage().Contains("Inner"));
    }

    [Fact]
    public void AutoWire_ScalarTypes_NotAutoWired()
    {
        // Scalar types (int, string, enum) should never trigger auto-wiring
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class Source { public int Value { get; set; } public string Name { get; set; } }
    public class Dest { public int Value { get; set; } public string Name { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should directly assign, not auto-wire
        Assert.Contains("Value = source.Value", generatedCode);
        Assert.Contains("Name = source.Name", generatedCode);
        // No FM0025 ambiguity warnings
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0025"));
    }

    [Fact]
    public void AutoWire_DirectlyAssignable_NotAutoWired()
    {
        // If types are directly assignable, auto-wiring should not kick in
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class Shared { public int Value { get; set; } }
    public class Source { public Shared Inner { get; set; } }
    public class Dest { public Shared Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should directly assign since types are the same
        Assert.Contains("Inner = source.Inner", generatedCode);
    }

    [Fact]
    public void AutoWire_AssemblyLevelOptOut_DisablesAutoWiring()
    {
        var source = @"
using ForgeMap;

[assembly: ForgeMapDefaults(AutoWireNestedMappings = false)]

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // With auto-wire disabled at assembly level, Inner should NOT be auto-wired
        Assert.DoesNotContain("__autoWire_Inner", generatedCode);
    }

    [Fact]
    public void AutoWire_MultipleNestedProperties_AllAutoWired()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class AddressEntity { public string Street { get; set; } }
    public class AddressDto { public string Street { get; set; } }
    public class PhoneEntity { public string Number { get; set; } }
    public class PhoneDto { public string Number { get; set; } }
    public class PersonEntity { public AddressEntity Address { get; set; } public PhoneEntity Phone { get; set; } }
    public class PersonDto { public AddressDto Address { get; set; } public PhoneDto Phone { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial AddressDto Forge(AddressEntity source);
        public partial PhoneDto Forge(PhoneEntity source);
        public partial PersonDto Forge(PersonEntity source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Both Address and Phone should be auto-wired via __autoWire_ pattern
        Assert.Contains("__autoWire_Address", generatedCode);
        Assert.Contains("Forge(__autoWire_Address)", generatedCode);
        Assert.Contains("__autoWire_Phone", generatedCode);
        Assert.Contains("Forge(__autoWire_Phone)", generatedCode);
    }

    [Fact]
    public void AutoWire_NoMatchingMethod_FallsThrough()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        // No Forge method for InnerSource -> InnerDest
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // No matching forge method exists — Inner should NOT be auto-wired
        Assert.DoesNotContain("__autoWire_Inner", generatedCode);
    }

    [Fact]
    public void AutoWire_ReverseForge_FM0026_WhenNoReverseMethod()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        // No InnerSource Forge(InnerDest source) reverse method

        [ReverseForge]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        // Should get FM0026 because auto-wired property Inner has no reverse forge method
        Assert.Contains(diagnostics, d => d.Id == "FM0026" && d.GetMessage().Contains("Inner"));
    }

    [Fact]
    public void AutoWire_ReverseForge_NoFM0026_WhenReverseMethodExists()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial InnerSource Forge(InnerDest source);

        [ReverseForge]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        // Should NOT get FM0026 because reverse forge method exists
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0026"));
    }

    [Fact]
    public void AutoWire_ReverseForge_NoFM0026_WhenInnerHasReverseForge()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        [ReverseForge]
        public partial InnerDest Forge(InnerSource source);

        [ReverseForge]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        // Inner forge method has [ReverseForge], so reverse auto-wire should be possible
        // Should NOT get FM0026
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0026"));
    }

    [Fact]
    public void AutoWire_ForgePropertyDotPath_AutoWiresLeafType()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class MiddleSource { public InnerSource Nested { get; set; } }
    public class OuterSource { public MiddleSource Middle { get; set; } }
    public class OuterDest
    {
        public InnerDest DeepNested { get; set; }
    }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);

        [ForgeProperty(""Middle.Nested"", ""DeepNested"")]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should auto-wire the dot-path leaf type InnerSource -> InnerDest via __autoWire_ pattern
        Assert.Contains("__autoWire_DeepNested", generatedCode);
        Assert.Contains("Forge(__autoWire_DeepNested)", generatedCode);
    }

    [Fact]
    public void AutoWire_ConstructorParameter_GeneratesForgeCall()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest
    {
        public OuterDest(InnerDest inner) { Inner = inner; }
        public InnerDest Inner { get; }
    }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Constructor parameter should be auto-wired via forge method
        Assert.Contains("__autoWire_inner", generatedCode);
        Assert.Contains("Forge(__autoWire_inner)", generatedCode);
        // No FM0014 (unmatched ctor param) should be emitted
        Assert.Empty(diagnostics.Where(d => d.Id == "FM0014"));
    }

    [Fact]
    public void AutoWire_ForgeInto_GeneratesForgeCall()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public int Value { get; set; } }
    public class OuterSource { public InnerSource Inner { get; set; } }
    public class OuterDest { public InnerDest Inner { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial InnerDest Forge(InnerSource source);
        public partial void Forge(OuterSource source, [UseExistingValue] OuterDest dest);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // ForgeInto should auto-wire Inner via the forge method
        Assert.Contains("__autoWire_Inner", generatedCode);
    }

    [Fact]
    public void AutoWire_DotPath_NonAssignableLeaf_FallsThrough()
    {
        // When [ForgeProperty] maps to a leaf type that isn't assignable and
        // no forge method matches, the property should be unmapped (FM0006), not emit invalid code
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class InnerSource { public int Value { get; set; } }
    public class InnerDest { public string Label { get; set; } }
    public class OuterSource
    {
        public InnerSource Nested { get; set; }
    }
    public class OuterDest
    {
        public InnerDest Deep { get; set; }
    }

    [ForgeMap]
    public partial class TestForger
    {
        // No InnerSource -> InnerDest forge method exists
        [ForgeProperty(""Nested"", ""Deep"")]
        public partial OuterDest Forge(OuterSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // No auto-wire pattern should appear since no matching forge method exists
        Assert.DoesNotContain("__autoWire_Deep", generatedCode);
        // The property should NOT be assigned with incompatible types (no "source.Nested" direct assignment)
        Assert.DoesNotContain("Deep = source.Nested", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
