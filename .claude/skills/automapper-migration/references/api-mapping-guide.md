# AutoMapper → ForgeMap API Migration Guide

This reference maps AutoMapper patterns to their ForgeMap equivalents.

## Core Concepts

| AutoMapper Concept | ForgeMap Equivalent | Notes |
|---|---|---|
| `Profile` class | `[ForgeMap]` partial class | ForgeMap uses one partial class per "forger" group |
| `CreateMap<S,D>()` | `partial D Forge(S source);` method | Each mapping is a partial method declaration |
| `IMapper` (injected) | Forger class (injected) | Register via `services.AddForgeMaps()` |
| `mapper.Map<D>(src)` | `forger.Forge(src)` | Direct method call |
| `mapper.Map(src, dest)` | `forger.ForgeInto(src, dest)` | Uses `[UseExistingValue]` parameter |

## Property Mapping

| AutoMapper | ForgeMap | Example |
|---|---|---|
| Auto by name | Auto by name (default) | Same-name properties map automatically |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` | Attribute on the forge method |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` | Attribute on the forge method |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.A.B))` | `[ForgeProperty("A.B", nameof(D.X))]` | Dot notation for nested access |
| Flattening (`Order.CustomerName` from `Order.Customer.Name`) | Auto-flattened by default | ForgeMap auto-flattens (e.g., `CustomerName` → `Customer.Name`); use `[ForgeProperty("Customer.Name", nameof(OrderDto.CustomerName))]` only when auto-flatten fails to match |

## Custom Resolution

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `IValueResolver<S,D,TVal>` | `[ForgeFrom(nameof(D.DestProp), nameof(ResolverMethod))]` | Static/instance method on the forger class |
| `.MapFrom(s => expr)` | `[ForgeFrom(nameof(D.DestProp), nameof(Method))]` | Resolver method returns value |
| `ITypeConverter<S,D>` | `[ConvertWith(typeof(Converter))]` | Implements `ITypeConverter<S,D>`. **Requires ForgeMap 1.1+** — use `[ForgeFrom]` resolvers on 1.0 |

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
| Auto-detected nested maps | `[ForgeWith(nameof(D.DestProp), nameof(NestedForgeMethod))]` | Must declare a forge method for the nested type |
| `.IncludeMembers(s => s.Inner)` | Not directly supported | Use `[ForgeProperty]` with dot notation instead |

### Example

```csharp
// AutoMapper
CreateMap<Order, OrderDto>();
CreateMap<Address, AddressDto>();
// AutoMapper auto-discovers nested Address→AddressDto

// ForgeMap
[ForgeMap]
public partial class OrderForger
{
    [ForgeWith(nameof(OrderDto.ShippingAddress), nameof(ForgeAddress))]
    public partial OrderDto Forge(Order source);

    public partial AddressDto ForgeAddress(Address source);
}
```

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

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Default: returns `null` for null source | `NullHandling.ReturnNull` (default) | Same behavior |
| Custom null handling | `NullHandling.ThrowException` | Set on `[ForgeMap]` or `[ForgeMapDefaults]` |
| `.NullSubstitute(value)` | Not directly supported | Use `[AfterForge]` hook to substitute |

## Collections

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| Auto collection mapping | Auto-generated when `GenerateCollectionMappings = true` (default) | Supports `List<T>`, arrays, `IEnumerable<T>` |
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

## Configuration Validation

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `AssertConfigurationIsValid()` | Compiler diagnostics (FM0001–FM0018) | Errors at compile time, not runtime |
| Unmapped property warnings | FM0005: Unmapped source property | Configurable via `SuppressDiagnostics` |

## Case-Insensitive Matching

| AutoMapper | ForgeMap |
|---|---|
| Default: case-insensitive, flattening | Default: case-sensitive, auto-flatten enabled |
| N/A | `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` |

## Assembly-Level Defaults

```csharp
// Apply defaults to all forgers in the assembly
[assembly: ForgeMapDefaults(
    NullHandling = NullHandling.ThrowException,
    PropertyMatching = PropertyMatching.ByNameCaseInsensitive,
    GenerateCollectionMappings = true
)]
```

## Features NOT in ForgeMap (require workarounds)

| AutoMapper Feature | Workaround |
|---|---|
| `ProjectTo<T>()` (IQueryable) | Map in-memory after materializing the query |
| `ConstructUsing()` | Use record types with constructor mapping (auto-detected) |
| Conditional mapping (`.PreCondition()`) | Use `[BeforeForge]` to validate, or `[ForgeFrom]` with conditional logic |
| Value converters (global type conversion) | `[ConvertWith]` per method (**requires v1.1+**), or `[ForgeFrom]` resolvers (v1.0) |
| Mapping inheritance (`.Include<>()`, `.IncludeBase<>()`) | Declare separate forge methods, use `[ForgeWith]` for shared parts |
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

### Pattern 4: Nested object mapping

```csharp
// BEFORE (AutoMapper)
CreateMap<Order, OrderDto>();
CreateMap<LineItem, LineItemDto>();

// AFTER (ForgeMap)
[ForgeWith(nameof(OrderDto.Items), nameof(ForgeItem))]
public partial OrderDto Forge(Order source);

public partial LineItemDto ForgeItem(LineItem source);
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

// AFTER (ForgeMap)
[ForgeMap]
public partial class AppForger
{
    public partial void ForgeInto(Source source, [UseExistingValue] Dest destination);
}
forger.ForgeInto(source, existingDest);
```
