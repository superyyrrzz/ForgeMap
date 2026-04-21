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

    /// <summary>
    /// Specifies a static or instance method on the forger class to use for converting
    /// this individual property's value. The method must accept the source property type
    /// and return the destination property type (e.g., <c>TDest MethodName(TSource value)</c>).
    /// When set, this takes precedence over default assignment for this property only.
    /// </summary>
    public string? ConvertWith { get; set; }

    /// <summary>
    /// Specifies a type that implements <see cref="ITypeConverter{TSource, TDestination}"/>
    /// to use for converting this individual property's value.
    /// The type must have an accessible parameterless constructor.
    /// When set, this takes precedence over default assignment for this property only.
    /// Cannot be combined with <see cref="ConvertWith"/> (method name).
    /// </summary>
    public Type? ConvertWithType { get; set; }

    /// <summary>
    /// Name of a property on the source collection's element type to project.
    /// When set, generates a LINQ <c>Select</c> over the source collection that reads the named member,
    /// then materializes the result into the destination wrapper:
    /// <c>List&lt;T&gt;</c>/<c>IList&lt;T&gt;</c>/<c>ICollection&lt;T&gt;</c>/<c>IReadOnlyList&lt;T&gt;</c>/<c>IReadOnlyCollection&lt;T&gt;</c> via <c>ToList()</c>;
    /// arrays via <c>ToArray()</c>; <c>HashSet&lt;T&gt;</c> via the set constructor; <c>ReadOnlyCollection&lt;T&gt;</c> by wrapping a list;
    /// or returned as <c>IEnumerable&lt;T&gt;</c> directly.
    /// Built-in element coercions (enum cast, string↔enum, enum→string, <c>DateTimeOffset</c>→<c>DateTime</c>, <c>Nullable&lt;T&gt;</c> unwrap) are composed into the lambda.
    /// Use <c>nameof()</c> for compile-time safety.
    /// Mutually exclusive with <see cref="ConvertWith"/> and <see cref="ConvertWithType"/> on the same <c>[ForgeProperty]</c>.
    /// </summary>
    public string? SelectProperty { get; set; }
}
