# Your First Forger

This guide builds on the [Quick Start](quick-start.md) with real-world patterns: renaming properties, ignoring fields, mapping nested objects, and using dependency injection.

## Renaming Properties

When source and destination property names differ, use `[ForgeProperty]`:

```csharp
public class UserEntity
{
    public int UserId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public string PasswordHash { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string DisplayName { get; set; }
}
```

```csharp
[ForgeMap]
public partial class AppForger
{
    [ForgeProperty(nameof(UserEntity.UserId), nameof(UserDto.Id))]
    [ForgeFrom(nameof(UserDto.DisplayName), nameof(ResolveDisplayName))]
    public partial UserDto Forge(UserEntity source);

    private static string ResolveDisplayName(UserEntity source)
        => $"{source.FirstName} {source.LastName}";
}
```

Key points:
- `[ForgeProperty]` renames: `UserId` → `Id`
- `[ForgeFrom]` computes: `DisplayName` from a resolver method
- `PasswordHash` is not mapped because `UserDto` has no matching property — ForgeMap emits an FM0005 warning

## Ignoring Properties

Use `[Ignore]` to suppress warnings for intentionally unmapped destination properties:

```csharp
public class AuditDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime? LastModified { get; set; }  // Populated elsewhere
}
```

```csharp
[Ignore(nameof(AuditDto.LastModified))]
public partial AuditDto Forge(UserEntity source);
```

Without `[Ignore]`, the compiler emits FM0006 (unmapped destination property).

## Nested Objects

When your models contain nested objects, declare forge methods for each type. ForgeMap v1.3+ auto-wires them:

```csharp
public class Order
{
    public string Id { get; set; }
    public Address ShippingAddress { get; set; }
    public List<LineItem> Items { get; set; }
}

public class Address { public string City { get; set; } public string Zip { get; set; } }
public class LineItem { public string Sku { get; set; } public int Qty { get; set; } }

// DTOs
public class OrderDto
{
    public string Id { get; set; }
    public AddressDto ShippingAddress { get; set; }
    public List<LineItemDto> Items { get; set; }
}

public class AddressDto { public string City { get; set; } public string Zip { get; set; } }
public class LineItemDto { public string Sku { get; set; } public int Qty { get; set; } }
```

```csharp
[ForgeMap]
public partial class AppForger
{
    public partial OrderDto Forge(Order source);
    public partial AddressDto Forge(Address source);
    public partial LineItemDto Forge(LineItem source);
}
```

The generator automatically discovers that `Forge(Address)` satisfies `OrderDto.ShippingAddress`, and generates inline iteration for `List<LineItem>` → `List<LineItemDto>`.

## Reverse Mapping

Add `[ReverseForge]` to generate the inverse mapping:

```csharp
[ReverseForge]
public partial OrderDto Forge(Order source);
// Also generates: partial Order Forge(OrderDto source);
```

## Dependency Injection

Register all forgers with one call:

```csharp
// In Program.cs or Startup.cs
services.AddForgeMaps();
```

Then inject the forger:

```csharp
public class OrderService
{
    private readonly AppForger _forger;

    public OrderService(AppForger forger) => _forger = forger;

    public OrderDto GetOrder(int id)
    {
        var entity = _repository.Get(id);
        return _forger.Forge(entity);
    }
}
```

## Common Diagnostics

As you build forgers, you'll encounter these diagnostics:

| Code | Meaning | Fix |
|------|---------|-----|
| FM0001 | Class not partial | Add `partial` to the class declaration |
| FM0002 | Method not partial | Add `partial` to the forge method |
| FM0005 | Unmapped source property | Safe to ignore, or add a mapping |
| FM0006 | Unmapped destination property | Add `[Ignore]` or map the property |
| FM0007 | Nullable → non-nullable | Set `NullPropertyHandling` strategy |

See the full [Diagnostics Reference](../reference/diagnostics.md) for all 45 rules.
