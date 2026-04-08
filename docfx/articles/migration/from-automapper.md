# Migrating from AutoMapper

This guide shows how to replace AutoMapper with ForgeMap in your .NET project. Each section maps an AutoMapper concept to its ForgeMap equivalent with before/after code examples.

## Core Concepts

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `Profile` class | `[ForgeMap]` partial class | One partial class per forger group |
| `CreateMap<S,D>()` | `partial D Forge(S source);` | Each mapping is a partial method |
| `IMapper` (injected) | Forger class (injected) | Register via `services.AddForgeMaps()` |
| `mapper.Map<D>(src)` | `forger.Forge(src)` | Direct method call |
| `mapper.Map(src, dest)` | `forger.ForgeInto(src, dest)` | Void method with `[UseExistingValue]` parameter |

## Step-by-Step Migration

### 1. Install ForgeMap

```bash
dotnet add package ForgeMap
```

### 2. Create a Forger Class

Replace each AutoMapper `Profile` with a `[ForgeMap]` partial class:

```csharp
// BEFORE (AutoMapper)
public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>();
    }
}

// AFTER (ForgeMap)
[ForgeMap]
public partial class UserForger
{
    public partial UserDto Forge(User source);
}
```

### 3. Replace DI Registration

```csharp
// BEFORE
services.AddAutoMapper(typeof(Program).Assembly);

// AFTER
services.AddForgeMaps();
```

### 4. Update Injection Sites

```csharp
// BEFORE
public class UserService(IMapper mapper) { ... }

// AFTER
public class UserService(UserForger forger) { ... }
```

## Property Mapping

| AutoMapper | ForgeMap |
|---|---|
| Auto by name | Auto by name (default) |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.A.B))` | `[ForgeProperty("A.B", nameof(D.X))]` |
| Flattening (`CustomerName` from `Customer.Name`) | Auto-flattened by convention |

### Example: Custom property mapping with ignore

```csharp
// BEFORE
CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, o => o.MapFrom(s => $"{s.First} {s.Last}"))
    .ForMember(d => d.Password, o => o.Ignore());

// AFTER
[ForgeFrom(nameof(UserDto.FullName), nameof(ResolveFullName))]
[Ignore(nameof(UserDto.Password))]
public partial UserDto Forge(User source);

private static string ResolveFullName(User source)
    => $"{source.First} {source.Last}";
```

## Custom Resolution

| AutoMapper | ForgeMap |
|---|---|
| `IValueResolver<S,D,TVal>` | `[ForgeFrom(nameof(D.Prop), nameof(Method))]` |
| `.MapFrom(s => expr)` | `[ForgeFrom(...)]` with resolver method |
| `ITypeConverter<S,D>` | `[ConvertWith(typeof(Converter))]` |

Resolver methods live on the forger class:

```csharp
// Full source access
private static string ResolveFullName(UserEntity source)
    => $"{source.FirstName} {source.LastName}";

// Single property access
private static decimal ConvertPrice(int priceInCents)
    => priceInCents / 100m;
```

## Nested Objects

ForgeMap v1.3+ auto-wires nested mappings when a matching forge method exists in the same forger:

```csharp
// BEFORE
CreateMap<Order, OrderDto>();
CreateMap<Address, AddressDto>();

// AFTER — auto-wired, no [ForgeWith] needed
[ForgeMap]
public partial class OrderForger
{
    public partial OrderDto Forge(Order source);
    public partial AddressDto Forge(Address source);
}
```

Use `[ForgeWith]` to disambiguate when multiple forge methods match (FM0025).

## Reverse Mapping

```csharp
// BEFORE
CreateMap<Order, OrderDto>().ReverseMap();

// AFTER
[ReverseForge]
public partial OrderDto Forge(Order source);
```

> [!NOTE]
> `[ForgeFrom]` resolvers cannot be auto-reversed (FM0012). Add manual reverse mappings for those properties.

## Lifecycle Hooks

| AutoMapper | ForgeMap |
|---|---|
| `.BeforeMap((s,d) => ...)` | `[BeforeForge(nameof(Method))]` — `void Method(S source)` |
| `.AfterMap((s,d) => ...)` | `[AfterForge(nameof(Method))]` — `void Method(S source, D dest)` |

```csharp
// BEFORE
CreateMap<Order, OrderDto>()
    .BeforeMap((s, d) => ValidateOrder(s))
    .AfterMap((s, d) => d.Total = d.Items.Sum(i => i.Price));

