using System.Collections.Generic;
using ForgeMap;

namespace ForgeMap.Tests;

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

#region v0.6 Forgers

[ForgeMap]
public partial class HookForger
{
    // BeforeForge only
    [BeforeForge(nameof(ValidateOrder))]
    public partial OrderDtoV6 Forge(OrderEntityV6 source);

    // AfterForge only
    [AfterForge(nameof(EnrichOrder))]
    public partial OrderDto Forge(OrderEntity source);

    // Both BeforeForge and AfterForge
    [BeforeForge(nameof(ValidateOrder))]
    [AfterForge(nameof(EnrichOrderV6))]
    public partial OrderDtoV6 ForgeWithBoth(OrderEntityV6 source);

    // Multiple BeforeForge hooks
    [BeforeForge(nameof(ValidateOrder))]
    [BeforeForge(nameof(LogOrder))]
    public partial OrderDtoV6 ForgeMultiBefore(OrderEntityV6 source);

    // ForgeInto with hooks
    [BeforeForge(nameof(ValidateOrder))]
    [AfterForge(nameof(EnrichOrderV6))]
    public partial void ForgeInto(OrderEntityV6 source, [UseExistingValue] OrderDtoV6 destination);

    // Hook methods

    private static void ValidateOrder(OrderEntityV6 source)
    {
        if (source.Id <= 0) throw new ArgumentException("Id must be positive", nameof(source));
    }

    private static void EnrichOrder(OrderEntity source, OrderDto destination)
    {
        destination.Name = $"Order #{source.Id} - {destination.Name}";
    }

    private static void EnrichOrderV6(OrderEntityV6 source, OrderDtoV6 destination)
    {
        destination.DisplayName = $"Order #{source.Id} - {source.Name}";
    }

    [ThreadStatic]
    internal static bool LogCalled;

    private static void LogOrder(OrderEntityV6 source)
    {
        _ = source; // Suppress IDE0060
        LogCalled = true;
    }
}

#endregion

#region Auto-Wire Forger

[ForgeMap]
public partial class AutoWireForger
{
    // These forge methods should be auto-discovered for nested properties
    public partial AddressDto Forge(AddressEntity source);
    public partial AutoWirePhoneDto Forge(AutoWirePhoneEntity source);

    // No explicit [ForgeWith] — auto-wiring should handle Address and Phone
    public partial AutoWirePersonDto Forge(AutoWirePersonEntity source);
}

#endregion

#region v1.4 Nested Existing-Target Forger

[ForgeMap]
public partial class ExistingTargetForger
{
    public partial void ForgeInto(EtCustomerUpdateDto source, [UseExistingValue] EtCustomer target);
    public partial void ForgeInto(EtAddressUpdateDto source, [UseExistingValue] EtAddress target);
    public partial void ForgeInto(EtOrderItemUpdateDto source, [UseExistingValue] EtOrderItem target);

    public partial EtOrderItem Forge(EtOrderItemUpdateDto source);

    [ForgeProperty("Customer", "Customer", ExistingTarget = true)]
    [ForgeProperty("ShippingAddress", "ShippingAddress", ExistingTarget = true)]
    public partial void ForgeInto(EtOrderUpdateDto source, [UseExistingValue] EtOrder target);

    [ForgeProperty("Customer", "Customer", ExistingTarget = true)]
    [ForgeProperty("ShippingAddress", "ShippingAddress", ExistingTarget = true)]
    [ForgeProperty("Items", "Items", ExistingTarget = true,
        CollectionUpdate = CollectionUpdateStrategy.Sync, KeyProperty = "Id")]
    public partial void ForgeIntoWithSync(EtOrderUpdateDto source, [UseExistingValue] EtOrder target);
}

#endregion

#region v1.4 String→Enum Forgers

[ForgeMap]
public partial class StringToEnumPropertyForger
{
    // string → enum (Parse, default)
    public partial TicketWithEnumPriority Forge(TicketWithStringPriority source);

    // string? → enum
    public partial TicketWithEnumPriority ForgeFromNullable(TicketWithNullableStringPriority source);

    // string → enum?
    public partial TicketWithNullableEnumPriority ForgeToNullable(TicketWithStringPriority source);

    // string? → enum?
    public partial TicketWithNullableEnumPriority ForgeNullableToNullable(TicketWithNullableStringPriority source);

    // ReverseForge: string→enum forward, enum→string reverse
    [ReverseForge]
    public partial TicketWithEnumPriority ForgeReversible(TicketWithStringPriority source);
}

[ForgeMap(StringToEnum = StringToEnumConversion.TryParse)]
public partial class StringToEnumTryParseForger
{
    public partial TicketWithEnumPriority Forge(TicketWithStringPriority source);

    public partial TicketWithNullableEnumPriority ForgeToNullable(TicketWithStringPriority source);
}

[ForgeMap(StringToEnum = StringToEnumConversion.None)]
public partial class StringToEnumNoneForger
{
    [Ignore(nameof(TicketWithEnumPriority.Priority))]
    public partial TicketWithEnumPriority Forge(TicketWithStringPriority source);
}

[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial class StringToEnumSkipNullForger
{
    // string? → enum (SkipNull: should skip assignment when null, preserving default)
    public partial TicketWithEnumPriorityInitialized Forge(TicketWithNullableStringPriority source);

    // enum? → string (SkipNull: should skip assignment when null, preserving default)
    public partial TicketWithStringPriorityFromEnum ForgeEnumToString(TicketWithNullableEnumPriority source);

    // ForgeInto: string? → enum (SkipNull)
    public partial void ForgeInto(TicketWithNullableStringPriority source, [UseExistingValue] TicketWithEnumPriorityInitialized destination);

    // ForgeInto: enum? → string (SkipNull)
    public partial void ForgeIntoEnumToString(TicketWithNullableEnumPriority source, [UseExistingValue] TicketWithStringPriorityFromEnum destination);
}

#endregion

#region v1.4 ConvertWith Forger

// --- Forger with [ConvertWith] ---

[ForgeMap]
public partial class ConvertWithForger
{
    private readonly ConvertWithSourceToDestConverter _converter = new();

    [ConvertWith(typeof(ConvertWithSourceToDestConverter))]
    public partial ConvertWithDest ForgeTypeBased(ConvertWithSource source);

    [ConvertWith(nameof(_converter))]
    public partial ConvertWithDest ForgeMemberBased(ConvertWithSource source);
}

#endregion
