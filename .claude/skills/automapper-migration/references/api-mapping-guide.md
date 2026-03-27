# AutoMapper → ForgeMap API Migration Guide

This reference maps AutoMapper patterns to their ForgeMap equivalents.

## Core Concepts

| AutoMapper Concept | ForgeMap Equivalent | Notes |
|---|---|---|
| `Profile` class | `[ForgeMap]` partial class | ForgeMap uses one partial class per "forger" group |
| `CreateMap<S,D>()` | `partial D Forge(S source);` method | Each mapping is a partial method declaration |
| `IMapper` (injected) | Forger class (injected) | Register via `services.AddForgeMaps()` |
| `mapper.Map<D>(src)` | `forger.Forge(src)` | Direct method call |
| `mapper.Map(src, dest)` | `forger.ForgeInto(src, dest)` | Any void partial method with a `[UseExistingValue]` parameter; `ForgeInto` is a naming convention, not required |

## Property Mapping

| AutoMapper | ForgeMap | Example |
|---|---|---|
| Auto by name | Auto by name (default) | Same-name properties map automatically |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` | Attribute on the forge method |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` | Attribute on the forge method |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.A.B))` | `[ForgeProperty("A.B", nameof(D.X))]` | Dot notation for nested access |
| Flattening (`Order.CustomerName` from `Order.Customer.Name`) | Auto-flattened by convention (no attributes needed) | ForgeMap auto-flattens by convention (e.g., `CustomerName` → `Customer.Name`); use `[ForgeProperty("Customer.Name", nameof(OrderDto.CustomerName))]` only as an override when naming doesn't line up |

## Custom Resolution

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `IValueResolver<S,D,TVal>` | `[ForgeFrom(nameof(D.DestProp), nameof(ResolverMethod))]` | Static/instance method on the forger class |
| `.MapFrom(s => expr)` | `[ForgeFrom(nameof(D.DestProp), nameof(Method))]` | Resolver method returns value |
| `ITypeConverter<S,D>` | `[ForgeFrom]` resolver methods | The `[ConvertWith]` attribute exists in the abstractions but is not yet honored by the generator for conversion; use `[ForgeFrom]`-based resolver methods instead |

### Resolver method signatures

```csharp
// Full source access (like IValueResolver)
private static string ResolveFullName(UserEntity source)
    => $"{source.FirstName} {source.LastName}";

// Single property access
private static decimal ConvertPrice(int priceInCents)
    => priceInCents / 100m;
```

## Nested Object Mapping

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Auto-detected nested maps | Auto-wired when matching forge method exists (v1.3+, `AutoWireNestedMappings = true` default) | `[ForgeWith]` still works as explicit override |
| `.IncludeMembers(s => s.Inner)` | Not directly supported | Use `[ForgeProperty]` with dot notation instead |

### Example

```csharp
// AutoMapper
CreateMap<Order, OrderDto>();
CreateMap<Address, AddressDto>();
// AutoMapper auto-discovers nested Address→AddressDto

// ForgeMap (v1.3+ — auto-wired, no [ForgeWith] needed)
[ForgeMap]
public partial class OrderForger
{
    public partial OrderDto Forge(Order source);
    public partial AddressDto Forge(Address source);
}
// The generator auto-discovers that Forge(Address) satisfies OrderDto.ShippingAddress.
// Use [ForgeWith] only when multiple forge methods match (FM0025 ambiguity).
```

## Mapping Inheritance & Polymorphic Dispatch

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `.IncludeBase<TBaseSrc, TBaseDst>()` | `[IncludeBaseForge(typeof(TBaseSrc), typeof(TBaseDst))]` | Inherits `[Ignore]`, `[ForgeProperty]`, `[ForgeFrom]`, `[ForgeWith]` from the base forge method; the base method must exist in the same forger class (FM0019) |
| `.Include<TDerivedSrc, TDerivedDst>()` | Not needed — `[ForgeAllDerived]` auto-discovers | No explicit registration; derived forge overloads must be declared in the same forger class and share the same method name to be auto-discovered |
| `.IncludeAllDerived()` | `[ForgeAllDerived]` | Generates polymorphic dispatch (`is` cascade), most-derived checked first; works with abstract/interface destinations (v1.3+, generates dispatch-only body with `NotSupportedException` fallback) |
| Inherited properties from compiled assemblies | Automatic (generator fix) | No configuration needed — base-type properties are discovered automatically |

### Configuration inheritance

`[IncludeBaseForge]` inherits property-level attributes from the base forge method:

| Attribute | Inherited? | Notes |
|---|---|---|
| `[Ignore]` | Yes | Ignored properties remain ignored |
| `[ForgeProperty]` | Yes | Renamed properties carry over |
| `[ForgeFrom]` | Yes | Custom resolvers apply to inherited properties |
| `[ForgeWith]` | Yes | Nested forge methods apply to inherited properties |
| `[BeforeForge]` / `[AfterForge]` | No | Hooks are method-specific; declare explicitly on derived |
| `[ReverseForge]` | No | Reverse generation is per-method |

Explicit attributes on the derived method **override** inherited attributes for the same property.

### Chaining

`[IncludeBaseForge]` can chain through multiple inheritance levels (Base → Middle → Leaf).

### Polymorphic dispatch example

```csharp
// AutoMapper
CreateMap<BaseEntity, BaseDto>()
    .ForMember(d => d.AuditTrail, o => o.Ignore())
    .IncludeAllDerived();
