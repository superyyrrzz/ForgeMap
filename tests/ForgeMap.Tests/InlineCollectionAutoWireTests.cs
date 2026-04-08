using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class InlineCollectionAutoWireTests
{
    [Fact]
    public void InlineCollectionAutoWire_ListToList_GeneratesForeachAdd()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new global::System.Collections.Generic.List<", generatedCode);
        Assert.Contains("foreach", generatedCode);
        Assert.Contains(".Add(Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ArrayToArray_GeneratesArrayConvertAll()
    {
        var source = @"
using ForgeMap;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public ItemSource[] Items { get; set; } }
    public class ParentDest { public ItemDest[] Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("global::System.Array.ConvertAll(", generatedCode);
        Assert.Contains("Forge(__collItem)", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_IEnumerableToIEnumerable_GeneratesSelect()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public IEnumerable<ItemSource> Items { get; set; } }
    public class ParentDest { public IEnumerable<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(".Select(__collItem => Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_HashSetToHashSet_GeneratesForeachAdd()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public HashSet<ItemSource> Items { get; set; } }
    public class ParentDest { public HashSet<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new global::System.Collections.Generic.HashSet<", generatedCode);
        Assert.Contains("foreach", generatedCode);
        Assert.Contains(".Add(Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ExplicitCollectionMethod_TakesPrecedence()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial List<ItemDest> Forge(List<ItemSource> source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__autoWire_Items", generatedCode);
        Assert.DoesNotContain("__collItem", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_IgnoredProperty_NoInlineGenerated()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);

        [Ignore(""Items"")]
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // Ignored property should not generate inline collection code
        Assert.DoesNotContain("__collItem", generatedCode);
        Assert.DoesNotContain("__collResult_", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_DisabledByAutoWireNestedMappingsFalse()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap(AutoWireNestedMappings = false)]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.DoesNotContain("__collItem", generatedCode);
        Assert.DoesNotContain("__collResult_", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_NoElementForgeMethod_FallsThrough()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public string Name { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.DoesNotContain("__collItem", generatedCode);
        Assert.DoesNotContain("__collResult_", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ListToList_PreSizedWhenSourceHasCheapCount()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (_, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Contains(".Count)", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_IEnumerableToList_NotPreSized()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public IEnumerable<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new global::System.Collections.Generic.List<", generatedCode);
        Assert.Contains("foreach", generatedCode);
        Assert.DoesNotContain(".Count)", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_MixedTypes_IEnumerableToArray()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public IEnumerable<ItemSource> Items { get; set; } }
    public class ParentDest { public ItemDest[] Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        // IEnumerable<T> → U[] uses Select + ToArray (single expression) to avoid double enumeration
        Assert.Contains("ToArray", generatedCode);
        Assert.Contains("Select", generatedCode);
        Assert.Contains("Forge(__collItem)", generatedCode);
        Assert.DoesNotContain("__collIdx_", generatedCode); // No indexed loop for IEnumerable
    }

    [Fact]
    public void InlineCollectionAutoWire_MixedTypes_ListToIReadOnlyList()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public IReadOnlyList<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new global::System.Collections.Generic.List<", generatedCode);
        Assert.Contains(".Add(Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_NullPropertyHandling_SkipNull()
    {
        var source = @"
#nullable enable
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource>? Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("if (source.Items is { }", generatedCode);
        Assert.Contains("foreach", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_NullPropertyHandling_CoalesceToDefault()
    {
        var source = @"
#nullable enable
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource>? Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("new global::System.Collections.Generic.List<", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_NullPropertyHandling_ThrowException()
    {
        var source = @"
#nullable enable
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource>? Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap(NullPropertyHandling = NullPropertyHandling.ThrowException)]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("ArgumentNullException", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ForgeInto_GeneratesStatementBlock()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial void ForgeInto(ParentSource source, [UseExistingValue] ParentDest destination);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("destination.Items", generatedCode);
        Assert.Contains("foreach", generatedCode);
        Assert.Contains(".Add(Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_FM0025_AmbiguousElementMethods()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ItemDest ForgeAlt(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0025" && d.GetMessage().Contains("Items"));
    }

    [Fact]
    public void InlineCollectionAutoWire_FM0026_ReverseForgeWarning()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);

        [ReverseForge]
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, _) = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "FM0026" && d.GetMessage().Contains("Items"));
    }

    [Fact]
    public void InlineCollectionAutoWire_ReverseForge_WithReverseElementMethod_NoWarning()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public List<ItemDest> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ItemSource Forge(ItemDest source);

        [ReverseForge]
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Id == "FM0026"));
        Assert.Contains("foreach", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ConstructorParameter()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest
    {
        public ParentDest(List<ItemDest> items) { Items = items; }
        public List<ItemDest> Items { get; }
    }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("foreach", generatedCode);
        Assert.Contains("Forge(__collItem)", generatedCode);
        Assert.Contains("new TestNamespace.ParentDest(", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ForgeProperty_MappedCollection()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> SourceItems { get; set; } }
    public class ParentDest { public List<ItemDest> DestItems { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);

        [ForgeProperty(""SourceItems"", ""DestItems"")]
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("source.SourceItems", generatedCode);
        Assert.Contains("foreach", generatedCode);
        Assert.Contains(".Add(Forge(__collItem))", generatedCode);
    }

    [Fact]
    public void InlineCollectionAutoWire_ListToArray_GeneratesIndexedLoop()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSource { public int Id { get; set; } }
    public class ItemDest { public int Id { get; set; } }
    public class ParentSource { public List<ItemSource> Items { get; set; } }
    public class ParentDest { public ItemDest[] Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial ItemDest Forge(ItemSource source);
        public partial ParentDest Forge(ParentSource source);
    }
}";
        var (diagnostics, trees) = RunGenerator(source);
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("__collIdx_Items", generatedCode);
        Assert.Contains("Forge(__collItem)", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
