using System;

namespace ForgeMap;

/// <summary>
/// Generates a reverse forging method automatically.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ReverseForgeAttribute : Attribute
{
}
