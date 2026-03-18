using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ForgeMap;
using Xunit;

namespace ForgeMap.Tests;

#region Test Models

public class OrderEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string InternalCode { get; set; } = string.Empty;
}

public class OrderDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public string InternalCode { get; set; } = string.Empty;
}

public class UserEntity
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
}

public class ProductEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}

public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Description { get; set; } = string.Empty; // Unmapped
}

// v0.2 Test Models

public class OrderEntityV2
{
    public string OrderId { get; set; } = string.Empty;
    public DateTime PlacedAt { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}

public class OrderDtoV2
{
    public string Id { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalWithTax { get; set; }
    public string CustomerName { get; set; } = string.Empty;
}

public class ProductEntityV2
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
}

public class ProductDtoV2
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class CustomerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class NestedOrderEntity
{
    public int Id { get; set; }
    public CustomerInfo? Customer { get; set; }
}

public class FlatOrderDto
{
    public int Id { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
}

public class InvoiceEntity
{
    public int InvoiceNumber { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
}

public class InvoiceDisplayDto
{
    public string DisplayNumber { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string FormattedDate { get; set; } = string.Empty;
}

// v0.2 Nullable Test Models

public class OrderWithNullables
{
    public int Id { get; set; }
    public DateTime? ShippedAt { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? Quantity { get; set; }
}

public class OrderWithRequiredDates
{
    public int Id { get; set; }
    public DateTime ShippedAt { get; set; }
    public decimal DiscountPercent { get; set; }
    public int Quantity { get; set; }
}

public class OrderWithOptionalDates
{
    public int Id { get; set; }
    public DateTime? ShippedAt { get; set; }
    public decimal? DiscountPercent { get; set; }
    public int? Quantity { get; set; }
}

public class SimpleOrder
{
    public int Id { get; set; }
    public DateTime ShippedAt { get; set; }
    public decimal DiscountPercent { get; set; }
    public int Quantity { get; set; }
}

// v0.3 Test Models - ForgeWith (nested objects)

public class AddressEntity
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class UserWithAddressEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressEntity? Address { get; set; }
}

public class UserWithAddressDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressDto? Address { get; set; }
}

// v0.4 Test Models - Enums

public enum OrderStatus { Pending, Processing, Shipped, Delivered }
public enum OrderStatusDto { Pending, Processing, Shipped, Delivered }

// v0.4 Test Models - Constructor mapping

public record OrderRecordDto(string Id, string CustomerName, DateTime OrderDate);

public class ImmutableOrder
{
    public ImmutableOrder(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; }
    public string Name { get; }
}

public class HybridOrder
{
    public HybridOrder(int id)
    {
        Id = id;
    }

    public int Id { get; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OrderForCtorEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OrderForRecordEntity
{
    public string Id { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
}

// v0.4 Test Models - Flattening

public class CompanyInfo
{
    public string Name { get; set; } = string.Empty;
    public AddressEntity? Address { get; set; }
}

public class EmployeeEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CompanyInfo? Company { get; set; }
}

public class FlatEmployeeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string? CompanyAddressCity { get; set; }
}

// v0.5 Test Models - ReverseForge

public class ArticleEntity
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string InternalNote { get; set; } = string.Empty;
}

public class ArticleDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string InternalNote { get; set; } = string.Empty;
}

public class BookEntity
{
    public int Id { get; set; }
    public string BookTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

public class BookDto
{
    public int Id { get; set; }
    public string DisplayTitle { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
}

public class TagEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class TagDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string ComputedLabel { get; set; } = string.Empty;
}

public class ContactEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressEntity? HomeAddress { get; set; }
}

public class ContactDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressDto? HomeAddress { get; set; }
}

#endregion

#region Forgers

[ForgeMap]
public partial class AppForger
{
    /// <summary>
    /// Simple property mapping - all properties matched by name.
    /// </summary>
    public partial OrderDto Forge(OrderEntity source);

    /// <summary>
    /// Mapping with ignored properties - sensitive data excluded.
    /// </summary>
    [Ignore(nameof(UserDto.PasswordHash), nameof(UserDto.SecurityStamp))]
    public partial UserDto Forge(UserEntity source);

    /// <summary>
    /// Mapping with single ignored property.
    /// </summary>
    [Ignore(nameof(ProductDto.Description))]
    public partial ProductDto Forge(ProductEntity source);

    /// <summary>
    /// ForgeInto pattern - updates existing destination instance.
    /// </summary>
    public partial void ForgeInto(OrderEntity source, [UseExistingValue] OrderDto destination);

