using ForgeMap.Benchmarks.Models;
using Riok.Mapperly.Abstractions;

namespace ForgeMap.Benchmarks.Mappers;

[Mapper]
public partial class BenchmarkMapper
{
    // Simple
    public partial SimpleDestination Map(SimpleSource source);

    // Nested
    public partial OrderDestination Map(OrderSource source);
    public partial CustomerDestination Map(CustomerSource source);
    public partial AddressDestination Map(AddressSource source);

    // Collection
    public partial List<SimpleDestination> Map(List<SimpleSource> source);

    // Deep graph
    public partial CompanyDestination Map(CompanySource source);
    public partial DepartmentDestination Map(DepartmentSource source);
    public partial TeamDestination Map(TeamSource source);
    public partial EmployeeDestination Map(EmployeeSource source);
}
