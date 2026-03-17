using System;

namespace TypeForge;

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
}
