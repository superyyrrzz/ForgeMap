using System;

namespace ForgeMap;

/// <summary>
/// Maps a source property to a differently-named destination property.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ForgePropertyAttribute"/>.
    /// </summary>
    /// <param name="sourceProperty">The name of the source property (can use dot notation for nested paths).</param>
    /// <param name="destinationProperty">The name of the destination property.</param>
    public ForgePropertyAttribute(string sourceProperty, string destinationProperty)
    {
        SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
    }

    /// <summary>
    /// Gets the name of the source property.
    /// </summary>
    public string SourceProperty { get; }

    /// <summary>
    /// Gets the name of the destination property.
    /// </summary>
    public string DestinationProperty { get; }

    /// <summary>
    /// Gets or sets how a nullable source property should be assigned to a non-nullable destination property.
    /// Overrides the forger-level and assembly-level <see cref="NullPropertyHandling"/> for this property.
    /// A value of <c>(NullPropertyHandling)(-1)</c> means "not set" (inherit from forger/assembly).
    /// </summary>
    public NullPropertyHandling NullPropertyHandling { get; set; } = (NullPropertyHandling)(-1);
}
