using Microsoft.CodeAnalysis;

namespace TypeForge.Generator;

/// <summary>
/// Diagnostic descriptors for TypeForge source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "TypeForge";

    public static readonly DiagnosticDescriptor ClassMustBePartial = new(
        id: "TF0001",
        title: "Forger class must be partial",
        messageFormat: "The class '{0}' marked with [TypeForge] must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MethodMustBePartial = new(
        id: "TF0002",
        title: "Forging method must be partial",
        messageFormat: "The forging method '{0}' must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SourceTypeHasNoProperties = new(
        id: "TF0003",
        title: "Source type has no accessible properties",
        messageFormat: "The source type '{0}' has no accessible properties to forge",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DestinationTypeHasNoConstructor = new(
        id: "TF0004",
        title: "Destination type has no accessible constructor",
        messageFormat: "The destination type '{0}' has no accessible constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedSourceProperty = new(
        id: "TF0005",
        title: "Unmapped source property",
        messageFormat: "The source property '{0}.{1}' is not mapped to any destination property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedDestinationProperty = new(
        id: "TF0006",
        title: "Unmapped destination property",
        messageFormat: "The destination property '{0}.{1}' is not mapped from any source property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullableToNonNullableMapping = new(
        id: "TF0007",
        title: "Nullable to non-nullable mapping",
        messageFormat: "The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ResolverMethodNotFound = new(
        id: "TF0008",
        title: "Resolver method not found",
        messageFormat: "The resolver method '{0}' referenced in [ForgeFrom] or [ForgeWith] was not found",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidResolverSignature = new(
        id: "TF0009",
        title: "Invalid resolver method signature",
        messageFormat: "The resolver method '{0}' has an invalid signature",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CircularMappingDependency = new(
        id: "TF0010",
        title: "Circular mapping dependency detected",
        messageFormat: "A circular mapping dependency was detected: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyMappedByConvention = new(
        id: "TF0011",
        title: "Property mapped by convention",
        messageFormat: "The property '{0}' was mapped by name convention",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor ForgeFromCannotBeReversed = new(
        id: "TF0012",
        title: "[ForgeFrom] cannot be auto-reversed",
        messageFormat: "The [ForgeFrom] attribute on '{0}' cannot be automatically reversed; provide manual reverse mapping",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        id: "TF0013",
        title: "Ambiguous constructor selection",
        messageFormat: "Multiple constructors on '{0}' match equally; add or remove constructor parameters to disambiguate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructorParameterNotMatched = new(
        id: "TF0014",
        title: "Constructor parameter has no matching source",
        messageFormat: "The constructor parameter '{0}' on '{1}' has no matching source property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeWithLacksReverseForge = new(
        id: "TF0015",
        title: "[ForgeWith] nested method lacks [ReverseForge]",
        messageFormat: "The nested forging method '{0}' used in [ForgeWith] does not have [ReverseForge]; reverse mapping may be incomplete",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HookMethodInvalid = new(
        id: "TF0016",
        title: "Hook method not found or has invalid signature",
        messageFormat: "The hook method '{0}' was not found or has an invalid signature",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseExistingValueInvalid = new(
        id: "TF0017",
        title: "[UseExistingValue] on non-reference type or method returns non-void",
        messageFormat: "[UseExistingValue] requires a reference type parameter and the method must return void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
