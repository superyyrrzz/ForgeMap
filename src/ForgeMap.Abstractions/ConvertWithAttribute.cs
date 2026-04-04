using System;

namespace ForgeMap;

/// <summary>
/// Uses a custom converter class for the forging.
/// Available in ForgeMap v1.1+.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConvertWithAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ConvertWithAttribute"/> with a converter type.
    /// </summary>
    /// <param name="converterType">The type of the converter class. Must implement <see cref="ITypeConverter{TSource, TDestination}"/>.</param>
    public ConvertWithAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    /// <summary>
    /// Creates a new <see cref="ConvertWithAttribute"/> with a member reference.
    /// </summary>
    /// <param name="memberName">The name of a field or property on the forger class whose type implements <see cref="ITypeConverter{TSource, TDestination}"/>.</param>
    public ConvertWithAttribute(string memberName)
    {
        MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
    }

    /// <summary>
    /// Gets the type of the converter class, or null if a member reference is used.
    /// </summary>
    public Type? ConverterType { get; }

    /// <summary>
    /// Gets the name of the field or property on the forger class, or null if a type reference is used.
    /// </summary>
    public string? MemberName { get; }
}
