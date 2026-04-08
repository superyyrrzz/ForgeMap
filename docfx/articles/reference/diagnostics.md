# Diagnostics Reference

ForgeMap reports diagnostics at compile time to help you catch mapping issues early. This page lists all 45 diagnostics grouped by category.

## Severity Levels

| Severity | Meaning |
|----------|---------|
| **Error** | Build fails. The mapping cannot be generated. |
| **Warning** | Build succeeds but the mapping may be incorrect. |
| **Info** | Informational. Most are disabled by default; enable with `<WarningsAsErrors>` or `#pragma`. |

## Suppressing Diagnostics

```csharp
// Per-forger
[ForgeMap(SuppressDiagnostics = new[] { "FM0005", "FM0007" })]

// Per-file
#pragma warning disable FM0005

// Per-project (.csproj)
<NoWarn>$(NoWarn);FM0005</NoWarn>
```

---

## Structure Errors (FM0001-FM0004)

These fire when the forger class or method declarations are invalid.

### FM0001 — Forger class must be partial

| | |
|---|---|
| **Severity** | Error |
| **Message** | The class '{0}' marked with [ForgeMap] must be declared as partial |
| **Fix** | Add the `partial` keyword to the class declaration. |

### FM0002 — Forging method must be partial

| | |
|---|---|
| **Severity** | Error |
| **Message** | The forging method '{0}' must be declared as partial |
| **Fix** | Add the `partial` keyword to the method declaration. |

### FM0003 — Source type has no accessible properties

| | |
|---|---|
| **Severity** | Error |
| **Message** | The source type '{0}' has no accessible properties to forge |
| **Fix** | Ensure the source type has public properties with getters. |

### FM0004 — Destination type has no accessible constructor

| | |
|---|---|
| **Severity** | Error |
| **Message** | The destination type '{0}' has no accessible constructor |
| **Fix** | Add a public or internal constructor to the destination type. |

---

## Mapping Warnings (FM0005-FM0007)

These alert you to potentially incomplete or unsafe mappings.

### FM0005 — Unmapped source property

| | |
|---|---|
| **Severity** | Warning |
| **Message** | The source property '{0}.{1}' is not mapped to any destination property |
| **Fix** | Add `[ForgeProperty]` to map it, or suppress if intentional. |

### FM0006 — Unmapped destination property

| | |
|---|---|
| **Severity** | Warning |
| **Message** | The destination property '{0}.{1}' is not mapped from any source property |
| **Fix** | Add a mapping or use `[Ignore]` to suppress. |

### FM0007 — Nullable to non-nullable mapping

| | |
|---|---|
| **Severity** | Warning |
| **Message** | The nullable source '{0}.{1}' is mapped to non-nullable destination '{2}.{3}' |
| **Fix** | Set `NullPropertyHandling` on the forger, property, or assembly level. Options: `NullForgiving` (default), `CoalesceToDefault`, `CoalesceToNew`, `SkipNull`, `ThrowException`. |

---

## Resolution Errors (FM0008-FM0010)

These fire when custom resolvers or nested mappings cannot be resolved.

### FM0008 — Resolver method not found

| | |
|---|---|
| **Severity** | Error |
| **Message** | The resolver method '{0}' referenced in [ForgeFrom] or [ForgeWith] was not found |
| **Fix** | Ensure the method exists on the forger class and is spelled correctly. |

### FM0009 — Invalid resolver method signature

| | |
|---|---|
| **Severity** | Error |
| **Message** | The resolver method '{0}' has an invalid signature |
| **Fix** | Resolver methods must match one of the supported signatures: `T Method(TSource source)` or `T Method(TPropertyValue value)`. |

### FM0010 — Circular mapping dependency detected

| | |
|---|---|
| **Severity** | Error |
| **Message** | A circular mapping dependency was detected: {0} |
| **Fix** | Break the cycle by using `[Ignore]` on one side or restructuring your types. |

---

## Convention Info (FM0011)

### FM0011 — Property mapped by convention