// AFTER
[BeforeForge(nameof(ValidateOrder))]
[AfterForge(nameof(CalculateTotal))]
public partial OrderDto Forge(Order source);

private void ValidateOrder(Order source) { /* ... */ }
private void CalculateTotal(Order source, OrderDto dest)
    => dest.Total = dest.Items.Sum(i => i.Price);
```

## Inheritance & Polymorphic Dispatch

```csharp
// BEFORE
CreateMap<BaseEntity, BaseDto>()
    .ForMember(d => d.AuditTrail, o => o.Ignore())
    .IncludeAllDerived();
CreateMap<ChildEntity, ChildDto>()
    .IncludeBase<BaseEntity, BaseDto>();

// AFTER
[ForgeAllDerived]
[Ignore(nameof(BaseDto.AuditTrail))]
public partial BaseDto Forge(BaseEntity source);

[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
public partial ChildDto Forge(ChildEntity source);
```

`[IncludeBaseForge]` inherits `[Ignore]`, `[ForgeProperty]`, `[ForgeFrom]`, and `[ForgeWith]` from the base method. Explicit attributes on the derived method override inherited ones.

## Map Into Existing Object

```csharp
// BEFORE
mapper.Map(source, existingDest);

// AFTER
[ForgeMap]
public partial class AppForger
{
    public partial void ForgeInto(Source source, [UseExistingValue] Dest destination);
}
forger.ForgeInto(source, existingDest);
```

For nested in-place updates with EF Core change tracking:

```csharp
[ForgeProperty("Items", "Items", ExistingTarget = true,
    CollectionUpdate = CollectionUpdateStrategy.Sync, KeyProperty = "Id")]
public partial void ForgeInto(OrderUpdateDto source, [UseExistingValue] Order target);
```

## Null Handling

| AutoMapper | ForgeMap |
|---|---|
| Default (assigns null through) | `NullPropertyHandling.NullForgiving` (default) |
| `AllowNullCollections = false` | `NullPropertyHandling.CoalesceToDefault` |
| Null → new T() via AfterMap | `NullPropertyHandling.CoalesceToNew` (v1.5+) |
| No equivalent | `NullPropertyHandling.SkipNull` |
| No equivalent | `NullPropertyHandling.ThrowException` |

Three-tier configuration: per-property > per-forger > assembly default:

```csharp
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]

[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial class AppForger { ... }

[ForgeProperty("Tags", "Tags", NullPropertyHandling = NullPropertyHandling.ThrowException)]
```

## Collections

| AutoMapper | ForgeMap |
|---|---|
| Auto collection mapping | Auto-generated (default on) |
| `mapper.Map<List<D>>(list)` | Standalone collection method (v1.5+) |
| `.ProjectTo<D>(config)` | Not supported (compile-time only) |

```csharp
// Standalone collection mapping (v1.5+)
public partial UserDto Forge(User source);
public partial IReadOnlyList<UserDto> ForgeAll(IEnumerable<User> source);
```

## String-to-Enum Conversion

```csharp
// BEFORE
CreateMap<Ticket, TicketDto>()
    .ForMember(d => d.Priority, o => o.MapFrom(s => Enum.Parse<Priority>(s.Priority, true)));

// AFTER — auto-converted, no configuration needed (v1.4+)
public partial TicketDto Forge(TicketEntity source);
```

Configure behavior via `StringToEnum` on `[ForgeMap]` or `[ForgeMapDefaults]`.

## [ConvertWith] Type Converters

```csharp
// BEFORE
CreateMap<Request, StorageModel>()
    .ConvertUsing<RequestConverter>();

// AFTER — type-based
[ConvertWith(typeof(RequestConverter))]
public partial StorageModel Forge(Request source);

// AFTER — member-based (DI-injected)
[ConvertWith(nameof(_converter))]
public partial StorageModel Forge(Request source);
```

## Features Not in ForgeMap

| AutoMapper Feature | Workaround |
|---|---|
| `ProjectTo<T>()` (IQueryable) | Map in-memory after materializing |
| `ConstructUsing()` | Adjust constructors or create manually before `ForgeInto` |
| Conditional mapping (`.PreCondition()`) | Use `[BeforeForge]` or `[ForgeFrom]` with conditional logic |
| Dynamic/runtime mapping | Not supported — ForgeMap is compile-time only |

## Validation

AutoMapper's `AssertConfigurationIsValid()` is replaced by compile-time diagnostics. All mapping errors are caught during build, not at runtime. See the [Diagnostics Reference](../reference/diagnostics.md) for the full list.