    /// <summary>
    /// ForgeInto with ignored properties.
    /// </summary>
    [Ignore(nameof(UserDto.PasswordHash))]
    public partial void ForgeInto(UserEntity source, [UseExistingValue] UserDto destination);

    // v0.2 Features

    /// <summary>
    /// ForgeProperty - maps source properties to differently named destination properties.
    /// </summary>
    [ForgeProperty(nameof(OrderEntityV2.OrderId), nameof(OrderDtoV2.Id))]
    [ForgeProperty(nameof(OrderEntityV2.PlacedAt), nameof(OrderDtoV2.OrderDate))]
    [ForgeFrom(nameof(OrderDtoV2.TotalWithTax), nameof(CalculateTotalWithTax))]
    public partial OrderDtoV2 Forge(OrderEntityV2 source);

    /// <summary>
    /// Simple ForgeProperty mapping.
    /// </summary>
    [ForgeProperty(nameof(ProductEntityV2.Title), nameof(ProductDtoV2.Name))]
    [ForgeProperty(nameof(ProductEntityV2.UnitPrice), nameof(ProductDtoV2.Price))]
    public partial ProductDtoV2 Forge(ProductEntityV2 source);

    /// <summary>
    /// ForgeFrom with resolver methods that take the full source object.
    /// </summary>
    [ForgeProperty(nameof(InvoiceEntity.CompanyName), nameof(InvoiceDisplayDto.Company))]
    [ForgeFrom(nameof(InvoiceDisplayDto.DisplayNumber), nameof(FormatInvoiceNumber))]
    [ForgeFrom(nameof(InvoiceDisplayDto.FormattedDate), nameof(FormatDate))]
    public partial InvoiceDisplayDto Forge(InvoiceEntity source);

    /// <summary>
    /// Nested path mapping with ForgeProperty.
    /// </summary>
    [ForgeProperty("Customer.Name", nameof(FlatOrderDto.CustomerName))]
    [ForgeProperty("Customer.Email", nameof(FlatOrderDto.CustomerEmail))]
    public partial FlatOrderDto Forge(NestedOrderEntity source);

    // Resolver methods for ForgeFrom

    private static decimal CalculateTotalWithTax(OrderEntityV2 source)
        => source.Subtotal * (1 + source.TaxRate);

    private static string FormatInvoiceNumber(InvoiceEntity source)
        => $"INV-{source.InvoiceNumber:D6}";

    private static string FormatDate(InvoiceEntity source)
        => source.InvoiceDate.ToString("yyyy-MM-dd");

    // v0.2 Nullable handling

    /// <summary>
    /// Nullable&lt;T&gt; to T conversion - uses explicit cast.
    /// </summary>
    public partial OrderWithRequiredDates Forge(OrderWithNullables source);

    /// <summary>
    /// T to Nullable&lt;T&gt; conversion - direct assignment
    /// </summary>
    public partial OrderWithOptionalDates Forge(SimpleOrder source);

    // v0.3 Features - ForgeWith (nested objects)

    /// <summary>
    /// Element forging for nested objects.
    /// </summary>
    public partial AddressDto Forge(AddressEntity source);

    /// <summary>
    /// ForgeWith - uses Forge(AddressEntity) for the Address property.
    /// </summary>
    [ForgeWith(nameof(UserWithAddressDto.Address), nameof(Forge))]
    public partial UserWithAddressDto Forge(UserWithAddressEntity source);

    // v0.3 Features - Collection forging

    /// <summary>
    /// Collection forging - List&lt;T&gt;
    /// </summary>
    public partial List<ProductDto> Forge(List<ProductEntity> source);

    /// <summary>
    /// Collection forging - T[]
    /// </summary>
    public partial ProductDto[] Forge(ProductEntity[] source);

    /// <summary>
    /// Collection forging - IEnumerable&lt;T&gt;
    /// </summary>
    public partial IEnumerable<ProductDto> Forge(IEnumerable<ProductEntity> source);
}

// v0.4 Forger - Enums

[ForgeMap]
public partial class EnumForger
{
    /// <summary>
    /// Enum to Enum - matched by name.
    /// </summary>
    public partial OrderStatusDto Forge(OrderStatus source);
}

[ForgeMap]
public partial class EnumToStringForger
{
    /// <summary>
    /// Enum to string - uses .ToString().
    /// </summary>
    public partial string Forge(OrderStatus source);
}

[ForgeMap]
public partial class StringToEnumForger
{
    /// <summary>
    /// String to Enum - uses Enum.Parse case-insensitive.
    /// </summary>
    public partial OrderStatus Forge(string source);
}

// v0.4 Forger - Constructor mapping

[ForgeMap]
public partial class CtorForger
{
    /// <summary>
    /// Record constructor mapping - all properties via constructor.
    /// </summary>
    public partial OrderRecordDto Forge(OrderForRecordEntity source);

