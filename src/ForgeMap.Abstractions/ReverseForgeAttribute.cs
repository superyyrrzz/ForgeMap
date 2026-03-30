using System;

namespace ForgeMap;

/// <summary>
/// Generates a reverse forging method automatically.
/// </summary>
/// <remarks>
/// The reverse method maps all matching properties in the opposite direction.
/// When the forward method maps from an internal model to an external DTO,
/// the generated reverse method allows the DTO to overwrite every matched property
/// on the model — including security-sensitive fields such as <c>IsAdmin</c>,
/// <c>PasswordHash</c>, or audit timestamps. Use <see cref="IgnoreAttribute"/> on
/// the forward method to exclude properties that should not be writable via the
/// reverse mapping.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ReverseForgeAttribute : Attribute
{
}
