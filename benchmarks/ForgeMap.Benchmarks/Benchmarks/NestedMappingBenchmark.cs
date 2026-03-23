using BenchmarkDotNet.Attributes;
using ForgeMap.Benchmarks.Mappers;
using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class NestedMappingBenchmark
{
    private OrderSource _source = null!;
    private BenchmarkForger _forger = null!;
    private BenchmarkMapper _mapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new OrderSource
        {
            Id = 1,
            OrderNumber = "ORD-2024-001",
            Total = 299.99m,
            OrderDate = new DateTime(2024, 3, 15),
            Customer = new CustomerSource
            {
                Id = 42,
                Name = "Jane Smith",
                Email = "jane.smith@example.com"
            },
            ShippingAddress = new AddressSource
            {
                Street = "123 Main St",
                City = "Seattle",
                State = "WA",
                ZipCode = "98101"
            }
        };

        _forger = new BenchmarkForger();
        _mapper = new BenchmarkMapper();
        _autoMapper = AutoMapperConfig.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public OrderDestination ForgeMap() => _forger.Forge(_source);

    [Benchmark]
    public OrderDestination Mapperly() => _mapper.Map(_source);

    [Benchmark]
    public OrderDestination AutoMapper() => _autoMapper.Map<OrderDestination>(_source);
}
