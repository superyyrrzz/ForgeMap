using System;

namespace TypeForge;

/// <summary>
/// Marks a parameter as an existing value to forge into (mutate in place).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class UseExistingValueAttribute : Attribute
{
}
