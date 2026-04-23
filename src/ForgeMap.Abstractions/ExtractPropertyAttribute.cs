using System;

namespace ForgeMap;

/// <summary>
/// Marks a partial forge method that returns a single property of the source object.
/// The method must have signature <c>partial TPrimitive MethodName(TEntity source)</c>.
/// The generator emits a null-guard governed by <c>NullHandling</c>, then returns
/// <c>source.PropertyName</c> (with built-in coercion if needed for the declared return type).
/// Available in ForgeMap v1.7+.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExtractPropertyAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ExtractPropertyAttribute"/>.
    /// </summary>
    /// <param name="propertyName">Name of the readable instance property on the source type to return.</param>
    public ExtractPropertyAttribute(string propertyName)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
    }

    /// <summary>
    /// Gets the name of the source property to extract.
    /// </summary>
    public string PropertyName { get; }
}
