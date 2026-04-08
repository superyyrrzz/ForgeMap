# Quick Start

This guide walks you through creating your first ForgeMap mapping in under 3 minutes.

## 1. Define Your Types

```csharp
public class OrderEntity
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public DateTime OrderDate { get; set; }
}

public class OrderDto
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public DateTime OrderDate { get; set; }
}
```

## 2. Create a Forger

A **forger** is a partial class decorated with `[ForgeMap]`. Each partial method declares a mapping:

```csharp
using ForgeMap;

[ForgeMap]
public partial class AppForger
{
    public partial OrderDto Forge(OrderEntity source);
}
```

At compile time, ForgeMap generates the method body that copies each matching property by name.

## 3. Use It

```csharp
var forger = new AppForger();

var entity = new OrderEntity
{
    Id = "ORD-001",
    CustomerName = "Acme Corp",
    OrderDate = DateTime.UtcNow
};

OrderDto dto = forger.Forge(entity);
// dto.Id == "ORD-001"
// dto.CustomerName == "Acme Corp"
// dto.OrderDate == entity.OrderDate
```

## What Just Happened?

The ForgeMap source generator:
1. Found the `[ForgeMap]` class and its partial methods
2. Matched properties by name between `OrderEntity` and `OrderDto`
3. Generated a `.g.cs` file with the mapping implementation

The generated code is equivalent to:

```csharp
public partial OrderDto Forge(OrderEntity source)
{
    if (source is null) return null;
    return new OrderDto
    {
        Id = source.Id,
        CustomerName = source.CustomerName,
        OrderDate = source.OrderDate,
    };
}
```

No reflection. No runtime overhead. Just straight property assignments.

## Next Steps

- [Your First Forger](your-first-forger.md) — property renaming, ignoring, nested objects
- [Diagnostics Reference](../reference/diagnostics.md) — understand compiler warnings
- [Migration from AutoMapper](../migration/from-automapper.md) — switching from AutoMapper
