using ForgeMap;
using System;

public class Source { public int Value { get; set; } }

[ForgeMap]
public partial class M
{
    [ExtractProperty(nameof(Source.Value))]
    public partial int ForgeValue(int? source);
}
