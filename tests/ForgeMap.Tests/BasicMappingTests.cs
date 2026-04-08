using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using ForgeMap;
using Xunit;

namespace ForgeMap.Tests;


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



#region v0.6 Feature Tests

public class BeforeForgeTests
{
    private readonly HookForger _forger = new();

    [Fact]
    public void BeforeForge_ShouldCallValidation()
    {
        // Arrange - invalid source (Id <= 0)
        var source = new OrderEntityV6 { Id = 0, Name = "Test", Total = 100m };

        // Act & Assert - should throw from BeforeForge validation
        var action = () => _forger.Forge(source);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*Id must be positive*");
    }

    [Fact]
    public void BeforeForge_ValidSource_ShouldMapSuccessfully()
    {
        var source = new OrderEntityV6 { Id = 1, Name = "Test Order", Total = 99.99m };

        var result = _forger.Forge(source);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.Name.Should().Be("Test Order");
        result.Total.Should().Be(99.99m);
    }

    [Fact]
    public void BeforeForge_NullSource_ShouldReturnNull_NotCallHook()
    {
        // Null source should return null before BeforeForge runs (per spec)
        OrderEntityV6? source = null;

        var result = _forger.Forge(source!);

        result.Should().BeNull();
    }

    [Fact]
    public void BeforeForge_MultipleHooks_ShouldCallInOrder()
    {
        HookForger.LogCalled = false;

        // Invalid source - ValidateOrder runs first and throws
        var source = new OrderEntityV6 { Id = -1, Name = "Bad", Total = 0m };

        var action = () => _forger.ForgeMultiBefore(source);
        action.Should().Throw<ArgumentException>();

        // LogOrder should NOT have been called since ValidateOrder threw first
        HookForger.LogCalled.Should().BeFalse();
    }

    [Fact]
    public void BeforeForge_MultipleHooks_AllCalled_WhenValid()
    {
        HookForger.LogCalled = false;

        var source = new OrderEntityV6 { Id = 5, Name = "Good", Total = 50m };

        var result = _forger.ForgeMultiBefore(source);

        result.Should().NotBeNull();
        result.Id.Should().Be(5);
        HookForger.LogCalled.Should().BeTrue();
    }
}

public class AfterForgeTests
{
    private readonly HookForger _forger = new();

    [Fact]
    public void AfterForge_ShouldEnrichDestination()
    {
        var source = new OrderEntity
        {
            Id = 42,
            Name = "Widget",
            Total = 100m,
            CreatedAt = DateTime.Now,
            InternalCode = "ABC"
        };

        var result = _forger.Forge(source);

        result.Should().NotBeNull();
        result.Name.Should().Be("Order #42 - Widget");
        result.Id.Should().Be(42);
        result.Total.Should().Be(100m);
    }

    [Fact]
    public void AfterForge_NullSource_ShouldReturnNull_NotCallHook()
    {
        OrderEntity? source = null;

        var result = _forger.Forge(source!);

        result.Should().BeNull();
    }
}

public class BeforeAndAfterForgeTests
{
    private readonly HookForger _forger = new();

    [Fact]
    public void BothHooks_ShouldValidateThenEnrich()
    {
        var source = new OrderEntityV6 { Id = 10, Name = "Combined", Total = 200m };

        var result = _forger.ForgeWithBoth(source);

        result.Should().NotBeNull();
        result.Id.Should().Be(10);
        result.Name.Should().Be("Combined");
        result.DisplayName.Should().Be("Order #10 - Combined");
    }

    [Fact]
    public void BothHooks_InvalidSource_ShouldThrowFromBeforeForge()
    {
        var source = new OrderEntityV6 { Id = 0, Name = "Invalid", Total = 0m };

        var action = () => _forger.ForgeWithBoth(source);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForgeInto_WithHooks_ShouldValidateAndEnrich()
    {
        var source = new OrderEntityV6 { Id = 7, Name = "Into Test", Total = 77m };
        var existing = new OrderDtoV6 { Id = 999, Name = "Old", Total = 0m, DisplayName = "Old Display" };

        _forger.ForgeInto(source, existing);

        existing.Id.Should().Be(7);
        existing.Name.Should().Be("Into Test");
        existing.Total.Should().Be(77m);
        existing.DisplayName.Should().Be("Order #7 - Into Test");
    }

    [Fact]
    public void ForgeInto_WithHooks_InvalidSource_ShouldThrowFromBeforeForge()
    {
        var source = new OrderEntityV6 { Id = -1, Name = "Bad", Total = 0m };
        var existing = new OrderDtoV6 { Id = 999, Name = "Old", Total = 0m };

        var action = () => _forger.ForgeInto(source, existing);
        action.Should().Throw<ArgumentException>();

        // Destination should be unchanged
        existing.Id.Should().Be(999);
        existing.Name.Should().Be("Old");
    }

    [Fact]
    public void ForgeInto_WithHooks_NullSource_ShouldNotCallHooks()
    {
        OrderEntityV6? source = null;
        var existing = new OrderDtoV6 { Id = 999, Name = "Original", Total = 100m };

        _forger.ForgeInto(source!, existing);

        // Destination should be unchanged
        existing.Id.Should().Be(999);
        existing.Name.Should().Be("Original");
    }
}

#endregion

#endregion



#region Auto-Wire Runtime Tests

public class AutoWireRuntimeTests
{
    private readonly AutoWireForger _forger = new();

