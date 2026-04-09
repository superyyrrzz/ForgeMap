namespace ForgeMap;

/// <summary>
/// Controls automatic string-to-enum conversion behavior for property mappings.
/// </summary>
public enum StringToEnumConversion
{
    /// <summary>Use Enum.Parse with null/empty string guard (returns default(T) for null/empty). Default.</summary>
    Parse,

    /// <summary>Use Enum.TryParse (falls back to default(T) on failure).</summary>
    TryParse,

    /// <summary>Do not auto-convert; require explicit [ForgeFrom] resolver.</summary>
    None,

    /// <summary>Use Enum.Parse without null/empty guard (throws on null/empty values). Legacy behavior.</summary>
    StrictParse
}
