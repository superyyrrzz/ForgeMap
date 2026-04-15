# Migrating from AutoMapper to ForgeMap

This guide shows how to replace AutoMapper with ForgeMap in your .NET project. ForgeMap is a Roslyn source generator — all mapping code is generated at compile time with zero runtime reflection.

For a detailed feature comparison, see [ForgeMap vs AutoMapper & Mapperly](ForgeMap-vs-AutoMapper-and-Mapperly.md).

## Step 1: Install ForgeMap

```bash
dotnet remove package AutoMapper
dotnet remove package AutoMapper.Extensions.Microsoft.DependencyInjection  # if present
dotnet add package ForgeMap
```

## Step 2: Replace Profiles with Mapper Classes

AutoMapper uses `Profile` classes with `CreateMap<S, D>()`. ForgeMap uses `[ForgeMap]` partial classes with method signatures.

**Before (AutoMapper):**
```csharp
public class UserProfile : Profile
{
    public UserProfile()
    {
        CreateMap<User, UserDto>();
        CreateMap<Address, AddressDto>();
    }
}
```

**After (ForgeMap):**
```csharp
[ForgeMap]
public partial class AppMapper
{
    public partial UserDto Forge(User source);
    public partial AddressDto Forge(Address source);
}
```

The generator auto-discovers that `Forge(Address)` should be used for the `Address` property on `User` — no extra configuration needed.

## Step 3: Replace Mapping Calls

| AutoMapper | ForgeMap |
|---|---|
| `mapper.Map<UserDto>(user)` | `mapper.Forge(user)` |
| `mapper.Map(source, dest)` | `mapper.ForgeInto(source, dest)` * |
| `mapper.Map<List<UserDto>>(users)` | `users.Select(u => mapper.Forge(u)).ToList()` |