    [Fact]
    public void AutoWire_NestedObject_MappedCorrectly()
    {
        var source = new AutoWirePersonEntity
        {
            Id = 1,
            Name = "Alice",
            Address = new AddressEntity { Street = "123 Main St", City = "Springfield", ZipCode = "62701" },
            Phone = new AutoWirePhoneEntity { Number = "555-1234", AreaCode = "312" }
        };

        var result = _forger.Forge(source);

        result.Id.Should().Be(1);
        result.Name.Should().Be("Alice");
        result.Address.Should().NotBeNull();
        result.Address!.Street.Should().Be("123 Main St");
        result.Address.City.Should().Be("Springfield");
        result.Address.ZipCode.Should().Be("62701");
        result.Phone.Should().NotBeNull();
        result.Phone!.Number.Should().Be("555-1234");
        result.Phone.AreaCode.Should().Be("312");
    }

    [Fact]
    public void AutoWire_NullNestedProperty_ReturnsNull()
    {
        var source = new AutoWirePersonEntity
        {
            Id = 2,
            Name = "Bob",
            Address = null,
            Phone = null
        };

        var result = _forger.Forge(source);

        result.Id.Should().Be(2);
        result.Name.Should().Be("Bob");
        result.Address.Should().BeNull();
        result.Phone.Should().BeNull();
    }
}

#endregion



#region v1.4 Nested Existing-Target Tests

public class NestedExistingTargetTests
{
    private readonly ExistingTargetForger _forger = new();

    [Fact]
    public void ExistingTarget_ShouldUpdateNestedObjectInPlace()
    {
        var source = new EtOrderUpdateDto
        {
            Status = "Shipped",
            Customer = new EtCustomerUpdateDto { Name = "Alice Updated", Email = "alice@new.com" },
            ShippingAddress = new EtAddressUpdateDto { City = "Springfield", ZipCode = "62704" }
        };

        var existingCustomer = new EtCustomer { Name = "Alice", Email = "alice@old.com" };
        var existingAddress = new EtAddress { City = "OldCity", ZipCode = "00000" };
        var target = new EtOrder
        {
            Status = "Pending",
            Customer = existingCustomer,
            ShippingAddress = existingAddress
        };

        _forger.ForgeInto(source, target);

        // Status should be updated directly
        target.Status.Should().Be("Shipped");

        // Nested objects should be the SAME reference (updated in place)
        target.Customer.Should().BeSameAs(existingCustomer);
        target.Customer!.Name.Should().Be("Alice Updated");
        target.Customer.Email.Should().Be("alice@new.com");

        target.ShippingAddress.Should().BeSameAs(existingAddress);
        target.ShippingAddress!.City.Should().Be("Springfield");
        target.ShippingAddress.ZipCode.Should().Be("62704");
    }

    [Fact]
    public void ExistingTarget_SourceNull_ShouldLeaveTargetUnchanged()
    {
        var source = new EtOrderUpdateDto
        {
            Status = "Shipped",
            Customer = null,
            ShippingAddress = null
        };

        var existingCustomer = new EtCustomer { Name = "Alice", Email = "alice@old.com" };
        var target = new EtOrder
        {
            Status = "Pending",
            Customer = existingCustomer,
            ShippingAddress = new EtAddress { City = "OldCity", ZipCode = "00000" }
        };

        _forger.ForgeInto(source, target);

        target.Status.Should().Be("Shipped");
        // Source is null — target properties should remain unchanged
        target.Customer.Should().BeSameAs(existingCustomer);
        target.Customer!.Name.Should().Be("Alice");
    }