    /// <summary>
    /// Immutable type with parameterized constructor only.
    /// </summary>
    public partial ImmutableOrder ForgeImmutable(OrderForCtorEntity source);

    /// <summary>
    /// Hybrid: constructor + property setters.
    /// </summary>
    public partial HybridOrder ForgeHybrid(OrderForCtorEntity source);
}

// v0.4 Forger - Automatic flattening

[ForgeMap]
public partial class FlattenForger
{
    /// <summary>
    /// Auto-flatten: EmployeeEntity.Company.Name → FlatEmployeeDto.CompanyName.
    /// </summary>
    public partial FlatEmployeeDto Forge(EmployeeEntity source);
}

// v0.5 Forger - ReverseForge

[ForgeMap]
public partial class ReverseForger
{
    /// <summary>
    /// Simple reverse: generates both Forge(ArticleEntity → ArticleDto) and Forge(ArticleDto → ArticleEntity).
    /// </summary>
    [ReverseForge]
    public partial ArticleDto Forge(ArticleEntity source);

    /// <summary>
    /// Reverse with [ForgeProperty]: BookTitle ↔ DisplayTitle swapped in both directions.
    /// </summary>
    [ReverseForge]
    [ForgeProperty(nameof(BookEntity.BookTitle), nameof(BookDto.DisplayTitle))]
    public partial BookDto Forge(BookEntity source);

    /// <summary>
    /// Reverse with [Ignore]: InternalNote is ignored in both directions.
    /// </summary>
    [ReverseForge]
    [Ignore(nameof(ArticleDto.InternalNote))]
    public partial ArticleDto ForgeIgnored(ArticleEntity source);

    /// <summary>
    /// Reverse with [ForgeWith]: HomeAddress uses nested Forge for AddressEntity ↔ AddressDto.
    /// Also has [ReverseForge] on the nested method so it can reverse.
    /// </summary>
    [ReverseForge]
    [ForgeWith(nameof(ContactDto.HomeAddress), nameof(Forge))]
    public partial ContactDto Forge(ContactEntity source);

    /// <summary>
    /// Nested address forging method with [ReverseForge].
    /// </summary>
    [ReverseForge]
    public partial AddressDto Forge(AddressEntity source);
}

#endregion

public class BasicMappingTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void SimplePropertyMapping_ShouldMapAllMatchingProperties()
    {
        // Arrange
        var entity = new OrderEntity
        {
            Id = 123,
            Name = "Test Order",
            Total = 99.99m,
            CreatedAt = new DateTime(2024, 1, 15),
            InternalCode = "INTERNAL"
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(123);
        dto.Name.Should().Be("Test Order");
        dto.Total.Should().Be(99.99m);
        dto.CreatedAt.Should().Be(new DateTime(2024, 1, 15));
        dto.InternalCode.Should().Be("INTERNAL");
    }

    [Fact]
    public void NullSource_ShouldReturnNull()
    {
        // Arrange
        OrderEntity? entity = null;

        // Act
        var dto = _forger.Forge(entity!);

        // Assert
        dto.Should().BeNull();
    }