| | |
|---|---|
| **Severity** | Info (disabled by default) |
| **Message** | The property '{0}' was mapped by name convention |
| **Notes** | Enable to audit which properties are auto-matched. |

---

## Reverse Mapping (FM0012, FM0015)

### FM0012 — [ForgeFrom] cannot be auto-reversed

| | |
|---|---|
| **Severity** | Warning |
| **Message** | The [ForgeFrom] attribute on '{0}' cannot be automatically reversed; provide manual reverse mapping |
| **Fix** | When using `[ReverseForge]`, add a separate `[ForgeFrom]` on the reverse method for properties that use custom resolvers. |

### FM0015 — [ForgeWith] nested method lacks [ReverseForge]

| | |
|---|---|
| **Severity** | Warning |
| **Message** | The nested forging method '{0}' used in [ForgeWith] does not have [ReverseForge]; reverse mapping may be incomplete |
| **Fix** | Add `[ReverseForge]` to the nested forge method, or provide a manual reverse mapping. |

---

## Constructor Mapping (FM0013-FM0014)

### FM0013 — Ambiguous constructor selection

| | |
|---|---|
| **Severity** | Error |
| **Message** | Multiple constructors on '{0}' match equally; add or remove constructor parameters to disambiguate |
| **Fix** | Reduce the number of constructors or adjust parameter names to match source properties clearly. |

### FM0014 — Constructor parameter has no matching source

| | |
|---|---|
| **Severity** | Error |
| **Message** | The constructor parameter '{0}' on '{1}' has no matching source property |
| **Fix** | Rename the constructor parameter to match a source property, or add a different constructor. |

---

## Hooks & Mutation (FM0016-FM0018)

### FM0016 — Hook method not found or invalid signature

| | |
|---|---|
| **Severity** | Error |
| **Message** | The hook method '{0}' was not found or has an invalid signature |
| **Fix** | `[BeforeForge]` method: `void Method(TSource source)`. `[AfterForge]` method: `void Method(TSource source, TDest destination)`. |

### FM0017 — [UseExistingValue] on non-reference type or non-void method

| | |
|---|---|
| **Severity** | Error |
| **Message** | [UseExistingValue] requires a reference type parameter and the method must return void |
| **Fix** | The forge method must return `void` and the `[UseExistingValue]` parameter must be a reference type. |

### FM0018 — Hooks not supported on enum or collection methods

| | |
|---|---|
| **Severity** | Warning |
| **Message** | [BeforeForge]/[AfterForge] hooks are not supported on enum or collection forge methods |
| **Fix** | Move hooks to the element-level forge method instead. |

---

## Inheritance & Polymorphism (FM0019-FM0024)

### FM0019 — [IncludeBaseForge] base forge method not found

| | |
|---|---|
| **Severity** | Error |
| **Message** | No forge method mapping '{0}' → '{1}' was found in this forger |
| **Fix** | Ensure the base forge method exists in the same forger class with matching source and destination types. |

### FM0020 — [IncludeBaseForge] type mismatch

| | |
|---|---|
| **Severity** | Error |
| **Message** | The type '{0}' does not derive from '{1}' |
| **Fix** | The derived source/destination types must inherit from the base types specified in `[IncludeBaseForge]`. |

### FM0021 — [IncludeBaseForge] inherited attribute overridden

| | |
|---|---|
| **Severity** | Info |
| **Message** | Inherited configuration for property '{0}' is overridden by an explicit attribute on the derived forge method |
| **Notes** | Enabled by default. Informational — confirms that your explicit attribute takes precedence over the inherited one. |

### FM0022 — [ForgeAllDerived] found no derived forge methods

| | |
|---|---|
| **Severity** | Warning |
| **Message** | [ForgeAllDerived] on '{0}' found no derived forge methods; {1} |
| **Fix** | Add derived forge methods with matching overloads in the same forger class, or remove `[ForgeAllDerived]` if polymorphic dispatch is not needed. |

### FM0023 — [ForgeAllDerived] cannot be combined with [ConvertWith]

