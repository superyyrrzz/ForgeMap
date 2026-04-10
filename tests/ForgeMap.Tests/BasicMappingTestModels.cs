using System.Collections.Generic;
using ForgeMap;

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

#region v0.6 Test Models

public class OrderEntityV6
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
}

public class OrderDtoV6
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

#endregion

#region Auto-Wire Models

public class AutoWirePhoneEntity
{
    public string Number { get; set; } = string.Empty;
    public string AreaCode { get; set; } = string.Empty;
}

public class AutoWirePhoneDto
{
    public string Number { get; set; } = string.Empty;
    public string AreaCode { get; set; } = string.Empty;
}

public class AutoWirePersonEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressEntity? Address { get; set; }
    public AutoWirePhoneEntity? Phone { get; set; }
}

public class AutoWirePersonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AddressDto? Address { get; set; }
    public AutoWirePhoneDto? Phone { get; set; }
}

#endregion

#region v1.4 Nested Existing-Target Models

public class EtCustomerUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class EtCustomer
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class EtAddressUpdateDto
{
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class EtAddress
{
    public string City { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class EtOrderUpdateDto
{
    public string Status { get; set; } = string.Empty;
    public EtCustomerUpdateDto? Customer { get; set; }
    public EtAddressUpdateDto? ShippingAddress { get; set; }
    public List<EtOrderItemUpdateDto>? Items { get; set; }
}

public class EtOrderItemUpdateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

public class EtOrder
{
    public string Status { get; set; } = string.Empty;
    public EtCustomer? Customer { get; set; }
    public EtAddress? ShippingAddress { get; set; }
    public List<EtOrderItem>? Items { get; set; }
}

public class EtOrderItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

#endregion

#region v1.4 String→Enum Models

public enum Priority { Low, Medium, High, Critical }

public class TicketWithStringPriority
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
}

public class TicketWithEnumPriority
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Priority Priority { get; set; }
}

public class TicketWithNullableStringPriority
{
    public int Id { get; set; }
    public string? Priority { get; set; }
}

public class TicketWithNullableEnumPriority
{
    public int Id { get; set; }
    public Priority? Priority { get; set; }
}

public class TicketWithEnumPriorityInitialized
{
    public int Id { get; set; }
    public Priority Priority { get; set; } = Priority.Critical;
}

public class TicketWithStringPriorityFromEnum
{
    public int Id { get; set; }
    public string Priority { get; set; } = "Default";
}

#endregion

#region v1.4 ConvertWith Models

public class ConvertWithSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class ConvertWithDest
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string FormattedPrice { get; set; } = string.Empty;
}

public class ConvertWithSourceToDestConverter : ITypeConverter<ConvertWithSource, ConvertWithDest>
{
    public ConvertWithDest Convert(ConvertWithSource source)
    {
        return new ConvertWithDest
        {
            Id = source.Id,
            DisplayName = $"[{source.Name}]",
            FormattedPrice = string.Format(System.Globalization.CultureInfo.InvariantCulture, "${0:F2}", source.Price)
        };
    }
}

#endregion

#region v1.6 Per-Property ConvertWith Models

public class PropertyConvertWithSource
{
    public int Id { get; set; }
    public string UserType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class PropertyConvertWithDest
{
    public int Id { get; set; }
    public int UserTypeCode { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion
