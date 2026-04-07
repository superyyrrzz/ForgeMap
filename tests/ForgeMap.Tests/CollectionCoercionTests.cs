using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;

namespace ForgeMap.Tests;

public class CollectionTypeCoercionTests
{
    [Fact]
    public void Coercion_ListToHashSet_GeneratesNewHashSet()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public List<string> Tags { get; set; } }
    public class Dest { public HashSet<string> Tags { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::System.Collections.Generic.HashSet<string>", generatedCode);
    }

    [Fact]
    public void Coercion_ArrayToList_GeneratesNewList()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public string[] Names { get; set; } }
    public class Dest { public List<string> Names { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::System.Collections.Generic.List<string>", generatedCode);
    }

    [Fact]
    public void Coercion_IEnumerableToArray_GeneratesToArray()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public IEnumerable<int> Values { get; set; } }
    public class Dest { public int[] Values { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("ToArray", generatedCode);
    }

    [Fact]
    public void Coercion_ListToReadOnlyCollection_GeneratesAsReadOnly()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestNamespace
{
    public class Source { public List<string> Items { get; set; } }
    public class Dest { public ReadOnlyCollection<string> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("AsReadOnly()", generatedCode);
    }

    [Fact]
    public void Coercion_ArrayToIReadOnlyList_DirectAssignment()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public string[] Tags { get; set; } }
    public class Dest { public IReadOnlyList<string> Tags { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Array implements IReadOnlyList<T>, so direct assignment (no coercion needed)
        Assert.Contains("source.Tags", generatedCode);
    }

    [Fact]
    public void Coercion_DictionaryToReadOnlyDictionary_PreservesComparer()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestNamespace
{
    public class Source { public Dictionary<string, int> Data { get; set; } }
    public class Dest { public ReadOnlyDictionary<string, int> Data { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("ReadOnlyDictionary", generatedCode);
        Assert.Contains(".Comparer", generatedCode);
    }

    [Fact]
    public void Coercion_IDictionaryToIReadOnlyDictionary_PatternMatch()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public IDictionary<string, int> Data { get; set; } }
    public class Dest { public IReadOnlyDictionary<string, int> Data { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("ReadOnlyDictionary", generatedCode);
        Assert.Contains("is global::System.Collections.Generic.Dictionary<string, int>", generatedCode);
    }

    [Fact]
    public void Coercion_IReadOnlyDictionaryToDictionary_PatternMatch()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public IReadOnlyDictionary<string, int> Data { get; set; } }
    public class Dest { public Dictionary<string, int> Data { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::System.Collections.Generic.Dictionary<string, int>", generatedCode);
    }

    [Fact]
    public void Coercion_DictKeyMismatch_EmitsFM0040()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public Dictionary<int, string> Data { get; set; } }
    public class Dest { public Dictionary<string, string> Data { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, _) = SourceGeneratorTests.RunGenerator(source);
        var fm0040 = diagnostics.Where(d => d.Id == "FM0040").ToList();
        Assert.NotEmpty(fm0040);
    }

    [Fact]
    public void Coercion_WithElementMapping_ListSrcToHashSetDest()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class ItemSrc { public int Id { get; set; } }
    public class ItemDst { public int Id { get; set; } }
    public class Source { public List<ItemSrc> Items { get; set; } }
    public class Dest { public HashSet<ItemDst> Items { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
        public partial ItemDst Forge(ItemSrc source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("HashSet", generatedCode);
    }

    [Fact]
    public void Coercion_NullHandling_CoalesceToDefault_EmptyHashSet()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public List<string> Tags { get; set; } }
    public class Dest { public HashSet<string> Tags { get; set; } }

    [ForgeMap]
    [ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // The coercion generates a HashSet from source, with empty fallback for null
        Assert.Contains("HashSet<string>", generatedCode);
        Assert.DoesNotContain("FM0006", generatedCode); // not unmapped
    }

    [Fact]
    public void Coercion_CtorParam_ListToHashSet()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Source { public List<string> Tags { get; set; } }
    public class Dest
    {
        public Dest(HashSet<string> tags) { Tags = tags; }
        public HashSet<string> Tags { get; }
    }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dest Forge(Source source);
    }
}";
        var (diagnostics, trees) = SourceGeneratorTests.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::System.Collections.Generic.HashSet<string>", generatedCode);
    }
}
