using System;

namespace ForgeMap;

/// <summary>
/// Inherits attribute-based configuration (<see cref="IgnoreAttribute"/>, <see cref="ForgePropertyAttribute"/>,
/// <see cref="ForgeFromAttribute"/>, <see cref="ForgeWithAttribute"/>) from a base forge method that maps
/// <see cref="BaseSourceType"/> → <see cref="BaseDestinationType"/>.
/// The base forge method must exist in the same forger class.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class IncludeBaseForgeAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="IncludeBaseForgeAttribute"/>.
    /// </summary>
    /// <param name="baseSourceType">The source type of the base forge method to inherit configuration from.</param>
    /// <param name="baseDestinationType">The destination type (return type) of the base forge method to inherit configuration from.</param>
    public IncludeBaseForgeAttribute(Type baseSourceType, Type baseDestinationType)
    {
        BaseSourceType = baseSourceType ?? throw new ArgumentNullException(nameof(baseSourceType));
        BaseDestinationType = baseDestinationType ?? throw new ArgumentNullException(nameof(baseDestinationType));
    }

    /// <summary>
    /// Gets the source type of the base forge method.
    /// </summary>
    public Type BaseSourceType { get; }

    /// <summary>
    /// Gets the destination type (return type) of the base forge method.
    /// </summary>
    public Type BaseDestinationType { get; }
}
