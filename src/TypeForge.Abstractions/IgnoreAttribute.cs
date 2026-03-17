using System;

namespace TypeForge;

/// <summary>
/// Ignores specified destination properties during forging.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class IgnoreAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="IgnoreAttribute"/> with the specified property names.
    /// </summary>
    /// <param name="propertyNames">The names of destination properties to ignore.</param>
    public IgnoreAttribute(params string[] propertyNames)
    {
        PropertyNames = propertyNames ?? throw new ArgumentNullException(nameof(propertyNames));
    }

    /// <summary>
    /// Gets the names of destination properties to ignore.
    /// </summary>
    public string[] PropertyNames { get; }
}
