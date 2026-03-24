using System;

namespace ForgeMap;

/// <summary>
/// Marks a partial class as a ForgeMap forger.
/// The source generator will implement all partial methods in this class.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    /// <summary>
    /// Gets or sets how null source objects should be handled.
    /// Default is <see cref="NullHandling.ReturnNull"/>.
    /// </summary>
    public NullHandling NullHandling { get; set; } = NullHandling.ReturnNull;

    /// <summary>
    /// Gets or sets how properties should be matched between source and destination types.
    /// Default is <see cref="PropertyMatching.ByName"/> (case-sensitive).
    /// </summary>
    public PropertyMatching PropertyMatching { get; set; } = PropertyMatching.ByName;

    /// <summary>
    /// Gets or sets diagnostic IDs to suppress for this forger.
    /// </summary>
    public string[]? SuppressDiagnostics { get; set; }

    /// <summary>
    /// Gets or sets how nullable source properties should be assigned to non-nullable destination properties.
    /// Default is <see cref="NullPropertyHandling.NullForgiving"/>.
    /// </summary>
    public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;
}
