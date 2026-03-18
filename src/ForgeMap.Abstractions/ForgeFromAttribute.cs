using System;

namespace ForgeMap;

/// <summary>
/// Maps a destination property using a custom resolver method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgeFromAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ForgeFromAttribute"/>.
    /// </summary>
    /// <param name="destinationProperty">The name of the destination property.</param>
    /// <param name="resolverMethodName">The name of the resolver method to call.</param>
    public ForgeFromAttribute(string destinationProperty, string resolverMethodName)
    {
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
        ResolverMethodName = resolverMethodName ?? throw new ArgumentNullException(nameof(resolverMethodName));
    }

    /// <summary>
    /// Gets the name of the destination property.
    /// </summary>
    public string DestinationProperty { get; }

    /// <summary>
    /// Gets the name of the resolver method to call.
    /// </summary>
    public string ResolverMethodName { get; }
}
