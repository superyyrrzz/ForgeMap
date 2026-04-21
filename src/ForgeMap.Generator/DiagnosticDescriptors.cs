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
        messageFormat: "[ForgeAllDerived] on '{0}' found no derived forge methods; {1}",
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

    public static readonly DiagnosticDescriptor AmbiguousAutoWire = new(
        id: "FM0025",
        title: "Ambiguous auto-wire: multiple forge methods match",
        messageFormat: "Multiple forge methods match for auto-wiring property '{0}' on '{1}'. Use explicit [ForgeWith] to resolve the ambiguity.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AutoWiredPropertyLacksReverseForge = new(
        id: "FM0026",
        title: "Auto-wired property has no reverse forge method",
        messageFormat: "Auto-wired property '{0}' has no reverse forge method; reverse mapping may be incomplete",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyAutoWired = new(
        id: "FM0027",
        title: "Property auto-wired via forge method",
        messageFormat: "Property '{0}' auto-wired via forge method '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor ExistingTargetOnNonMutationMethod = new(
        id: "FM0028",
        title: "ExistingTarget is only valid on mutation methods",
        messageFormat: "ExistingTarget = true is only valid on [UseExistingValue] mutation methods",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExistingTargetPropertyHasNoGetter = new(
        id: "FM0029",
        title: "Property has no getter for in-place update",
        messageFormat: "Property '{0}' has no getter — cannot read existing value for in-place update",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ExistingTargetNoMatchingForgeInto = new(
        id: "FM0030",
        title: "No matching ForgeInto method for nested existing-target",
        messageFormat: "No matching ForgeInto method found for nested existing-target property '{0}'. The existing target value may not be fully updated.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SyncRequiresKeyProperty = new(
        id: "FM0031",
        title: "CollectionUpdateStrategy.Sync requires KeyProperty",
        messageFormat: "CollectionUpdateStrategy.Sync requires KeyProperty to be set on [ForgeProperty] for property '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeyPropertyNotFound = new(
        id: "FM0032",
        title: "KeyProperty not found on element type",
        messageFormat: "KeyProperty '{0}' not found on element type '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor StringToEnumAutoConverted = new(
        id: "FM0033",
        title: "Property auto-converted from string to enum",
        messageFormat: "Property '{0}' auto-converted from string to enum '{1}' using {2}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor ConvertWithTypeDoesNotImplementInterface = new(
        id: "FM0034",
        title: "[ConvertWith] type does not implement ITypeConverter",
        messageFormat: "[ConvertWith] type '{0}' does not implement ITypeConverter<{1}, {2}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConvertWithNoParameterlessConstructor = new(
        id: "FM0035",
        title: "[ConvertWith] converter type has no accessible parameterless constructor",
        messageFormat: "[ConvertWith] converter type '{0}' has no accessible parameterless constructor and forger has no DI (IServiceProvider/IServiceScopeFactory)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConvertWithIgnoresPropertyAttributes = new(
        id: "FM0036",
        title: "[ConvertWith] takes precedence over mapping attributes",
        messageFormat: "[ConvertWith] on method '{0}' takes full precedence \u2014 [ForgeProperty], [ForgeFrom], and [ForgeWith] attributes are ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConvertWithMemberNotFound = new(
        id: "FM0037",
        title: "[ConvertWith] member not found or incompatible",
        messageFormat: "[ConvertWith] member '{0}' not found on forger class, or is inaccessible, or its type does not implement ITypeConverter<{1}, {2}>",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CoalesceToNewNoConstructor = new(
        id: "FM0038",
        title: "CoalesceToNew requires accessible parameterless constructor",
        messageFormat: "CoalesceToNew cannot synthesize 'new {0}()': type has no accessible parameterless constructor, or has uninitialized 'required' members without [SetsRequiredMembers]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionTypeCoerced = new(
        id: "FM0039",
        title: "Collection type coerced",
        messageFormat: "Property '{0}' collection type coerced from '{1}' to '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor CollectionCoercionNotSupported = new(
        id: "FM0040",
        title: "No known collection coercion",
        messageFormat: "Property '{0}': no known coercion from '{1}' to '{2}'; property skipped",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionMethodNoElementMethod = new(
        id: "FM0041",
        title: "Collection method has no matching element forge method",
        messageFormat: "Collection method '{0}' declared but no matching element forge method found for '{1}' \u2192 '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionMethodAmbiguous = new(
        id: "FM0042",
        title: "Ambiguous collection method",
        messageFormat: "Collection method '{0}' is ambiguous: multiple element forge methods match '{1}' \u2192 '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AfterForgeMethodInvalid = new(
        id: "FM0043",
        title: "[AfterForge] method not found or has wrong signature",
        messageFormat: "[AfterForge] method '{0}' not found on forger class, or has wrong signature; expected: void {0}({1} source, {2} destination)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AfterForgeWithConvertWith = new(
        id: "FM0044",
        title: "[AfterForge] and [ConvertWith] are mutually exclusive",
        messageFormat: "[AfterForge] and [ConvertWith] are mutually exclusive on method '{0}' \u2014 [ConvertWith] takes full control of the method body",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AfterForgeOnCollectionMethod = new(
        id: "FM0045",
        title: "[AfterForge] is not applicable to collection methods",
        messageFormat: "[AfterForge] is not applicable to collection method '{0}' — use element-level [AfterForge] on the element forge method instead",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnmatchedConstructorParameter = new(
        id: "FM0046",
        title: "Unmatched constructor parameter",
        messageFormat: "Constructor parameter '{0}' on '{1}' could not be matched to any source property or [ForgeProperty] mapping",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SpecifiedConstructorNotFound = new(
        id: "FM0047",
        title: "Specified constructor not found",
        messageFormat: "No constructor on '{0}' matches the parameter types specified in [ForgeConstructor]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructorMappingInfo = new(
        id: "FM0048",
        title: "Constructor mapping routing info",
        messageFormat: "Constructor routing for '{0}': {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor StringToEnumNullSafeGuardApplied = new(
        id: "FM0049",
        title: "Null-safe guard applied to string→enum conversion",
        messageFormat: "Property '{0}': null/empty string→enum guard applied; null or empty source values will return default({1})",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor NullableCollectionCoercionApplied = new(
        id: "FM0050",
        title: "Nullable-safe collection coercion applied",
        messageFormat: "Property '{0}': nullable-safe collection coercion applied from '{1}' to '{2}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor NullableCollectionCoercionUnsupported = new(
        id: "FM0051",
        title: "Unsupported nullable collection coercion",
        messageFormat: "Property '{0}': nullable element type coercion from '{1}' to '{2}' is not supported for this collection kind",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyConverterMethodNotFound = new(
        id: "FM0052",
        title: "Per-property converter method not found",
        messageFormat: "Converter method '{0}' specified for property '{1}' was not found on the forger class",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyConverterSignatureMismatch = new(
        id: "FM0053",
        title: "Per-property converter signature mismatch",
        messageFormat: "Converter method '{0}' for property '{1}' has an incompatible signature; expected '{2}' -> '{3}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor PropertyConverterApplied = new(
        id: "FM0054",
        title: "Per-property converter applied",
        messageFormat: "Property '{0}': per-property converter '{1}' applied",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor SelectPropertySourceNotEnumerable = new(
        id: "FM0055",
        title: "SelectProperty requires an enumerable source property",
        messageFormat: "SelectProperty set on '{0}' but source property type '{1}' is not enumerable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyMemberNotFound = new(
        id: "FM0056",
        title: "SelectProperty member not found on element type",
        messageFormat: "SelectProperty = \"{0}\" not found on element type '{1}' for property '{2}' (or not a public readable property)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyElementTypeIncompatible = new(
        id: "FM0057",
        title: "SelectProperty projected type incompatible with destination element type",
        messageFormat: "Projected property '{0}' (type '{1}') is not assignable to destination element type '{2}' for property '{3}', and no built-in coercion applies",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyConflictsWithConverter = new(
        id: "FM0058",
        title: "SelectProperty conflicts with ConvertWith / ConvertWithType",
        messageFormat: "Property '{0}' has more than one of SelectProperty, ConvertWith, ConvertWithType set on the same [ForgeProperty] — choose one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyApplied = new(
        id: "FM0059",
        title: "Projection applied for property",
        messageFormat: "Projection applied for property '{0}': {1}.Select(x => x.{2})",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);

    public static readonly DiagnosticDescriptor SelectPropertyConflictsWithForgeFromOrWith = new(
        id: "FM0072",
        title: "SelectProperty conflicts with [ForgeFrom] / [ForgeWith]",
        messageFormat: "Property '{0}' has SelectProperty set on [ForgeProperty] and is also targeted by [ForgeFrom] / [ForgeWith] — choose one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyDestinationNotEnumerable = new(
        id: "FM0073",
        title: "Unsupported destination collection type for SelectProperty",
        messageFormat: "SelectProperty set on '{0}' but destination property type '{1}' is not a supported collection type for SelectProperty (supported: T[], List/IList/ICollection/IReadOnlyList/IReadOnlyCollection<T>, HashSet<T>, ReadOnlyCollection<T>, IEnumerable<T>)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SelectPropertyNotSupportedOnForgeInto = new(
        id: "FM0075",
        title: "SelectProperty is not supported on ForgeInto / [UseExistingValue] methods",
        messageFormat: "Property '{0}' uses SelectProperty on a ForgeInto method, which is not yet supported and will be ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionAndSkipWhenBothSet = new(
        id: "FM0060",
        title: "Condition and SkipWhen are mutually exclusive",
        messageFormat: "Property '{0}' has both Condition and SkipWhen set on the same [ForgeProperty] — choose one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionalPredicateInvalid = new(
        id: "FM0061",
        title: "Conditional predicate method not found or has wrong signature",
        messageFormat: "Predicate method '{0}' for property '{1}' was not found on the forger class or has the wrong signature; expected: bool {0}({2})",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionalNotSupportedOnInitOrCtor = new(
        id: "FM0062",
        title: "Condition/SkipWhen cannot be applied to init, required, or constructor-bound properties",
        messageFormat: "Condition/SkipWhen cannot be applied to property '{0}' because it is set via constructor or init/required. Use [ForgeFrom] to choose between alternative constructions, or drop init if you need conditional post-construction assignment.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionalConflictsWithForgeFromOrWith = new(
        id: "FM0063",
        title: "Condition/SkipWhen conflicts with [ForgeFrom] / [ForgeWith]",
        messageFormat: "Property '{0}' has Condition/SkipWhen and is also targeted by [ForgeFrom] or [ForgeWith] — choose one",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConditionalAssignmentApplied = new(
        id: "FM0064",
        title: "Conditional assignment applied for property",
        messageFormat: "Conditional assignment applied for property '{0}' via '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: false);
}
