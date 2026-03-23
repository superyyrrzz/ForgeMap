using AutoMapper;
using ForgeMap.Benchmarks.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ForgeMap.Benchmarks.Mappers;

public static class AutoMapperConfig
{
    private static readonly MapperConfiguration Configuration = new(cfg =>
    {
        cfg.CreateMap<SimpleSource, SimpleDestination>();

        cfg.CreateMap<OrderSource, OrderDestination>();
        cfg.CreateMap<CustomerSource, CustomerDestination>();
        cfg.CreateMap<AddressSource, AddressDestination>();

        cfg.CreateMap<CompanySource, CompanyDestination>();
        cfg.CreateMap<DepartmentSource, DepartmentDestination>();
        cfg.CreateMap<TeamSource, TeamDestination>();
        cfg.CreateMap<EmployeeSource, EmployeeDestination>();
    }, NullLoggerFactory.Instance);

    public static IMapper CreateMapper() => Configuration.CreateMapper();
}
