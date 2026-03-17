using System;

namespace TypeForge;

/// <summary>
/// Generates a reverse forging method automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ReverseForgeAttribute : Attribute
{
}
