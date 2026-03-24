namespace ForgeMap;

/// <summary>
/// Specifies how nullable source properties should be assigned to non-nullable destination properties.
/// This setting only applies to reference type properties where the source has a nullable annotation
/// and the destination does not.
/// </summary>
public enum NullPropertyHandling
{
    /// <summary>
    /// Use the null-forgiving operator: <c>target.X = source.X!;</c>
    /// The assignment always happens. If the source value is null at runtime,
    /// the destination receives null (bypassing the compiler's nullable analysis).
    /// This is the default, matching AutoMapper's "assign through" behavior.
    /// </summary>
    NullForgiving,

    /// <summary>
    /// Skip the assignment when the source is null:
    /// <c>if (source.X is { } value) target.X = value;</c>
    /// The destination retains its constructor-initialized or default value.
    /// </summary>
    SkipNull,

    /// <summary>
    /// Coalesce to a type-appropriate default value:
    /// <c>target.X = source.X ?? &lt;default&gt;;</c>
    /// The assignment always happens. If the source value is null, a non-null
    /// default is substituted (see type-aware default values documentation).
    /// </summary>
    CoalesceToDefault,

    /// <summary>
    /// Throw an exception when the source is null:
    /// <c>target.X = source.X ?? throw new ArgumentNullException(nameof(source.X));</c>
    /// Fail-fast behavior for strict null safety using a single-evaluation, coalesce-to-throw pattern.
    /// </summary>
    ThrowException
}
