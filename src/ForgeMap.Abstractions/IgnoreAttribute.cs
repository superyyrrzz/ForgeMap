using System;

namespace ForgeMap;

/// <summary>
/// Ignores specified destination properties during forging.
/// </summary>
/// <remarks>
/// Use this attribute to prevent over-posting or mass assignment when mapping from untrusted
/// sources. For example, <c>[Ignore("IsAdmin", "Role")]</c> ensures those destination
/// properties are not set by the generated mapping code for that annotated forge method.
/// </remarks>
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
