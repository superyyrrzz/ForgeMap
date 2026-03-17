using System;

namespace TypeForge;

/// <summary>
/// Uses a custom converter class for the forging.
/// Available in TypeForge v1.1+.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConvertWithAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="ConvertWithAttribute"/>.
    /// </summary>
    /// <param name="converterType">The type of the converter class. Must implement <see cref="ITypeConverter{TSource, TDestination}"/>.</param>
    public ConvertWithAttribute(Type converterType)
    {
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    /// <summary>
    /// Gets the type of the converter class.
    /// </summary>
    public Type ConverterType { get; }
}
