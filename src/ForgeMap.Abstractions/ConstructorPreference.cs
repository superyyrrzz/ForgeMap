namespace ForgeMap;

/// <summary>
/// Controls how the generator selects constructors for destination type instantiation.
/// </summary>
public enum ConstructorPreference
{
    /// <summary>
    /// The generator automatically detects when a parameterized constructor is needed
    /// to map get-only properties. If all destination properties are settable, the
    /// parameterless constructor is preferred. If any get-only properties have matching
    /// source properties, the best-matching parameterized constructor is selected.
    /// Default behavior.
    /// </summary>
    Auto,

    /// <summary>
    /// Always prefer the parameterless constructor when available (v1.5 behavior).
    /// Get-only properties remain unmapped.
    /// </summary>
    PreferParameterless
}
