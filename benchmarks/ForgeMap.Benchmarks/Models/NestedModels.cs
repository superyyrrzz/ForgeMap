namespace ForgeMap.Benchmarks.Models;

// Source types

public class OrderSource
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime OrderDate { get; set; }
    public CustomerSource Customer { get; set; } = null!;
    public AddressSource ShippingAddress { get; set; } = null!;
}

public class CustomerSource
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class AddressSource
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

// Destination types

public class OrderDestination
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime OrderDate { get; set; }
    public CustomerDestination Customer { get; set; } = null!;
    public AddressDestination ShippingAddress { get; set; } = null!;
}

public class CustomerDestination
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class AddressDestination
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
