using System;

namespace ForgeMap;

/// <summary>
/// Generates a polymorphic dispatch method that inspects the runtime type
/// of the source and delegates to the most-specific derived forge method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ForgeAllDerivedAttribute : Attribute { }
