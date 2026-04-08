using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ForgeMap.Generator;
using Xunit;
using System.Diagnostics;

namespace ForgeMap.Tests;

public class ExistingTargetGeneratorTests
{
    [Fact]
    public void Generator_ExistingTarget_GeneratesNestedForgeIntoCall()
    {
        var source = @"
using ForgeMap;

public class CustomerDto { public string Name { get; set; } }
public class Customer { public string Name { get; set; } }
public class OrderDto
{
    public string Status { get; set; }
    public CustomerDto Customer { get; set; }
}
public class Order
{
    public string Status { get; set; }
    public Customer Customer { get; set; }
}

[ForgeMap]
public partial class TestForger
{
    public partial void ForgeInto(CustomerDto source, [UseExistingValue] Customer target);

    [ForgeProperty(""Customer"", ""Customer"", ExistingTarget = true)]
    public partial void ForgeInto(OrderDto source, [UseExistingValue] Order target);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should generate nested ForgeInto call pattern
        Assert.Contains("__src_Customer", generatedCode);
        Assert.Contains("__tgt_Customer", generatedCode);
        Assert.Contains("ForgeInto(__src_Customer, __tgt_Customer)", generatedCode);
    }

    [Fact]
    public void Generator_ExistingTarget_OnNonMutationMethod_EmitsFM0028()
    {
        var source = @"
using ForgeMap;

public class CustomerDto { public string Name { get; set; } }
public class Customer { public string Name { get; set; } }
public class OrderDto { public string Status { get; set; } public CustomerDto Customer { get; set; } }
public class Order { public string Status { get; set; } public Customer Customer { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(""Customer"", ""Customer"", ExistingTarget = true)]
    public partial OrderDto Forge(Order source);
}";

        var (diagnostics, _) = RunGenerator(source);
        var fm0028 = diagnostics.Where(d => d.Id == "FM0028").ToList();
        Assert.NotEmpty(fm0028);
    }

    [Fact]
    public void Generator_ExistingTarget_NoMatchingForgeInto_EmitsFM0030()
    {
        var source = @"
using ForgeMap;

public class CustomerDto { public string Name { get; set; } }
public class Customer { public string Name { get; set; } }
public class OrderDto { public string Status { get; set; } public CustomerDto Customer { get; set; } }
public class Order { public string Status { get; set; } public Customer Customer { get; set; } }

[ForgeMap]
public partial class TestForger
{
    // No ForgeInto for CustomerDto -> Customer
    [ForgeProperty(""Customer"", ""Customer"", ExistingTarget = true)]
    public partial void ForgeInto(OrderDto source, [UseExistingValue] Order target);
}";

        var (diagnostics, _) = RunGenerator(source);
        var fm0030 = diagnostics.Where(d => d.Id == "FM0030").ToList();
        Assert.NotEmpty(fm0030);
    }

    [Fact]
    public void Generator_CollectionSync_WithoutKeyProperty_EmitsFM0031()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

public class ItemDto { public int Id { get; set; } public string Name { get; set; } }
public class Item { public int Id { get; set; } public string Name { get; set; } }
public class OrderDto { public List<ItemDto> Items { get; set; } }
public class Order { public List<Item> Items { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(""Items"", ""Items"", ExistingTarget = true, CollectionUpdate = CollectionUpdateStrategy.Sync)]
    public partial void ForgeInto(OrderDto source, [UseExistingValue] Order target);
}";

        var (diagnostics, _) = RunGenerator(source);
        var fm0031 = diagnostics.Where(d => d.Id == "FM0031").ToList();
        Assert.NotEmpty(fm0031);
    }

    [Fact]
    public void Generator_CollectionSync_WithInvalidKeyProperty_EmitsFM0032()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

public class ItemDto { public int Id { get; set; } public string Name { get; set; } }
public class Item { public int Id { get; set; } public string Name { get; set; } }
public class OrderDto { public List<ItemDto> Items { get; set; } }
public class Order { public List<Item> Items { get; set; } }

[ForgeMap]
public partial class TestForger
{
    [ForgeProperty(""Items"", ""Items"", ExistingTarget = true,
        CollectionUpdate = CollectionUpdateStrategy.Sync, KeyProperty = ""NonExistent"")]
    public partial void ForgeInto(OrderDto source, [UseExistingValue] Order target);
}";

        var (diagnostics, _) = RunGenerator(source);
        var fm0032 = diagnostics.Where(d => d.Id == "FM0032").ToList();
        Assert.NotEmpty(fm0032);
    }

    [Fact]
    public void Generator_CollectionSync_GeneratesDictionaryBasedLoop()
    {
        var source = @"
using ForgeMap;
using System.Collections.Generic;

public class ItemDto { public int Id { get; set; } public string Name { get; set; } }
public class Item { public int Id { get; set; } public string Name { get; set; } }
public class OrderDto { public string Status { get; set; } public List<ItemDto> Items { get; set; } }
public class Order { public string Status { get; set; } public List<Item> Items { get; set; } }

[ForgeMap]
public partial class TestForger
{
    public partial void ForgeInto(ItemDto source, [UseExistingValue] Item target);
    public partial Item Forge(ItemDto source);

    [ForgeProperty(""Items"", ""Items"", ExistingTarget = true,
        CollectionUpdate = CollectionUpdateStrategy.Sync, KeyProperty = ""Id"")]
    public partial void ForgeInto(OrderDto source, [UseExistingValue] Order target);
}";

        var (diagnostics, trees) = RunGenerator(source);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);

        var generatedCode = string.Join("\n", trees.Select(t => t.GetText().ToString()));

        // Should generate dictionary-based sync loop
        Assert.Contains("__existing", generatedCode);
        Assert.Contains("__matched", generatedCode);
        Assert.Contains("TryGetValue", generatedCode);
        Assert.Contains("RemoveAll", generatedCode);
    }

    private static (IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<SyntaxTree> GeneratedTrees) RunGenerator(string source)
    {
        return TestHelper.RunGenerator(source);
    }
}
