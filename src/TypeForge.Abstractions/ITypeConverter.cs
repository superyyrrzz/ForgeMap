namespace TypeForge;

/// <summary>
/// Interface for custom type converters.
/// Implement this for complex type transformations that cannot be expressed with <see cref="ForgeFromAttribute"/>.
/// </summary>
/// <typeparam name="TSource">The source type to convert from.</typeparam>
/// <typeparam name="TDestination">The destination type to convert to.</typeparam>
public interface ITypeConverter<in TSource, out TDestination>
{
    /// <summary>
    /// Converts a source object to a destination object.
    /// </summary>
    /// <param name="source">The source object to convert. Guaranteed to be non-null when called through generated TypeForge code.</param>
    /// <returns>The converted destination object.</returns>
    TDestination Convert(TSource source);
}
