// Test scenario: Nullable<T> source parameter where T has a property
using ForgeMap;

public struct MyStruct
{
    public int Data { get; set; }
}

[ForgeMap]
public partial class M
{
    [ExtractProperty("Data")]
    public partial int Extract(MyStruct? source);
}
