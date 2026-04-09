using FluentAssertions;
using ForgeMap;
using Xunit;

namespace ForgeMap.Tests;

// ── Source / Dest models for DateTimeOffset→DateTime auto-coercion ──

public class DateTimeOffsetSource
{
    public int Id { get; set; }
    public DateTimeOffset Created { get; set; }
    public DateTimeOffset? Updated { get; set; }
    public DateTimeOffset NonNullToNullable { get; set; }
    public DateTimeOffset? NullableToNonNull { get; set; }
}

public class DateTimeDest
{
    public int Id { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? NonNullToNullable { get; set; }
    public DateTime NullableToNonNull { get; set; }
}

// ── Forger ──

[ForgeMap]
public partial class DateTimeCoercionForger
{
    public partial DateTimeDest Forge(DateTimeOffsetSource source);
}

// ── ForgeProperty path: different property names ──

public class DtoSourceRenamed
{
    public DateTimeOffset SourceCreated { get; set; }
    public DateTimeOffset? SourceUpdated { get; set; }
}

public class DtoDestRenamed
{
    public DateTime DestCreated { get; set; }
    public DateTime? DestUpdated { get; set; }
}

[ForgeMap]
public partial class DateTimeCoercionRenamedForger
{
    [ForgeProperty("SourceCreated", "DestCreated")]
    [ForgeProperty("SourceUpdated", "DestUpdated")]
    public partial DtoDestRenamed Forge(DtoSourceRenamed source);
}

// ── Tests ──

public class AutoCoerceTests
{
    [Fact]
    public void DateTimeOffset_To_DateTime_Convention()
    {
        var forger = new DateTimeCoercionForger();
        var now = DateTimeOffset.UtcNow;

        var source = new DateTimeOffsetSource
        {
            Id = 42,
            Created = now,
            Updated = now.AddHours(1),
            NonNullToNullable = now.AddDays(1),
            NullableToNonNull = now.AddDays(2)
        };

        var result = forger.Forge(source);

        result.Id.Should().Be(42);
        result.Created.Should().Be(now.UtcDateTime);
        result.Updated.Should().Be(now.AddHours(1).UtcDateTime);
        result.NonNullToNullable.Should().Be(now.AddDays(1).UtcDateTime);
        result.NullableToNonNull.Should().Be(now.AddDays(2).UtcDateTime);
    }

    [Fact]
    public void DateTimeOffset_Nullable_Null_To_DateTime_Nullable_Convention()
    {
        var forger = new DateTimeCoercionForger();
        var now = DateTimeOffset.UtcNow;

        var source = new DateTimeOffsetSource
        {
            Id = 1,
            Created = now,
            Updated = null, // nullable source, should map to null dest
            NonNullToNullable = now,
            NullableToNonNull = now
        };

        var result = forger.Forge(source);

        result.Updated.Should().BeNull();
    }

    [Fact]
    public void DateTimeOffset_To_DateTime_ForgeProperty()
    {
        var forger = new DateTimeCoercionRenamedForger();
        var now = DateTimeOffset.UtcNow;

        var source = new DtoSourceRenamed
        {
            SourceCreated = now,
            SourceUpdated = now.AddMinutes(30)
        };

        var result = forger.Forge(source);

        result.DestCreated.Should().Be(now.UtcDateTime);
        result.DestUpdated.Should().Be(now.AddMinutes(30).UtcDateTime);
    }

    [Fact]
    public void DateTimeOffset_ForgeProperty_Nullable_Null()
    {
        var forger = new DateTimeCoercionRenamedForger();
        var now = DateTimeOffset.UtcNow;

        var source = new DtoSourceRenamed
        {
            SourceCreated = now,
            SourceUpdated = null
        };

        var result = forger.Forge(source);

        result.DestCreated.Should().Be(now.UtcDateTime);
        result.DestUpdated.Should().BeNull();
    }

    [Fact]
    public void DateTimeOffset_PreservesUtcConversion()
    {
        var forger = new DateTimeCoercionForger();
        // Create a DateTimeOffset with a non-UTC offset
        var localOffset = new DateTimeOffset(2025, 6, 15, 14, 30, 0, TimeSpan.FromHours(5));

        var source = new DateTimeOffsetSource
        {
            Id = 99,
            Created = localOffset,
            Updated = localOffset,
            NonNullToNullable = localOffset,
            NullableToNonNull = localOffset
        };

        var result = forger.Forge(source);

        // UtcDateTime should convert to UTC (14:30 +05:00 = 09:30 UTC)
        result.Created.Should().Be(localOffset.UtcDateTime);
        result.Created.Kind.Should().Be(DateTimeKind.Utc);
        result.Created.Hour.Should().Be(9);
        result.Created.Minute.Should().Be(30);
    }
}

// ── Generator output verification tests ──

public class AutoCoerceGeneratorTests
{
    [Fact]
    public void Generator_Emits_UtcDateTime_For_DateTimeOffset_To_DateTime()
    {
        var source = @"
using System;
using ForgeMap;

public class Source
{
    public DateTimeOffset Timestamp { get; set; }
}

public class Dest
{
    public DateTime Timestamp { get; set; }
}

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}
";

        var (diagnostics, trees) = TestHelper.RunGenerator(source);

        var generated = trees.FirstOrDefault(t => t.FilePath.Contains("TestForger"));
        generated.Should().NotBeNull("generator should emit code for TestForger");

        var code = generated!.GetText().ToString();
        code.Should().Contain(".UtcDateTime", "DateTimeOffset→DateTime should use .UtcDateTime");
    }

    [Fact]
    public void Generator_Emits_NullConditional_UtcDateTime_For_Nullable_DateTimeOffset_To_Nullable_DateTime()
    {
        var source = @"
using System;
using ForgeMap;

public class Source
{
    public DateTimeOffset? Timestamp { get; set; }
}

public class Dest
{
    public DateTime? Timestamp { get; set; }
}

[ForgeMap]
public partial class TestForger
{
    public partial Dest Forge(Source source);
}
";

        var (diagnostics, trees) = TestHelper.RunGenerator(source);

        var generated = trees.FirstOrDefault(t => t.FilePath.Contains("TestForger"));
        generated.Should().NotBeNull("generator should emit code for TestForger");

        var code = generated!.GetText().ToString();
        code.Should().Contain("?.UtcDateTime", "DateTimeOffset?→DateTime? should use ?.UtcDateTime");
    }
}