    [Fact]
    public void ExistingTarget_TargetPropertyNull_ShouldSkip()
    {
        var source = new EtOrderUpdateDto
        {
            Status = "Shipped",
            Customer = new EtCustomerUpdateDto { Name = "New Customer", Email = "new@test.com" }
        };

        var target = new EtOrder
        {
            Status = "Pending",
            Customer = null // Target property is null
        };

        _forger.ForgeInto(source, target);

        target.Status.Should().Be("Shipped");
        // Default NullPropertyHandling (NullForgiving/SkipNull): skip when target is null
        target.Customer.Should().BeNull();
    }

    [Fact]
    public void ExistingTarget_CollectionSync_ShouldMatchUpdateAddRemove()
    {
        var source = new EtOrderUpdateDto
        {
            Status = "Shipped",
            Customer = new EtCustomerUpdateDto { Name = "Alice", Email = "a@a.com" },
            ShippingAddress = new EtAddressUpdateDto { City = "City", ZipCode = "12345" },
            Items = new List<EtOrderItemUpdateDto>
            {
                new() { Id = 1, Name = "Widget Updated", Quantity = 5 },
                new() { Id = 3, Name = "New Item", Quantity = 1 }
            }
        };

        var existingItem1 = new EtOrderItem { Id = 1, Name = "Widget", Quantity = 2 };
        var existingItem2 = new EtOrderItem { Id = 2, Name = "Gadget", Quantity = 3 };
        var target = new EtOrder
        {
            Status = "Pending",
            Customer = new EtCustomer { Name = "Alice", Email = "a@a.com" },
            ShippingAddress = new EtAddress { City = "City", ZipCode = "12345" },
            Items = new List<EtOrderItem> { existingItem1, existingItem2 }
        };

        _forger.ForgeIntoWithSync(source, target);

        target.Items.Should().NotBeNull();
        target.Items.Should().HaveCount(2);

        // Item 1 should be updated in place (same reference)
        target.Items![0].Should().BeSameAs(existingItem1);
        target.Items[0].Name.Should().Be("Widget Updated");
        target.Items[0].Quantity.Should().Be(5);

        // Item 2 (Id=2) should be removed (not in source)
        target.Items.Should().NotContain(i => i.Id == 2);

        // Item 3 should be added (new)
        target.Items.Should().Contain(i => i.Id == 3);
        target.Items.First(i => i.Id == 3).Name.Should().Be("New Item");
    }
}

#endregion



#region v1.4 String→Enum Tests

public class StringToEnumPropertyTests
{
    private readonly StringToEnumPropertyForger _forger = new();

    [Fact]
    public void StringToEnum_Parse_ValidValue_ShouldConvert()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "High" };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void StringToEnum_Parse_CaseInsensitive_ShouldConvert()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "high" };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void StringToEnum_Parse_InvalidValue_ShouldThrow()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "Invalid" };
        var act = () => _forger.Forge(source);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StringToEnum_NullableSource_NonNullValue_ShouldConvert()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = "Medium" };
        var result = _forger.ForgeFromNullable(source);
        result.Priority.Should().Be(Priority.Medium);
    }

    [Fact]
    public void StringToEnum_ToNullableDest_ShouldConvert()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "Critical" };
        var result = _forger.ForgeToNullable(source);
        result.Priority.Should().Be(Priority.Critical);
    }

    [Fact]
    public void StringToEnum_NullableToNullable_WithValue_ShouldConvert()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = "Low" };
        var result = _forger.ForgeNullableToNullable(source);
        result.Priority.Should().Be(Priority.Low);
    }

    [Fact]
    public void StringToEnum_NullableToNullable_NullValue_ShouldBeNull()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = null };
        var result = _forger.ForgeNullableToNullable(source);
        result.Priority.Should().BeNull();
    }

    [Fact]
    public void StringToEnum_ReverseForge_EnumToString_ShouldUseToString()
    {
        var source = new TicketWithEnumPriority { Id = 1, Name = "Bug", Priority = Priority.High };
        var result = _forger.ForgeReversible(source);
        result.Priority.Should().Be("High");
    }
}

public class StringToEnumTryParseTests
{
    private readonly StringToEnumTryParseForger _forger = new();

    [Fact]
    public void TryParse_ValidValue_ShouldConvert()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "High" };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(Priority.High);
    }

    [Fact]
    public void TryParse_InvalidValue_ShouldFallbackToDefault()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "Invalid" };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(default(Priority));
    }

    [Fact]
    public void TryParse_ToNullable_ValidValue_ShouldConvert()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "Medium" };
        var result = _forger.ForgeToNullable(source);
        result.Priority.Should().Be(Priority.Medium);
    }
}

public class StringToEnumNoneTests
{
    private readonly StringToEnumNoneForger _forger = new();

