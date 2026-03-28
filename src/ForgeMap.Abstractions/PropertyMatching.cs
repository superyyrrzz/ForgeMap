namespace ForgeMap;

/// <summary>
/// Specifies how properties should be matched between source and destination types.
/// </summary>
public enum PropertyMatching
{
    /// <summary>
    /// Case-sensitive property name matching. Assignable destination properties with a
    /// name-matched, readable source property are mapped automatically.
    /// Use <see cref="IgnoreAttribute"/> to exclude security-sensitive properties when
    /// mapping from untrusted sources.
    /// </summary>
    ByName,

    /// <summary>
    /// Case-insensitive property name matching.
    /// </summary>
    ByNameCaseInsensitive
}
