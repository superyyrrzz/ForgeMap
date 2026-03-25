using System;

namespace ForgeMap;

/// <summary>
/// Assembly-level defaults for all forgers in the assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the default null handling mode for all forgers in the assembly.
    /// </summary>
    public NullHandling NullHandling { get; set; } = NullHandling.ReturnNull;

    /// <summary>
    /// Gets or sets whether to automatically generate collection mapping methods.
    /// Default is true.
    /// </summary>
    public bool GenerateCollectionMappings { get; set; } = true;

    /// <summary>
    /// Gets or sets the default property matching mode for all forgers in the assembly.
    /// </summary>
    public PropertyMatching PropertyMatching { get; set; } = PropertyMatching.ByName;

    /// <summary>
    /// Gets or sets the default null property handling mode for all forgers in the assembly.
    /// Default is <see cref="NullPropertyHandling.NullForgiving"/>.
    /// </summary>
    public NullPropertyHandling NullPropertyHandling { get; set; } = NullPropertyHandling.NullForgiving;

    /// <summary>
    /// Gets or sets whether auto-discovery of nested forge methods is enabled by default.
    /// Default is true.
    /// </summary>
    public bool AutoWireNestedMappings { get; set; } = true;
}