\* `ForgeInto` is a naming convention, not a built-in method. You must declare it as a `partial void` method with a `[UseExistingValue]` parameter — see [Map Into Existing Object](#map-into-existing-object) below.

**Injection changes:** AutoMapper injects `IMapper`. ForgeMap mappers are plain classes — you can either inject them via DI (`services.AddForgeMaps()`) or use static instances:

```csharp
// Option A: DI
services.AddForgeMaps();
// constructor: public MyService(AppMapper mapper)

// Option B: Static instance (simpler, no DI needed)
private static readonly AppMapper _mapper = new();
```

## Common Patterns

### Property Renaming

```csharp
// AutoMapper
CreateMap<Order, OrderDto>()
    .ForMember(d => d.OrderDate, o => o.MapFrom(s => s.PlacedAt));

// ForgeMap
[ForgeProperty(nameof(Order.PlacedAt), nameof(OrderDto.OrderDate))]
public partial OrderDto Forge(Order source);
```

### Ignoring Properties

```csharp
// AutoMapper
CreateMap<User, UserDto>()
    .ForMember(d => d.PasswordHash, o => o.Ignore());

// ForgeMap
[Ignore(nameof(UserDto.PasswordHash))]
public partial UserDto Forge(User source);
```

### Custom Value Resolution

```csharp
// AutoMapper
CreateMap<User, UserDto>()
    .ForMember(d => d.FullName, o => o.MapFrom(s => $"{s.First} {s.Last}"));

// ForgeMap
[ForgeFrom(nameof(UserDto.FullName), nameof(ResolveFullName))]
public partial UserDto Forge(User source);

private static string ResolveFullName(User source) => $"{source.First} {source.Last}";
```

### Reverse Mapping

```csharp
// AutoMapper
CreateMap<Order, OrderDto>().ReverseMap();

// ForgeMap
[ReverseForge]
public partial OrderDto Forge(Order source);
// Generates both Forge(Order) → OrderDto and Forge(OrderDto) → Order
```

### Nested Objects

ForgeMap auto-wires nested mappings when a matching `Forge` method exists in the same class:

```csharp
[ForgeMap]
public partial class AppMapper
{
    public partial OrderDto Forge(Order source);      // auto-wires Address and LineItem
    public partial AddressDto Forge(Address source);
    public partial LineItemDto Forge(LineItem source);
}
```

### Map Into Existing Object

```csharp
// AutoMapper
mapper.Map(source, existingDest);

// ForgeMap
public partial void ForgeInto(UserDto source, [UseExistingValue] User destination);
```

### Enum Mapping

ForgeMap handles enum-to-enum, enum-to-string, and string-to-enum automatically:

```csharp
// Just declare the method — no configuration needed
public partial StatusDto Forge(StatusEntity source);  // enum → enum by name
```

### Per-Property Value Conversion

```csharp
// AutoMapper
CreateMap<Product, ProductDto>()
    .ForMember(d => d.PriceText, o => o.MapFrom(s => $"${s.Price:F2}"));

// ForgeMap
[ForgeProperty(nameof(Product.Price), nameof(ProductDto.PriceText), ConvertWith = nameof(FormatPrice))]
public partial ProductDto Forge(Product source);

private static string FormatPrice(decimal price) => $"${price:F2}";
```

### Before/After Hooks

```csharp
// AutoMapper
CreateMap<Order, OrderDto>()
    .AfterMap((s, d) => d.Total = d.Items.Sum(i => i.Price));

// ForgeMap
[AfterForge(nameof(CalculateTotal))]
public partial OrderDto Forge(Order source);

private static void CalculateTotal(Order source, OrderDto dest)
    => dest.Total = dest.Items.Sum(i => i.Price);
```

### Type Converter (Full Delegation)

```csharp
// AutoMapper
CreateMap<Event, StorageModel>().ConvertUsing<EventConverter>();

// ForgeMap
[ConvertWith(typeof(EventConverter))]
public partial StorageModel Forge(Event source);
// EventConverter must implement ITypeConverter<Event, StorageModel>
```

### Polymorphic / Inheritance Mapping

```csharp
// AutoMapper
CreateMap<BaseEntity, BaseDto>().IncludeAllDerived();
CreateMap<ChildEntity, ChildDto>().IncludeBase<BaseEntity, BaseDto>();

// ForgeMap
[ForgeAllDerived]
[Ignore(nameof(BaseDto.AuditTrail))]
public partial BaseDto Forge(BaseEntity source);

[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
public partial ChildDto Forge(ChildEntity source);
```

## DI Registration

```csharp
// AutoMapper
services.AddAutoMapper(typeof(Program).Assembly);

// ForgeMap
services.AddForgeMaps();
```

## What ForgeMap Doesn't Support

| AutoMapper Feature | Alternative |
|---|---|
| `ProjectTo<T>()` (IQueryable) | Materialize the query first, then map in-memory |
| `ConstructUsing()` | Use `[ConvertWith]` or `[ForgeFrom]` |
| `.Condition()` per member | Use `[AfterForge]` hook or `[ForgeFrom]` with conditional logic |
| Dynamic/runtime mapping | ForgeMap is compile-time only |
| `ForAllMaps()` global conventions | Apply attributes per mapper — no global convention system |

## Null Property Handling

AutoMapper assigns null through by default. ForgeMap provides 5 `NullPropertyHandling` strategies that control how nullable-to-non-nullable **reference type** property assignments are generated. (For null *source objects*, ForgeMap returns `null`/`default` by default — configurable via `NullHandling.ThrowException`.)

| Strategy | Behavior |
|---|---|
| `NullForgiving` (default) | Same as AutoMapper — assigns null through |
| `SkipNull` | Keeps destination's current value when source property is null |
| `CoalesceToDefault` | Null → type-appropriate default (`""` for strings, `Array.Empty<T>()` for arrays, `new List<T>()`/`new Dictionary<K,V>()` for collections) |
| `CoalesceToNew` | Null → `new T()` for any type with parameterless constructor |
| `ThrowException` | Throws `ArgumentNullException` |

Configure at assembly, per-mapper, or per-property level:

```csharp
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]

[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial class StrictMapper { ... }

[ForgeProperty("Tags", "Tags", NullPropertyHandling = NullPropertyHandling.ThrowException)]
public partial OrderDto Forge(Order source);
```
