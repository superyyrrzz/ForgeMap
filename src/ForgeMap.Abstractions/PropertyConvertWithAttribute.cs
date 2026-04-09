using System;

namespace ForgeMap;

/// <summary>
/// Specifies a per-property converter method or type for a specific destination property.
/// This is a standalone alternative to using <see cref="ForgePropertyAttribute.ConvertWith"/>
/// or <see cref="ForgePropertyAttribute.ConvertWithType"/>.
/// Place on a forge method alongside other mapping attributes.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class PropertyConvertWithAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="PropertyConvertWithAttribute"/> with a converter method name.
    /// </summary>
    /// <param name="destinationProperty">The name of the destination property.</param>
    /// <param name="methodName">The name of a method on the forger class that converts the source property value.</param>
    public PropertyConvertWithAttribute(string destinationProperty, string methodName)
    {
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
        MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
    }

    /// <summary>
    /// Creates a new <see cref="PropertyConvertWithAttribute"/> with a converter type.
    /// </summary>
    /// <param name="destinationProperty">The name of the destination property.</param>
    /// <param name="converterType">The type implementing <see cref="ITypeConverter{TSource, TDestination}"/>.</param>
    public PropertyConvertWithAttribute(string destinationProperty, Type converterType)
    {
        DestinationProperty = destinationProperty ?? throw new ArgumentNullException(nameof(destinationProperty));
        ConverterType = converterType ?? throw new ArgumentNullException(nameof(converterType));
    }

    /// <summary>
    /// Gets the name of the destination property.
    /// </summary>
    public string DestinationProperty { get; }

    /// <summary>
    /// Gets the converter method name, or null if a converter type is used.
    /// </summary>
    public string? MethodName { get; }

    /// <summary>
    /// Gets the converter type, or null if a method name is used.
    /// </summary>
    public Type? ConverterType { get; }
}