| | |
|---|---|
| **Severity** | Error |
| **Message** | [ForgeAllDerived] cannot be combined with [ConvertWith] on method '{0}' |
| **Fix** | Remove one of the attributes. `[ConvertWith]` takes full control of the method body, which conflicts with polymorphic dispatch. |

### FM0024 — [ForgeAllDerived] on abstract/interface destination

| | |
|---|---|
| **Severity** | Warning |
| **Message** | [ForgeAllDerived] on abstract/interface destination type '{0}': unmatched source subtypes will throw NotSupportedException at runtime |
| **Notes** | When the destination is abstract, the generated dispatch throws `NotSupportedException` for unmatched subtypes instead of falling back to a base mapping. Ensure all source subtypes have corresponding derived forge methods. |

---

## Auto-Wiring (FM0025-FM0027)

### FM0025 — Ambiguous auto-wire

| | |
|---|---|
| **Severity** | Warning |
| **Message** | Multiple forge methods match for auto-wiring property '{0}' on '{1}'. Use explicit [ForgeWith] to resolve the ambiguity. |
| **Fix** | Add `[ForgeWith(nameof(SpecificForgeMethod))]` to the property to disambiguate. |

### FM0026 — Auto-wired property has no reverse forge method

| | |
|---|---|
| **Severity** | Warning |
| **Message** | Auto-wired property '{0}' has no reverse forge method; reverse mapping may be incomplete |
| **Fix** | Add `[ReverseForge]` to the nested forge method used for auto-wiring. |

### FM0027 — Property auto-wired via forge method

| | |
|---|---|
| **Severity** | Info (disabled by default) |
| **Message** | Property '{0}' auto-wired via forge method '{1}' |
| **Notes** | Enable to audit auto-wiring decisions. |

---

## Existing Target & Collection Sync (FM0028-FM0032)

### FM0028 — ExistingTarget is only valid on mutation methods

| | |
|---|---|
| **Severity** | Error |
| **Message** | ExistingTarget = true is only valid on [UseExistingValue] mutation methods |
| **Fix** | Use `ExistingTarget` only on `[ForgeProperty]` attributes within `ForgeInto`-style void methods with `[UseExistingValue]`. |

### FM0029 — Property has no getter for in-place update

| | |
|---|---|
| **Severity** | Error |
| **Message** | Property '{0}' has no getter — cannot read existing value for in-place update |
| **Fix** | Add a getter to the destination property, or remove `ExistingTarget = true`. |

### FM0030 — No matching ForgeInto method for nested existing-target

| | |
|---|---|
| **Severity** | Warning |
| **Message** | No matching ForgeInto method found for nested existing-target property '{0}'. The existing target value may not be fully updated. |
| **Fix** | Declare a `ForgeInto` mutation method for the nested type. |

### FM0031 — Sync requires KeyProperty

| | |
|---|---|
| **Severity** | Error |
| **Message** | CollectionUpdateStrategy.Sync requires KeyProperty to be set on [ForgeProperty] for property '{0}' |
| **Fix** | Add `KeyProperty = "Id"` (or your key property name) to the `[ForgeProperty]` attribute. |

### FM0032 — KeyProperty not found on element type

| | |
|---|---|
| **Severity** | Error |
| **Message** | KeyProperty '{0}' not found on element type '{1}' |
| **Fix** | Ensure the specified key property exists on both the source and destination element types. |

---

## String-to-Enum (FM0033)

### FM0033 — Property auto-converted from string to enum

| | |
|---|---|
| **Severity** | Info (disabled by default) |
| **Message** | Property '{0}' auto-converted from string to enum '{1}' using {2} |
| **Notes** | Enable to audit automatic string-to-enum conversions. Configure behavior via `StringToEnum` on `[ForgeMap]` or `[ForgeMapDefaults]`. |

---

## [ConvertWith] (FM0034-FM0037)

### FM0034 — [ConvertWith] type does not implement ITypeConverter

| | |
|---|---|
| **Severity** | Error |
| **Message** | [ConvertWith] type '{0}' does not implement ITypeConverter<{1}, {2}> |
| **Fix** | The converter class must implement `ITypeConverter<TSource, TDestination>` with matching type arguments. |

