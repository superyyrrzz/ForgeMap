namespace ForgeMap;

/// <summary>
/// Specifies how properties should be matched between source and destination types.
/// </summary>
public enum PropertyMatching
{
    /// <summary>
    /// Case-sensitive property name matching.
    /// </summary>
    ByName,

    /// <summary>
    /// Case-insensitive property name matching.
    /// </summary>
    ByNameCaseInsensitive
}
