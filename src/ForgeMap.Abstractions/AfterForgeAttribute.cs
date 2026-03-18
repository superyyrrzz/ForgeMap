using System;

namespace ForgeMap;

/// <summary>
/// Calls a method after forging completes for post-processing.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class AfterForgeAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="AfterForgeAttribute"/>.
    /// </summary>
    /// <param name="methodName">The name of the method to call after forging.</param>
    public AfterForgeAttribute(string methodName)
    {
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
    }

    /// <summary>
    /// Gets the name of the method to call after forging.
    /// </summary>
    public string MethodName { get; }
}
