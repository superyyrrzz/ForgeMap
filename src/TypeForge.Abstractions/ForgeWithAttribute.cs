using System;

namespace TypeForge;

/// <summary>
/// Uses another forging method for a nested property.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgeWithAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ForgeWithAttribute"/>.
    /// </summary>
    /// <param name="destinationProperty">The name of the destination property.</param>
    /// <param name="forgingMethodName">The name of the forging method to use for the nested object.</param>
    public ForgeWithAttribute(string destinationProperty, string forgingMethodName)
    {
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
        ForgingMethodName = forgingMethodName ?? throw new ArgumentNullException(nameof(forgingMethodName));
    }

    /// <summary>
    /// Gets the name of the destination property.
    /// </summary>
    public string DestinationProperty { get; }

    /// <summary>
    /// Gets the name of the forging method to use.
    /// </summary>
    public string ForgingMethodName { get; }
}
