using Microsoft.CodeAnalysis;

namespace ForgeMap.Generator;

/// <summary>
/// Diagnostic descriptors for ForgeMap source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "ForgeMap";

    public static readonly DiagnosticDescriptor ClassMustBePartial = new(
        id: "FM0001",
        title: "Forger class must be partial",
        messageFormat: "The class '{0}' marked with [ForgeMap] must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MethodMustBePartial = new(
        id: "FM0002",
        title: "Forging method must be partial",
        messageFormat: "The forging method '{0}' must be declared as partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SourceTypeHasNoProperties = new(
        id: "FM0003",
        title: "Source type has no accessible properties",
        messageFormat: "The source type '{0}' has no accessible properties to forge",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DestinationTypeHasNoConstructor = new(
        id: "FM0004",
        title: "Destination type has no accessible constructor",
        messageFormat: "The destination type '{0}' has no accessible constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedSourceProperty = new(
        id: "FM0005",
        title: "Unmapped source property",
        messageFormat: "The source property '{0}.{1}' is not mapped to any destination property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmappedDestinationProperty = new(
        id: "FM0006",
        title: "Unmapped destination property",
        messageFormat: "The destination property '{0}.{1}' is not mapped from any source property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullableToNonNullableMapping = new(
        id: "FM0007",
        title: "Nullable to non-nullable mapping",
        messageFormat: "The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ResolverMethodNotFound = new(
        id: "FM0008",
        title: "Resolver method not found",
        messageFormat: "The resolver method '{0}' referenced in [ForgeFrom] or [ForgeWith] was not found",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidResolverSignature = new(
        id: "FM0009",
        title: "Invalid resolver method signature",
        messageFormat: "The resolver method '{0}' has an invalid signature",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CircularMappingDependency = new(
        id: "FM0010",
        title: "Circular mapping dependency detected",
        messageFormat: "A circular mapping dependency was detected: {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyMappedByConvention = new(
        id: "FM0011",
        title: "Property mapped by convention",
        messageFormat: "The property '{0}' was mapped by name convention",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor ForgeFromCannotBeReversed = new(
        id: "FM0012",
        title: "[ForgeFrom] cannot be auto-reversed",
        messageFormat: "The [ForgeFrom] attribute on '{0}' cannot be automatically reversed; provide manual reverse mapping",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        id: "FM0013",
        title: "Ambiguous constructor selection",
        messageFormat: "Multiple constructors on '{0}' match equally; add or remove constructor parameters to disambiguate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructorParameterNotMatched = new(
        id: "FM0014",
        title: "Constructor parameter has no matching source",
        messageFormat: "The constructor parameter '{0}' on '{1}' has no matching source property",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeWithLacksReverseForge = new(
        id: "FM0015",
        title: "[ForgeWith] nested method lacks [ReverseForge]",
        messageFormat: "The nested forging method '{0}' used in [ForgeWith] does not have [ReverseForge]; reverse mapping may be incomplete",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HookMethodInvalid = new(
        id: "FM0016",
        title: "Hook method not found or has invalid signature",
        messageFormat: "The hook method '{0}' was not found or has an invalid signature",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UseExistingValueInvalid = new(
        id: "FM0017",
        title: "[UseExistingValue] on non-reference type or method returns non-void",
        messageFormat: "[UseExistingValue] requires a reference type parameter and the method must return void",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HooksNotSupportedOnMethodKind = new(
        id: "FM0018",
        title: "[BeforeForge]/[AfterForge] not supported on enum or collection forge methods",
        messageFormat: "[BeforeForge]/[AfterForge] hooks are not supported on enum or collection forge methods",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeBaseForgeMethodNotFound = new(
        id: "FM0019",
        title: "[IncludeBaseForge] base forge method not found",
        messageFormat: "No forge method mapping '{0}' → '{1}' was found in this forger",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeBaseForgeTypeMismatch = new(
        id: "FM0020",
        title: "[IncludeBaseForge] type mismatch",
        messageFormat: "The type '{0}' does not derive from '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor IncludeBaseForgeOverridden = new(
        id: "FM0021",
        title: "[IncludeBaseForge] inherited attribute overridden",
        messageFormat: "Inherited configuration for property '{0}' is overridden by an explicit attribute on the derived forge method",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeAllDerivedNoDerivedMethods = new(
        id: "FM0022",
        title: "[ForgeAllDerived] found no derived forge methods",
        messageFormat: "[ForgeAllDerived] on '{0}' found no derived forge methods; polymorphic dispatch will only map the base type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeAllDerivedNoDerivedMethodsAbstract = new(
        id: "FM0022",
        title: "[ForgeAllDerived] found no derived forge methods",
        messageFormat: "[ForgeAllDerived] on '{0}' found no derived forge methods; dispatch-only body has no base-type fallback — all inputs will throw NotSupportedException",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeAllDerivedWithConvertWith = new(
        id: "FM0023",
        title: "[ForgeAllDerived] cannot be combined with [ConvertWith]",
        messageFormat: "[ForgeAllDerived] cannot be combined with [ConvertWith] on method '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ForgeAllDerivedAbstractDestination = new(
        id: "FM0024",
        title: "[ForgeAllDerived] on abstract/interface destination",
        messageFormat: "[ForgeAllDerived] on abstract/interface destination type '{0}': unmatched source subtypes will throw NotSupportedException at runtime",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
