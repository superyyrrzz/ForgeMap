using System;

namespace ForgeMap;

/// <summary>
/// Specifies which constructor on the destination type should be used for mapping.
/// Applied to a forge method to explicitly select a constructor by its parameter types.
/// </summary>
/// <remarks>
/// When applied, the generator will look for a constructor on the destination type
/// whose parameter types match the specified <see cref="ParameterTypes"/> array.
/// If no matching constructor is found, diagnostic FM0047 is emitted.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ForgeConstructorAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ForgeConstructorAttribute"/> with the specified constructor parameter types.
    /// </summary>
    /// <param name="parameterTypes">
    /// The types of the constructor parameters, in order.
    /// Pass an empty array to explicitly select the parameterless constructor.
    /// </param>
    public ForgeConstructorAttribute(params Type[] parameterTypes)
    {
        ParameterTypes = parameterTypes ?? throw new ArgumentNullException(nameof(parameterTypes));
    }

    /// <summary>
    /// Gets the constructor parameter types used to identify the target constructor.
    /// </summary>
    public Type[] ParameterTypes { get; }
}
