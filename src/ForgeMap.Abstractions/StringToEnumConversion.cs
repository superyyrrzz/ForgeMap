namespace ForgeMap;

/// <summary>
/// Controls automatic string-to-enum conversion behavior for property mappings.
/// </summary>
public enum StringToEnumConversion
{
    /// <summary>Use Enum.Parse (throws on invalid values). Default.</summary>
    Parse,

    /// <summary>Use Enum.TryParse (falls back to default(T) on failure).</summary>
    TryParse,

    /// <summary>Do not auto-convert; require explicit [ForgeFrom] resolver.</summary>
    None
}
