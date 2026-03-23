using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Mappers;

[ForgeMap]
public partial class BenchmarkForger
{
    // Simple flat mapping
    public partial SimpleDestination Forge(SimpleSource source);

    // Nested mapping
    [ForgeWith(nameof(OrderDestination.Customer), nameof(Forge))]
    [ForgeWith(nameof(OrderDestination.ShippingAddress), nameof(Forge))]
    public partial OrderDestination Forge(OrderSource source);
    public partial CustomerDestination Forge(CustomerSource source);
    public partial AddressDestination Forge(AddressSource source);

    // Collection mapping
    public partial List<SimpleDestination> Forge(List<SimpleSource> source);

    // Deep graph mapping
    [ForgeWith(nameof(CompanyDestination.Department), nameof(Forge))]
    public partial CompanyDestination Forge(CompanySource source);
    [ForgeWith(nameof(DepartmentDestination.Team), nameof(Forge))]
    public partial DepartmentDestination Forge(DepartmentSource source);
    [ForgeWith(nameof(TeamDestination.Lead), nameof(Forge))]
    public partial TeamDestination Forge(TeamSource source);
    public partial EmployeeDestination Forge(EmployeeSource source);
}
