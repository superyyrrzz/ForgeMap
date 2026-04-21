using System;

namespace ForgeMap;

/// <summary>
/// Marks a partial forge method that constructs a new destination object from the source primitive
/// by assigning or binding it to the named property. The method must have signature
/// <c>partial TEntity MethodName(TPrimitive source)</c>. The destination type must either expose a
/// settable (<c>set</c> or <c>init</c>) property of that name or a constructor parameter of that name.
/// The generator emits the appropriate construction form (<c>new TEntity { Prop = source }</c> or
/// <c>new TEntity(prop: source)</c>), with null-guarding governed by <c>NullHandling</c>.
/// Available in ForgeMap v1.7+.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WrapPropertyAttribute : Attribute
{
    /// <summary>
    /// Creates a new <see cref="WrapPropertyAttribute"/>.
    /// </summary>
    /// <param name="propertyName">Name of the destination property or constructor parameter to assign.</param>
    public WrapPropertyAttribute(string propertyName)
    {
        PropertyName = propertyName ?? throw new ArgumentNullException(nameof(propertyName));
    }

    /// <summary>
    /// Gets the name of the destination property or constructor parameter to assign.
    /// </summary>
    public string PropertyName { get; }
}
