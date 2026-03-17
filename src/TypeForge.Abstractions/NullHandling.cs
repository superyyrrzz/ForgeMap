namespace TypeForge;

/// <summary>
/// Specifies how null source objects should be handled during forging.
/// </summary>
public enum NullHandling
{
    /// <summary>
    /// Return null when the source object is null.
    /// </summary>
    ReturnNull,

    /// <summary>
    /// Throw an exception when the source object is null.
    /// </summary>
    ThrowException
}