    [Fact]
    public void IgnoreAttribute_ShouldExcludeSpecifiedProperties()
    {
        // Arrange
        var entity = new UserEntity
        {
            Id = 1,
            Username = "john.doe",
            Email = "john@example.com",
            PasswordHash = "secret_hash_abc123",
            SecurityStamp = "stamp_xyz789"
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.Username.Should().Be("john.doe");
        dto.Email.Should().Be("john@example.com");
        // Ignored properties should have default values (not mapped)
        dto.PasswordHash.Should().BeNullOrEmpty();
        dto.SecurityStamp.Should().BeNullOrEmpty();
    }

    [Fact]
    public void IgnoreSingleProperty_ShouldExcludeOnlyThatProperty()
    {
        // Arrange
        var entity = new ProductEntity
        {
            Id = 42,
            Name = "Widget",
            Price = 19.99m,
            Quantity = 100
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(42);
        dto.Name.Should().Be("Widget");
        dto.Price.Should().Be(19.99m);
        dto.Quantity.Should().Be(100);
        // Description is not in source and is ignored anyway
        dto.Description.Should().BeEmpty();
    }

    [Fact]
    public void MultipleForgerMethods_ShouldWorkIndependently()
    {
        // Arrange
        var orderEntity = new OrderEntity { Id = 1, Name = "Order 1", Total = 50m };
        var userEntity = new UserEntity { Id = 2, Username = "jane", Email = "jane@test.com" };

        // Act
        var orderDto = _forger.Forge(orderEntity);
        var userDto = _forger.Forge(userEntity);

        // Assert
        orderDto.Should().NotBeNull();
        orderDto.Id.Should().Be(1);
        orderDto.Name.Should().Be("Order 1");

        userDto.Should().NotBeNull();
        userDto.Id.Should().Be(2);
        userDto.Username.Should().Be("jane");
    }
}

public class ForgeIntoTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void ForgeInto_ShouldUpdateExistingInstance()
    {
        // Arrange
        var source = new OrderEntity
        {
            Id = 10,
            Name = "Updated Order",
            Total = 250.00m,
            CreatedAt = new DateTime(2024, 6, 1),
            InternalCode = "NEW_CODE"
        };
        var existing = new OrderDto
        {
            Id = 999,
            Name = "Old Name",
            Total = 0m,
            CreatedAt = DateTime.MinValue,
            InternalCode = "OLD_CODE"
        };

        // Act
        _forger.ForgeInto(source, existing);

        // Assert
        existing.Id.Should().Be(10);
        existing.Name.Should().Be("Updated Order");
        existing.Total.Should().Be(250.00m);
        existing.CreatedAt.Should().Be(new DateTime(2024, 6, 1));
        existing.InternalCode.Should().Be("NEW_CODE");
    }

    [Fact]
    public void ForgeInto_NullSource_ShouldNotModifyDestination()
    {
        // Arrange
        OrderEntity? source = null;
        var existing = new OrderDto
        {
            Id = 999,
            Name = "Original",
            Total = 100m
        };

        // Act
        _forger.ForgeInto(source!, existing);

        // Assert - destination should remain unchanged
        existing.Id.Should().Be(999);
        existing.Name.Should().Be("Original");
        existing.Total.Should().Be(100m);
    }

    [Fact]
    public void ForgeInto_NullDestination_ShouldThrowArgumentNullException()
    {
        // Arrange
        var source = new OrderEntity { Id = 1, Name = "Test" };
        OrderDto? destination = null;

        // Act & Assert
        var action = () => _forger.ForgeInto(source, destination!);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("destination");
    }

    [Fact]
    public void ForgeInto_WithIgnore_ShouldNotUpdateIgnoredProperties()
    {
        // Arrange
        var source = new UserEntity
        {
            Id = 5,
            Username = "newuser",
            Email = "new@example.com",
            PasswordHash = "new_hash",
            SecurityStamp = "new_stamp"
        };
        var existing = new UserDto
        {
            Id = 999,
            Username = "olduser",
            Email = "old@example.com",
            PasswordHash = "old_hash",
            SecurityStamp = "old_stamp"
        };

        // Act
        _forger.ForgeInto(source, existing);

        // Assert
        existing.Id.Should().Be(5);
        existing.Username.Should().Be("newuser");
        existing.Email.Should().Be("new@example.com");
        // PasswordHash is ignored (SecurityStamp is not ignored in ForgeInto)
        existing.PasswordHash.Should().Be("old_hash");
        existing.SecurityStamp.Should().Be("new_stamp");
    }
}

#region v0.2 Feature Tests

public class ForgePropertyTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void ForgeProperty_ShouldMapDifferentlyNamedProperties()
    {
        // Arrange
        var entity = new OrderEntityV2
        {
            OrderId = "ORD-12345",
            PlacedAt = new DateTime(2024, 3, 15, 10, 30, 0),
            Subtotal = 100.00m,
            TaxRate = 0.08m,
            CustomerName = "John Doe"
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be("ORD-12345");
        dto.OrderDate.Should().Be(new DateTime(2024, 3, 15, 10, 30, 0));
        dto.CustomerName.Should().Be("John Doe");
    }

