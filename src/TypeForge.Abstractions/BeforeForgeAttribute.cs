using System;

namespace TypeForge;

/// <summary>
/// Calls a method before forging begins for pre-processing or validation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class BeforeForgeAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="BeforeForgeAttribute"/>.
    /// </summary>
    /// <param name="methodName">The name of the method to call before forging.</param>
    public BeforeForgeAttribute(string methodName)
    {
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
    }

    /// <summary>
    /// Gets the name of the method to call before forging.
    /// </summary>
    public string MethodName { get; }
}
