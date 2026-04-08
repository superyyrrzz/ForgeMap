using Microsoft.CodeAnalysis;
using Xunit;

namespace ForgeMap.Tests;

public class StandaloneCollectionMethodTests
{
    [Fact]
    public void Basic_IReadOnlyList_Return_GeneratesForeachList()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial IReadOnlyList<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Should generate foreach+List pattern
        Assert.Contains("ForgeDst(item)", generatedCode);
        Assert.Contains("new global::System.Collections.Generic.List<TestNamespace.Dst>()", generatedCode);
        Assert.Contains("result.Add(", generatedCode);
    }

    [Fact]
    public void ArrayReturn_WithCheapCount_GeneratesPreSizedArray()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial Dst[] ForgeDsts(IReadOnlyList<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Pre-sized array with indexed assignment
        Assert.Contains("source.Count", generatedCode);
        Assert.Contains("result[i++] = ForgeDst(item)", generatedCode);
    }

    [Fact]
    public void ArrayReturn_WithIEnumerableSource_GeneratesSelectToArray()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial Dst[] ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // IEnumerable source → Select+ToArray fallback
        Assert.Contains("ForgeDst(item)", generatedCode);
        Assert.Contains(".ToArray()", generatedCode);
    }

    [Fact]
    public void ListReturn_WithArraySource_GeneratesPreSizedList()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial List<Dst> ForgeDstList(Src[] source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Pre-sized List from array source
        Assert.Contains("source.Length", generatedCode);
        Assert.Contains("ForgeDst(item)", generatedCode);
    }

    [Fact]
    public void IEnumerableReturn_GeneratesLazySelect()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial IEnumerable<Dst> ForgeDstsLazy(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Lazy Select projection
        Assert.Contains("Select(item => ForgeDst(item))", generatedCode);
        // Should NOT contain foreach or .Add
        Assert.DoesNotContain("foreach", generatedCode.Split("ForgeDstsLazy")[1].Split("}")[0]);
    }

    [Fact]
    public void HashSetReturn_GeneratesForeachAdd()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial HashSet<Dst> ForgeDstSet(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("new global::System.Collections.Generic.HashSet<TestNamespace.Dst>()", generatedCode);
        Assert.Contains("result.Add(ForgeDst(item))", generatedCode);
    }

    [Fact]
    public void DifferentMethodNames_MatchesByType()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        // Element method is named ForgeItem
        public partial Dst ForgeItem(Src source);
        // Collection method is named ForgeAll — different name, same type pair
        public partial IReadOnlyList<Dst> ForgeAll(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Must call ForgeItem (discovered by type), not ForgeAll (by name)
        Assert.Contains("ForgeItem(item)", generatedCode);
    }

    [Fact]
    public void NoElementMethod_EmitsFM0041()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        // No element method declared!
        public partial IReadOnlyList<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, _) = TestHelper.RunGenerator(source);
        var fm0041 = diagnostics.Where(d => d.Id == "FM0041").ToList();
        Assert.NotEmpty(fm0041);
        Assert.Contains("ForgeDsts", fm0041[0].GetMessage());
    }

    [Fact]
    public void AmbiguousElementMethods_EmitsFM0042()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        // Two element methods with the same type pair
        public partial Dst ForgeOne(Src source);
        public partial Dst ForgeTwo(Src source);
        public partial IReadOnlyList<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, _) = TestHelper.RunGenerator(source);
        var fm0042 = diagnostics.Where(d => d.Id == "FM0042").ToList();
        Assert.NotEmpty(fm0042);
        Assert.Contains("ForgeDsts", fm0042[0].GetMessage());
    }

    [Fact]
    public void NullHandling_ThrowException_GeneratesThrow()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap(NullHandling = NullHandling.ThrowException)]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial IReadOnlyList<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("throw new global::System.ArgumentNullException", generatedCode);
    }

    [Fact]
    public void ConvertWithOnCollectionMethod_TakesPrecedence()
    {
        // When [ConvertWith] is on the collection method, it should be handled by
        // ConvertWith logic, not the standalone collection method logic.
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }
    public class MyConverter : ITypeConverter<IEnumerable<Src>, IReadOnlyList<Dst>>
    {
        public IReadOnlyList<Dst> Convert(IEnumerable<Src> source) => new List<Dst>();
    }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        [ConvertWith(typeof(MyConverter))]
        public partial IReadOnlyList<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Should use converter, not element method iteration
        Assert.Contains("MyConverter", generatedCode);
    }

    [Fact]
    public void ReadOnlyCollectionReturn_GeneratesListAndAsReadOnly()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial ReadOnlyCollection<Dst> ForgeDsts(List<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        Assert.Contains("AsReadOnly()", generatedCode);
        Assert.Contains("ForgeDst(item)", generatedCode);
        // Pre-sized from List source
        Assert.Contains("source.Count", generatedCode);
    }

    [Fact]
    public void NullHandling_ReturnNull_GeneratesNullReturn()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial List<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Default NullHandling is ReturnNull
        Assert.Contains("return null!", generatedCode);
    }

    [Fact]
    public void PreSized_FromListSource_UsesCount()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial List<Dst> ForgeDsts(List<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // Pre-sized list from List source
        Assert.Contains("new global::System.Collections.Generic.List<TestNamespace.Dst>(source.Count)", generatedCode);
    }

    [Fact]
    public void NoPreSizing_FromIEnumerableSource()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

namespace TestNamespace
{
    public class Src { public int Id { get; set; } }
    public class Dst { public int Id { get; set; } }

    [ForgeMap]
    public partial class TestForger
    {
        public partial Dst ForgeDst(Src source);
        public partial List<Dst> ForgeDsts(IEnumerable<Src> source);
    }
}";
        var (diagnostics, trees) = TestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));
        // No pre-sizing — IEnumerable has no cheap count
        Assert.Contains("new global::System.Collections.Generic.List<TestNamespace.Dst>()", generatedCode);
        Assert.DoesNotContain("source.Count", generatedCode);
    }
}