    [Fact]
    public void None_ShouldNotAutoConvert_PropertyIsDefault()
    {
        var source = new TicketWithStringPriority { Id = 1, Name = "Bug", Priority = "High" };
        var result = _forger.Forge(source);
        // Priority is ignored, so it should be default
        result.Priority.Should().Be(default(Priority));
    }
}

public class StringToEnumSkipNullTests
{
    private readonly StringToEnumSkipNullForger _forger = new();

    [Fact]
    public void SkipNull_StringToEnum_NullSource_ShouldPreserveDefault()
    {
        // string? → enum with SkipNull: null source should preserve the initializer (Critical)
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = null };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(Priority.Critical);
    }

    [Fact]
    public void SkipNull_StringToEnum_NonNullSource_ShouldConvert()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = "Low" };
        var result = _forger.Forge(source);
        result.Priority.Should().Be(Priority.Low);
    }

    [Fact]
    public void SkipNull_EnumToString_NullSource_ShouldPreserveDefault()
    {
        // enum? → string with SkipNull: null source should preserve the initializer ("Default")
        var source = new TicketWithNullableEnumPriority { Id = 1, Priority = null };
        var result = _forger.ForgeEnumToString(source);
        result.Priority.Should().Be("Default");
    }

    [Fact]
    public void SkipNull_EnumToString_NonNullSource_ShouldConvert()
    {
        var source = new TicketWithNullableEnumPriority { Id = 1, Priority = Priority.High };
        var result = _forger.ForgeEnumToString(source);
        result.Priority.Should().Be("High");
    }

    [Fact]
    public void SkipNull_ForgeInto_StringToEnum_NullSource_ShouldPreserveExisting()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = null };
        var dest = new TicketWithEnumPriorityInitialized { Id = 99, Priority = Priority.High };
        _forger.ForgeInto(source, dest);
        dest.Priority.Should().Be(Priority.High); // preserved, not overwritten
        dest.Id.Should().Be(1); // Id still mapped
    }

    [Fact]
    public void SkipNull_ForgeInto_StringToEnum_NonNullSource_ShouldConvert()
    {
        var source = new TicketWithNullableStringPriority { Id = 1, Priority = "Low" };
        var dest = new TicketWithEnumPriorityInitialized { Id = 99, Priority = Priority.High };
        _forger.ForgeInto(source, dest);
        dest.Priority.Should().Be(Priority.Low);
    }

    [Fact]
    public void SkipNull_ForgeInto_EnumToString_NullSource_ShouldPreserveExisting()
    {
        var source = new TicketWithNullableEnumPriority { Id = 1, Priority = null };
        var dest = new TicketWithStringPriorityFromEnum { Id = 99, Priority = "Existing" };
        _forger.ForgeIntoEnumToString(source, dest);
        dest.Priority.Should().Be("Existing"); // preserved, not overwritten
        dest.Id.Should().Be(1);
    }

    [Fact]
    public void SkipNull_ForgeInto_EnumToString_NonNullSource_ShouldConvert()
    {
        var source = new TicketWithNullableEnumPriority { Id = 1, Priority = Priority.Medium };
        var dest = new TicketWithStringPriorityFromEnum { Id = 99, Priority = "Existing" };
        _forger.ForgeIntoEnumToString(source, dest);
        dest.Priority.Should().Be("Medium");
    }
}

#endregion

#region v1.4 ConvertWith Tests


// --- Tests ---

public class ConvertWithTests
{
    private readonly ConvertWithForger _forger = new();

    [Fact]
    public void ConvertWith_TypeBased_ShouldDelegateToConverter()
    {
        var source = new ConvertWithSource { Id = 1, Name = "Widget", Price = 9.99m };
        var result = _forger.ForgeTypeBased(source);

        result.Should().NotBeNull();
        result.Id.Should().Be(1);
        result.DisplayName.Should().Be("[Widget]");
        result.FormattedPrice.Should().Be("$9.99");
    }

    [Fact]
    public void ConvertWith_TypeBased_NullSource_ShouldReturnNull()
    {
        var result = _forger.ForgeTypeBased(null!);
        result.Should().BeNull();
    }

    [Fact]
    public void ConvertWith_MemberBased_ShouldDelegateToField()
    {
        var source = new ConvertWithSource { Id = 2, Name = "Gadget", Price = 19.50m };
        var result = _forger.ForgeMemberBased(source);

        result.Should().NotBeNull();
        result.Id.Should().Be(2);
        result.DisplayName.Should().Be("[Gadget]");
        result.FormattedPrice.Should().Be("$19.50");
    }

    [Fact]
    public void ConvertWith_MemberBased_NullSource_ShouldReturnNull()
    {
        var result = _forger.ForgeMemberBased(null!);
        result.Should().BeNull();
    }
}

#endregion