### FM0035 — [ConvertWith] converter has no parameterless constructor

| | |
|---|---|
| **Severity** | Error |
| **Message** | [ConvertWith] converter type '{0}' has no accessible parameterless constructor and forger has no DI (IServiceProvider/IServiceScopeFactory) |
| **Fix** | Add a parameterless constructor to the converter, or add an `IServiceProvider` constructor parameter to the forger for DI resolution. Alternatively, use member-based `[ConvertWith(nameof(_field))]`. |

### FM0036 — [ConvertWith] takes precedence over mapping attributes

| | |
|---|---|
| **Severity** | Warning |
| **Message** | [ConvertWith] on method '{0}' takes full precedence — [ForgeProperty], [ForgeFrom], and [ForgeWith] attributes are ignored |
| **Notes** | `[ConvertWith]` delegates the entire conversion. Other mapping attributes on the same method have no effect. |

### FM0037 — [ConvertWith] member not found or incompatible

| | |
|---|---|
| **Severity** | Error |
| **Message** | [ConvertWith] member '{0}' not found on forger class, or is inaccessible, or its type does not implement ITypeConverter<{1}, {2}> |
| **Fix** | Ensure the field or property exists, is accessible, and its type implements the correct `ITypeConverter` interface. |

---

## v1.5 Features (FM0038-FM0042)

### FM0038 — CoalesceToNew requires parameterless constructor

| | |
|---|---|
| **Severity** | Error |
| **Message** | CoalesceToNew cannot synthesize 'new {0}()': type has no accessible parameterless constructor, or has uninitialized 'required' members without [SetsRequiredMembers] |
| **Fix** | Add a parameterless constructor to the type, or use a different `NullPropertyHandling` strategy. |

### FM0039 — Collection type coerced

| | |
|---|---|
| **Severity** | Info (disabled by default) |
| **Message** | Property '{0}' collection type coerced from '{1}' to '{2}' |
| **Notes** | Informational — the generator automatically adapted between compatible collection types. |

### FM0040 — No known collection coercion

| | |
|---|---|
| **Severity** | Warning |
| **Message** | Property '{0}': no known coercion from '{1}' to '{2}'; property skipped |
| **Fix** | Use compatible collection types (e.g., `List<T>` → `IReadOnlyList<T>`), or add a manual `[ForgeFrom]` resolver. |

### FM0041 — Collection method has no matching element forge method

| | |
|---|---|
| **Severity** | Error |
| **Message** | Collection method '{0}' declared but no matching element forge method found for '{1}' → '{2}' |
| **Fix** | Add a forge method for the element type (e.g., `partial TDest Forge(TSrc source)`). |

### FM0042 — Ambiguous collection method

| | |
|---|---|
| **Severity** | Error |
| **Message** | Collection method '{0}' is ambiguous: multiple element forge methods match '{1}' → '{2}' |
| **Fix** | Remove duplicate forge methods for the same type pair, or restructure into separate forger classes. |

---

## [AfterForge] (FM0043-FM0045)

### FM0043 — [AfterForge] method not found or wrong signature

| | |
|---|---|
| **Severity** | Error |
| **Message** | [AfterForge] method '{0}' not found on forger class, or has wrong signature; expected: void {0}({1} source, {2} destination) |
| **Fix** | The method must return `void` and take the source and destination types as parameters: `void MethodName(TSource source, TDest destination)`. |

### FM0044 — [AfterForge] and [ConvertWith] are mutually exclusive

| | |
|---|---|
| **Severity** | Error |
| **Message** | [AfterForge] and [ConvertWith] are mutually exclusive on method '{0}' — [ConvertWith] takes full control of the method body |
| **Fix** | Remove one of the attributes. If using `[ConvertWith]`, handle post-mapping logic inside the converter. |

### FM0045 — [AfterForge] is not applicable to collection methods

| | |
|---|---|
| **Severity** | Error |
| **Message** | [AfterForge] is not applicable to collection method '{0}' — use element-level [AfterForge] on the element forge method instead |
| **Fix** | Move the `[AfterForge]` attribute to the element-level forge method. |