    [Fact]
    public void ForgeProperty_SimpleRename_ShouldWork()
    {
        // Arrange
        var entity = new ProductEntityV2
        {
            Id = 42,
            Title = "Widget Pro",
            UnitPrice = 29.99m
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(42);
        dto.Name.Should().Be("Widget Pro");
        dto.Price.Should().Be(29.99m);
    }

    [Fact]
    public void ForgeProperty_NestedPath_ShouldFlattenProperties()
    {
        // Arrange
        var entity = new NestedOrderEntity
        {
            Id = 100,
            Customer = new CustomerInfo
            {
                Name = "Alice Smith",
                Email = "alice@example.com"
            }
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(100);
        dto.CustomerName.Should().Be("Alice Smith");
        dto.CustomerEmail.Should().Be("alice@example.com");
    }

    [Fact]
    public void ForgeProperty_NullNestedObject_ShouldHandleGracefully()
    {
        // Arrange
        var entity = new NestedOrderEntity
        {
            Id = 200,
            Customer = null
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(200);
        dto.CustomerName.Should().BeNull();
        dto.CustomerEmail.Should().BeNull();
    }
}

public class ForgeFromTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void ForgeFrom_WithSourceObject_ShouldCallResolver()
    {
        // Arrange
        var entity = new OrderEntityV2
        {
            OrderId = "ORD-001",
            PlacedAt = new DateTime(2024, 3, 15),
            Subtotal = 100.00m,
            TaxRate = 0.08m,
            CustomerName = "Test Customer"
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.TotalWithTax.Should().Be(108.00m); // 100 * 1.08
    }

    [Fact]
    public void ForgeFrom_MultipleResolvers_ShouldWorkTogether()
    {
        // Arrange
        var entity = new InvoiceEntity
        {
            InvoiceNumber = 42,
            CompanyName = "Acme Corp",
            InvoiceDate = new DateTime(2024, 6, 15)
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.DisplayNumber.Should().Be("INV-000042");
        dto.Company.Should().Be("Acme Corp");
        dto.FormattedDate.Should().Be("2024-06-15");
    }

    [Fact]
    public void ForgeFrom_CombinedWithForgeProperty_ShouldWorkCorrectly()
    {
        // Arrange
        var entity = new OrderEntityV2
        {
            OrderId = "ORD-999",
            PlacedAt = new DateTime(2024, 12, 25),
            Subtotal = 200.00m,
            TaxRate = 0.10m,
            CustomerName = "Holiday Shopper"
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be("ORD-999"); // ForgeProperty
        dto.OrderDate.Should().Be(new DateTime(2024, 12, 25)); // ForgeProperty
        dto.TotalWithTax.Should().Be(220.00m); // ForgeFrom: 200 * 1.10
        dto.CustomerName.Should().Be("Holiday Shopper"); // Convention mapping
    }
}

public class NullableHandlingTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void NullableToNonNullable_WithValue_ShouldConvert()
    {
        // Arrange
        var entity = new OrderWithNullables
        {
            Id = 1,
            ShippedAt = new DateTime(2024, 6, 15),
            DiscountPercent = 0.1m,
            Quantity = 5
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.ShippedAt.Should().Be(new DateTime(2024, 6, 15));
        dto.DiscountPercent.Should().Be(0.1m);
        dto.Quantity.Should().Be(5);
    }

    [Fact]
    public void NullableToNonNullable_WithNull_ShouldThrow()
    {
        // Arrange
        var entity = new OrderWithNullables
        {
            Id = 1,
            ShippedAt = null,
            DiscountPercent = null,
            Quantity = null
        };

        // Act & Assert
        var action = () => _forger.Forge(entity);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NonNullableToNullable_ShouldConvert()
    {
        // Arrange
        var entity = new SimpleOrder
        {
            Id = 1,
            ShippedAt = new DateTime(2024, 8, 20),
            DiscountPercent = 0.25m,
            Quantity = 10
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.ShippedAt.Should().Be(new DateTime(2024, 8, 20));
        dto.DiscountPercent.Should().Be(0.25m);
        dto.Quantity.Should().Be(10);
    }
}

#endregion

#region v0.3 Feature Tests

public class ForgeWithTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void ForgeWith_ShouldMapNestedObject()
    {
        // Arrange
        var entity = new UserWithAddressEntity
        {
            Id = 1,
            Name = "Alice",
            Address = new AddressEntity
            {
                Street = "123 Main St",
                City = "Springfield",
                ZipCode = "62701"
            }
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.Name.Should().Be("Alice");
        dto.Address.Should().NotBeNull();
        dto.Address!.Street.Should().Be("123 Main St");
        dto.Address.City.Should().Be("Springfield");
        dto.Address.ZipCode.Should().Be("62701");
    }

    [Fact]
    public void ForgeWith_NullNestedObject_ShouldReturnNull()
    {
        // Arrange
        var entity = new UserWithAddressEntity
        {
            Id = 2,
            Name = "Bob",
            Address = null
        };

        // Act
        var dto = _forger.Forge(entity);

        // Assert
        dto.Should().NotBeNull();
        dto.Id.Should().Be(2);
        dto.Name.Should().Be("Bob");
        dto.Address.Should().BeNull();
    }
}

public class CollectionForgingTests
{
    private readonly AppForger _forger = new();

    [Fact]
    public void CollectionForge_List_ShouldMapElements()
    {
        // Arrange
        var entities = new List<ProductEntity>
        {
            new() { Id = 1, Name = "Widget", Price = 9.99m, Quantity = 10 },
            new() { Id = 2, Name = "Gadget", Price = 19.99m, Quantity = 5 }
        };

        // Act
        var dtos = _forger.Forge(entities);

        // Assert
        dtos.Should().NotBeNull();
        dtos.Should().HaveCount(2);
        dtos[0].Id.Should().Be(1);
        dtos[0].Name.Should().Be("Widget");
        dtos[1].Id.Should().Be(2);
        dtos[1].Name.Should().Be("Gadget");
    }

    [Fact]
    public void CollectionForge_Array_ShouldMapElements()
    {
        // Arrange
        var entities = new[]
        {
            new ProductEntity { Id = 1, Name = "Item1", Price = 5.00m, Quantity = 3 },
            new ProductEntity { Id = 2, Name = "Item2", Price = 10.00m, Quantity = 7 }
        };

        // Act
        var dtos = _forger.Forge(entities);

        // Assert
        dtos.Should().NotBeNull();
        dtos.Should().HaveCount(2);
        dtos[0].Id.Should().Be(1);
        dtos[0].Name.Should().Be("Item1");
        dtos[1].Id.Should().Be(2);
    }

    [Fact]
    public void CollectionForge_IEnumerable_ShouldMapElements()
    {
        // Arrange
        IEnumerable<ProductEntity> entities = new List<ProductEntity>
        {
            new() { Id = 1, Name = "A", Price = 1.00m, Quantity = 1 },
            new() { Id = 2, Name = "B", Price = 2.00m, Quantity = 2 },
            new() { Id = 3, Name = "C", Price = 3.00m, Quantity = 3 }
        };

        // Act
        var dtos = _forger.Forge(entities);

        // Assert
        dtos.Should().NotBeNull();
        var list = dtos.ToList();
        list.Should().HaveCount(3);
        list[0].Name.Should().Be("A");
        list[2].Name.Should().Be("C");
    }

    [Fact]
    public void CollectionForge_NullSource_ShouldReturnNull()
    {
        // Arrange
        List<ProductEntity>? entities = null;

        // Act
        var dtos = _forger.Forge(entities!);

        // Assert
        dtos.Should().BeNull();
    }

    [Fact]
    public void CollectionForge_EmptyList_ShouldReturnEmptyList()
    {
        // Arrange
        var entities = new List<ProductEntity>();

        // Act
        var dtos = _forger.Forge(entities);

        // Assert
        dtos.Should().NotBeNull();
        dtos.Should().BeEmpty();
    }

    [Fact]
    public void CollectionForge_EmptyArray_ShouldReturnEmptyArray()
    {
        // Arrange
        var entities = Array.Empty<ProductEntity>();

        // Act
        var dtos = _forger.Forge(entities);

        // Assert
        dtos.Should().NotBeNull();
        dtos.Should().BeEmpty();
    }
}

#endregion

#region v0.4 Feature Tests

public class EnumForgingTests
{
    [Fact]
    public void EnumToEnum_ShouldMapByName()
    {
        var forger = new EnumForger();
        var result = forger.Forge(OrderStatus.Shipped);
        result.Should().Be(OrderStatusDto.Shipped);
    }

    [Fact]
    public void EnumToEnum_AllValues_ShouldMap()
    {
        var forger = new EnumForger();
        forger.Forge(OrderStatus.Pending).Should().Be(OrderStatusDto.Pending);
        forger.Forge(OrderStatus.Processing).Should().Be(OrderStatusDto.Processing);
        forger.Forge(OrderStatus.Shipped).Should().Be(OrderStatusDto.Shipped);
        forger.Forge(OrderStatus.Delivered).Should().Be(OrderStatusDto.Delivered);
    }

    [Fact]
    public void EnumToString_ShouldReturnName()
    {
        var forger = new EnumToStringForger();
        var result = forger.Forge(OrderStatus.Delivered);
        result.Should().Be("Delivered");
    }

    [Fact]
    public void StringToEnum_ShouldParseCaseInsensitive()
    {
        var forger = new StringToEnumForger();
        forger.Forge("Pending").Should().Be(OrderStatus.Pending);
        forger.Forge("shipped").Should().Be(OrderStatus.Shipped);
        forger.Forge("DELIVERED").Should().Be(OrderStatus.Delivered);
    }

    [Fact]
    public void StringToEnum_InvalidValue_ShouldThrow()
    {
        var forger = new StringToEnumForger();
        var action = () => forger.Forge("NonExistent");
        action.Should().Throw<ArgumentException>();
    }
}

public class ConstructorMappingTests
{
    private readonly CtorForger _forger = new();

    [Fact]
    public void RecordConstructor_ShouldMapViaConstructor()
    {
        var entity = new OrderForRecordEntity
        {
            Id = "ORD-001",
            CustomerName = "Alice",
            OrderDate = new DateTime(2024, 6, 15)
        };

        var result = _forger.Forge(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be("ORD-001");
        result.CustomerName.Should().Be("Alice");
        result.OrderDate.Should().Be(new DateTime(2024, 6, 15));
    }

    [Fact]
    public void ImmutableType_ShouldMapViaConstructor()
    {
        var entity = new OrderForCtorEntity { Id = 42, Name = "Test Order", Total = 100m };

        var result = _forger.ForgeImmutable(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("Test Order");
    }

    [Fact]
    public void HybridType_ShouldMapCtorAndSetters()
    {
        var entity = new OrderForCtorEntity { Id = 10, Name = "Hybrid", Total = 250m };

        var result = _forger.ForgeHybrid(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be(10);
        result.Name.Should().Be("Hybrid");
        result.Total.Should().Be(250m);
    }

    [Fact]
    public void RecordConstructor_NullSource_ShouldReturnNull()
    {
        OrderForRecordEntity? entity = null;
        var result = _forger.Forge(entity!);
        result.Should().BeNull();
    }
}

public class AutoFlatteningTests
{
    private readonly FlattenForger _forger = new();

    [Fact]
    public void AutoFlatten_ShouldMapNestedProperties()
    {
        var entity = new EmployeeEntity
        {
            Id = 1,
            Name = "Bob",
            Company = new CompanyInfo
            {
                Name = "Acme Corp",
                Address = new AddressEntity
                {
                    Street = "123 Main St",
                    City = "Springfield",
                    ZipCode = "62701"
                }
            }
        };

        var result = _forger.Forge(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.Name.Should().Be("Bob");
        result.CompanyName.Should().Be("Acme Corp");
        result.CompanyAddressCity.Should().Be("Springfield");
    }

    [Fact]
    public void AutoFlatten_NullIntermediate_ShouldReturnNull()
    {
        var entity = new EmployeeEntity
        {
            Id = 2,
            Name = "Alice",
            Company = null
        };

        var result = _forger.Forge(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be(2);
        result.Name.Should().Be("Alice");
        result.CompanyName.Should().BeNull();
        result.CompanyAddressCity.Should().BeNull();
    }

    [Fact]
    public void AutoFlatten_NullDeepIntermediate_ShouldReturnNull()
    {
        var entity = new EmployeeEntity
        {
            Id = 3,
            Name = "Charlie",
            Company = new CompanyInfo
            {
                Name = "NoAddress Inc",
                Address = null
            }
        };

        var result = _forger.Forge(entity);

        result.Should().NotBeNull();
        result.Id.Should().Be(3);
        result.Name.Should().Be("Charlie");
        result.CompanyName.Should().Be("NoAddress Inc");
        result.CompanyAddressCity.Should().BeNull();
    }
}

#endregion

#region v0.5 Feature Tests

public class ReverseForgeTests
{
    private readonly ReverseForger _forger = new();

    [Fact]
    public void ReverseForge_SimpleMapping_ShouldMapForward()
    {
        var entity = new ArticleEntity { Id = 1, Title = "Hello", Content = "World", InternalNote = "Note" };
        var dto = _forger.Forge(entity);

        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.Title.Should().Be("Hello");
        dto.Content.Should().Be("World");
    }

    [Fact]
    public void ReverseForge_SimpleMapping_ShouldMapReverse()
    {
        var dto = new ArticleDto { Id = 2, Title = "Reverse", Content = "Test", InternalNote = "N" };
        var entity = _forger.Forge(dto);

        entity.Should().NotBeNull();
        entity.Id.Should().Be(2);
        entity.Title.Should().Be("Reverse");
        entity.Content.Should().Be("Test");
    }

    [Fact]
    public void ReverseForge_NullSource_ShouldReturnNull_Forward()
    {
        ArticleEntity? entity = null;
        var dto = _forger.Forge(entity!);
        dto.Should().BeNull();
    }

    [Fact]
    public void ReverseForge_NullSource_ShouldReturnNull_Reverse()
    {
        ArticleDto? dto = null;
        var entity = _forger.Forge(dto!);
        entity.Should().BeNull();
    }

    [Fact]
    public void ReverseForge_WithForgeProperty_ShouldMapForward()
    {
        var entity = new BookEntity { Id = 1, BookTitle = "My Book", Author = "Alice" };
        var dto = _forger.Forge(entity);

        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.DisplayTitle.Should().Be("My Book");
        dto.Author.Should().Be("Alice");
    }

    [Fact]
    public void ReverseForge_WithForgeProperty_ShouldMapReverse()
    {
        var dto = new BookDto { Id = 2, DisplayTitle = "Other Book", Author = "Bob" };
        var entity = _forger.Forge(dto);

        entity.Should().NotBeNull();
        entity.Id.Should().Be(2);
        entity.BookTitle.Should().Be("Other Book");
        entity.Author.Should().Be("Bob");
    }

    [Fact]
    public void ReverseForge_WithIgnore_ShouldIgnoreBothDirections()
    {
        var entity = new ArticleEntity { Id = 1, Title = "Test", Content = "Body", InternalNote = "Secret" };
        var dto = _forger.ForgeIgnored(entity);

        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.Title.Should().Be("Test");
        dto.Content.Should().Be("Body");
        dto.InternalNote.Should().BeNullOrEmpty(); // ignored

        var reversedEntity = _forger.ForgeIgnored(dto);

        reversedEntity.Should().NotBeNull();
        reversedEntity.Id.Should().Be(1);
        reversedEntity.Title.Should().Be("Test");
        reversedEntity.Content.Should().Be("Body");
        reversedEntity.InternalNote.Should().BeNullOrEmpty(); // ignored in reverse too
    }

    [Fact]
    public void ReverseForge_WithForgeWith_ShouldMapNestedForward()
    {
        var entity = new ContactEntity
        {
            Id = 1,
            Name = "Alice",
            HomeAddress = new AddressEntity { Street = "123 Main St", City = "Springfield", ZipCode = "62701" }
        };

        var dto = _forger.Forge(entity);

        dto.Should().NotBeNull();
        dto.Id.Should().Be(1);
        dto.Name.Should().Be("Alice");
        dto.HomeAddress.Should().NotBeNull();
        dto.HomeAddress!.Street.Should().Be("123 Main St");
        dto.HomeAddress.City.Should().Be("Springfield");
    }

    [Fact]
    public void ReverseForge_WithForgeWith_ShouldMapNestedReverse()
    {
        var dto = new ContactDto
        {
            Id = 2,
            Name = "Bob",
            HomeAddress = new AddressDto { Street = "456 Oak Ave", City = "Shelbyville", ZipCode = "12345" }
        };

        var entity = _forger.Forge(dto);

        entity.Should().NotBeNull();
        entity.Id.Should().Be(2);
        entity.Name.Should().Be("Bob");
        entity.HomeAddress.Should().NotBeNull();
        entity.HomeAddress!.Street.Should().Be("456 Oak Ave");
        entity.HomeAddress.City.Should().Be("Shelbyville");
    }

    [Fact]
    public void ReverseForge_WithForgeWith_NullNested_ShouldReturnNull()
    {
        var entity = new ContactEntity { Id = 3, Name = "Charlie", HomeAddress = null };
        var dto = _forger.Forge(entity);

        dto.Should().NotBeNull();
        dto.HomeAddress.Should().BeNull();

        var reverseEntity = _forger.Forge(dto);
        reverseEntity.Should().NotBeNull();
        reverseEntity.HomeAddress.Should().BeNull();
    }
}

#endregion