CreateMap<DerivedAEntity, DerivedADto>()
    .IncludeBase<BaseEntity, BaseDto>();
CreateMap<DerivedBEntity, DerivedBDto>()
    .IncludeBase<BaseEntity, BaseDto>();

// ForgeMap
[ForgeAllDerived]
[Ignore(nameof(BaseDto.AuditTrail))]
public partial BaseDto Forge(BaseEntity source);

[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
public partial DerivedADto Forge(DerivedAEntity source);

[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
public partial DerivedBDto Forge(DerivedBEntity source);
```

### Collection interop

When `[ForgeAllDerived]` is on a base forge method, collection forge methods for that base type dispatch each element polymorphically.

## Reverse Mapping

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `.ReverseMap()` | `[ReverseForge]` | Generates reverse method automatically |
| Reverse with customization | Limited — `[ForgeFrom]` cannot be auto-reversed | FM0012 warning emitted |

## Lifecycle Hooks

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `.BeforeMap((s,d) => ...)` | `[BeforeForge(nameof(MethodName))]` | `void Method(S source)` |
| `.AfterMap((s,d) => ...)` | `[AfterForge(nameof(MethodName))]` | `void Method(S source, D dest)` |

## Null Handling

### Source-object null handling

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Default: returns `default(TDestination)` for null source | `NullHandling.ReturnNull` (default) | Same behavior (default/null depending on destination type) |
| Custom null handling | `NullHandling.ThrowException` | Set on `[ForgeMap]` or `[ForgeMapDefaults]` |
| `.NullSubstitute(value)` | Not directly supported | Use `[AfterForge]` hook to substitute |

### Nullable-property handling

Controls how nullable-to-non-nullable **reference type** property assignments and constructor-parameter expressions are generated. Does not apply to value types, `[ForgeFrom]` resolvers, or `[ForgeWith]` nested mappings.

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Default (assigns null through / `AllowNullDestinationValues = true`) | `NullPropertyHandling.NullForgiving` (default) | `target.X = source.X!;` — same runtime behavior as AutoMapper (for both property assignments and ctor parameters) |
| `AllowNullCollections = false` | `NullPropertyHandling.CoalesceToDefault` | **Not a strict 1:1.** AutoMapper's setting is collection-only; `CoalesceToDefault` applies to all nullable-ref → non-nullable-ref properties where a default can be generated (`string` → `""`, arrays/concrete types with parameterless ctor → `new T()`). Other reference types fall back to `NullForgiving`. For collection-only coalescing, use per-property `[ForgeProperty(..., NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]` on the collection members only. |
| `.NullSubstitute(value)` per property | `[ForgeFrom]` resolver returning the substitute | No direct attribute; resolver has full control |
| No equivalent | `NullPropertyHandling.SkipNull` | `if (source.X is { } v) target.X = v;` — destination keeps its initialized value; falls back to `NullForgiving` for constructor parameters (can't omit required args) and init-only properties (can't conditionally assign after initialization) |
| No equivalent | `NullPropertyHandling.ThrowException` | `target.X = source.X ?? throw new ArgumentNullException(...)` — fail-fast |

**Three-tier configuration** — per-property > per-forger > assembly default:

```csharp
// Assembly-level default
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]

// Per-forger override
[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]

// Per-property override (omit NullPropertyHandling to inherit)
[ForgeProperty(nameof(Source.Tags), nameof(Dest.Tags),
    NullPropertyHandling = NullPropertyHandling.ThrowException)]
```

**FM0007 diagnostic** — ForgeMap reports a warning for nullable ref → non-nullable ref *direct* property assignments (by convention, `[ForgeProperty]`, or auto-flatten). It is not reported when the destination is mapped via `[ForgeFrom]` resolver or `[ForgeWith]` nested forging. Suppress with `SuppressDiagnostics = new[] { "FM0007" }`, `#pragma warning disable FM0007`, or `<NoWarn>FM0007</NoWarn>` in `.csproj`.

## Collections

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Auto collection mapping | Auto-generated when `GenerateCollectionMappings = true` on `[assembly: ForgeMapDefaults(...)]` (assembly-level, default `true`) | Supports `List<T>`, arrays, `IEnumerable<T>` |
| Auto nested collection mapping | Auto-wired inline (v1.3+, `AutoWireNestedMappings = true`) when element forge method exists | Supports `List<T>`, `IList<T>`, `T[]`, `HashSet<T>`, `IEnumerable<T>`, `IReadOnlyCollection<T>` — no explicit collection forge method needed |
| `.ProjectTo<D>(config)` | Not supported (compile-time only) | ForgeMap is source-generated, not queryable |

## DI Registration

| AutoMapper | ForgeMap |
|---|---|
| `services.AddAutoMapper(assemblies)` | `services.AddForgeMaps()` |
| `services.AddAutoMapper(cfg => ...)` | N/A — configuration is via attributes |
| `ServiceLifetime.Transient/Scoped/Singleton` | `services.AddForgeMaps(ServiceLifetime.Scoped)` |
| Inject `IMapper` | Inject the forger class directly (e.g., `AppForger`) |

## Enum Mapping

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Auto enum-to-enum by name | Auto by name | Forge method: `partial DEnum Forge(SEnum source)` |
| Enum-to-string | Auto string conversion | Forge method: `partial string Forge(SEnum source)` |
| Compatible enum mapping | Auto-converted when compatible | Distinct enum types with identical members, values, declaration order, and the same underlying integral type are cast automatically via their underlying type — including nullable variants; commonly cross-namespace but namespace difference is not required |

## Configuration Validation

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `AssertConfigurationIsValid()` | Compiler diagnostics (FM0001–FM0027) | Errors at compile time, not runtime |
| Unmapped property warnings | FM0005: Unmapped source property | Configurable via `SuppressDiagnostics` |

## Case-Insensitive Matching

| AutoMapper | ForgeMap |
|---|---|
| Default: case-insensitive, flattening | Default: case-sensitive; flattening via conventions and `[ForgeProperty]` |
| N/A | `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` |

## Assembly-Level Defaults

```csharp
// Apply defaults to all forgers in the assembly
[assembly: ForgeMapDefaults(
    NullHandling = NullHandling.ThrowException,
    PropertyMatching = PropertyMatching.ByNameCaseInsensitive,
    GenerateCollectionMappings = true,
    AutoWireNestedMappings = true,             // default; set false to require explicit [ForgeWith]
    NullPropertyHandling = NullPropertyHandling.NullForgiving  // default
)]
```

## Features NOT in ForgeMap (require workarounds)

| AutoMapper Feature | Workaround |
|---|---|
| `ProjectTo<T>()` (IQueryable) | Map in-memory after materializing the query |
| `ConstructUsing()` | No direct equivalent. ForgeMap maps constructor/record parameters when the destination has an accessible constructor; for custom factory logic, adjust destination constructors/records where possible or create the destination manually (e.g., in calling code, a `[ForgeFrom]` resolver, or a `[BeforeForge]` hook). |
| Conditional mapping (`.PreCondition()`) | Use `[BeforeForge]` to validate, or `[ForgeFrom]` with conditional logic |
| Dynamic/runtime mapping | Not supported — ForgeMap is compile-time only |

## Common Migration Patterns

### Pattern 1: Simple DTO mapping

```csharp
// BEFORE (AutoMapper)
public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>();
    }
}
// Usage: var dto = mapper.Map<UserDto>(user);

// AFTER (ForgeMap)
[ForgeMap]
public partial class UserForger
{
    public partial UserDto Forge(User source);
}
// Usage: var dto = forger.Forge(user);
```

### Pattern 2: Custom property mapping with ignore

```csharp
// BEFORE (AutoMapper)
CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, o => o.MapFrom(s => $"{s.First} {s.Last}"))
    .ForMember(d => d.Password, o => o.Ignore());

// AFTER (ForgeMap)
[ForgeFrom(nameof(UserDto.FullName), nameof(ResolveFullName))]
[Ignore(nameof(UserDto.Password))]
public partial UserDto Forge(User source);

private static string ResolveFullName(User source)
    => $"{source.First} {source.Last}";
```

### Pattern 3: Bidirectional mapping

```csharp
// BEFORE (AutoMapper)
CreateMap<Order, OrderDto>().ReverseMap();

// AFTER (ForgeMap)
[ReverseForge]
public partial OrderDto Forge(Order source);
// Generates both Forge(Order) → OrderDto and Forge(OrderDto) → Order
```

### Pattern 4: Nested object and collection mapping

```csharp
// BEFORE (AutoMapper)
CreateMap<Order, OrderDto>();
CreateMap<Address, AddressDto>();  // nested single object
CreateMap<LineItem, LineItemDto>(); // nested collection

// AFTER (ForgeMap v1.3+) — auto-wired, no [ForgeWith] needed
public partial OrderDto Forge(Order source);
public partial AddressDto Forge(Address source);
public partial LineItemDto Forge(LineItem source);
// The generator auto-discovers that Forge(Address) maps the ShippingAddress property,
// and generates inline List<LineItem> → List<LineItemDto> iteration using Forge(LineItem).
// Use [ForgeWith] only to disambiguate when multiple forge methods could match (FM0025).
```

### Pattern 5: DI registration

```csharp
// BEFORE (AutoMapper)
services.AddAutoMapper(typeof(Program).Assembly);
// Inject: IMapper mapper

// AFTER (ForgeMap)
services.AddForgeMaps();
// Inject: AppForger forger
```

### Pattern 6: Before/After mapping hooks

```csharp
// BEFORE (AutoMapper)
CreateMap<Order, OrderDto>()
    .BeforeMap((s, d) => ValidateOrder(s))
    .AfterMap((s, d) => d.Total = d.Items.Sum(i => i.Price));

// AFTER (ForgeMap)
[BeforeForge(nameof(ValidateOrder))]
[AfterForge(nameof(CalculateTotal))]
public partial OrderDto Forge(Order source);

private void ValidateOrder(Order source) { /* ... */ }
private void CalculateTotal(Order source, OrderDto dest)
    => dest.Total = dest.Items.Sum(i => i.Price);
```

### Pattern 7: Map into existing object

```csharp
// BEFORE (AutoMapper)
mapper.Map(source, existingDest);

// AFTER (ForgeMap) — method name is a convention; the generator
// recognizes any void partial method with a [UseExistingValue] parameter.
[ForgeMap]
public partial class AppForger
{
    public partial void ForgeInto(Source source, [UseExistingValue] Dest destination);
}
forger.ForgeInto(source, existingDest);
```

### Pattern 8: Inheritance hierarchy with polymorphic dispatch

```csharp
// BEFORE (AutoMapper)
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<BaseEntity, BaseDto>()
            .ForMember(d => d.AuditTrail, o => o.Ignore())
            .IncludeAllDerived();

        CreateMap<ChildEntity, ChildDto>()
            .IncludeBase<BaseEntity, BaseDto>();

        CreateMap<GrandChildEntity, GrandChildDto>()
            .IncludeBase<BaseEntity, BaseDto>();
    }
}
// Usage — polymorphic: mapper.Map<BaseDto>(anyEntity)

// AFTER (ForgeMap)
[ForgeMap]
public partial class AppForger
{
    [ForgeAllDerived]
    [Ignore(nameof(BaseDto.AuditTrail))]
    public partial BaseDto Forge(BaseEntity source);

    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial ChildDto Forge(ChildEntity source);

    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial GrandChildDto Forge(GrandChildEntity source);
}
// Usage — polymorphic: forger.Forge(anyEntity)
```

### Pattern 9: Nullable-to-non-nullable property mapping

```csharp
// BEFORE (AutoMapper)
// AutoMapper assigns null through by default.
// Some projects use AllowNullCollections = false for safety:
CreateMap<Assessment, SnapshotDto>();
// cfg.AllowNullCollections = false; // in MapperConfiguration

// AFTER (ForgeMap) — default NullForgiving matches AutoMapper default
[ForgeMap]
public partial class AppForger
{
    public partial SnapshotDto Forge(Assessment source);
}
// Effectively: target.Tags = source.Tags!;

// AFTER (ForgeMap) — CoalesceToDefault matches AllowNullCollections = false
[ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
public partial class AppForger
{
    public partial SnapshotDto Forge(Assessment source);
}
// Effectively: target.Tags = source.Tags ?? new List<string>();

// Per-property override for mixed strategies
[ForgeMap]
public partial class AppForger
{
    [ForgeProperty(nameof(Assessment.Tags), nameof(SnapshotDto.Tags),
        NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]
    [ForgeProperty(nameof(Assessment.AuditLog), nameof(SnapshotDto.AuditLog),
        NullPropertyHandling = NullPropertyHandling.ThrowException)]
    public partial SnapshotDto Forge(Assessment source);
}
```
