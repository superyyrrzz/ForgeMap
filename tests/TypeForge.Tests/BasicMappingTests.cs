using FluentAssertions;
using TypeForge;
using Xunit;

namespace TypeForge.Tests;

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

#endregion

#region Forgers

[TypeForge]
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

