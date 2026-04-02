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

    /// <summary>
    /// When true, the destination property's existing value is updated in place
    /// rather than replaced with a new instance. Requires the destination property
    /// to be a readable reference-type property. Runtime null values on the destination
    /// are handled according to the configured <see cref="NullPropertyHandling"/> (skip/coalesce/throw).
    /// Used with <see cref="UseExistingValueAttribute"/> mutation methods to preserve object identity
    /// (e.g., EF Core change tracking). Default is false.
    /// </summary>
    public bool ExistingTarget { get; set; }

    /// <summary>
    /// Specifies how a collection property is updated when <see cref="ExistingTarget"/> is true.
    /// Ignored when <see cref="ExistingTarget"/> is false or the property is not a collection type.
    /// Default is <see cref="CollectionUpdateStrategy.Replace"/>.
    /// </summary>
    public CollectionUpdateStrategy CollectionUpdate { get; set; }

    /// <summary>
    /// The property name used as a matching key for <see cref="CollectionUpdateStrategy.Sync"/>.
    /// Both source and destination element types must have a property with this name.
    /// Required when <see cref="CollectionUpdate"/> is <see cref="CollectionUpdateStrategy.Sync"/>.
    /// </summary>
    public string? KeyProperty { get; set; }
}
