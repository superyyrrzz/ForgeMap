using BenchmarkDotNet.Attributes;
using ForgeMap.Benchmarks.Mappers;
using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
[GroupBenchmarksBy(BenchmarkDotNet.Configs.BenchmarkLogicalGroupRule.ByParams)]
public class CollectionMappingBenchmark
{
    private List<SimpleSource> _items = null!;
    private BenchmarkForger _forger = null!;
    private BenchmarkMapper _mapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [Params(100, 1000)]
    public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _forger = new BenchmarkForger();
        _mapper = new BenchmarkMapper();
        _autoMapper = AutoMapperConfig.CreateMapper();

        _items = Enumerable.Range(0, Count).Select(CreateSource).ToList();
    }

    private static SimpleSource CreateSource(int i) => new()
    {
        Id = i,
        FirstName = $"First{i}",
        LastName = $"Last{i}",
        Email = $"user{i}@example.com",
        Age = 20 + (i % 50),
        Salary = 40000m + (i * 100),
        IsActive = i % 2 == 0,
        CreatedAt = new DateTime(2024, 1, 1).AddDays(i),
        UpdatedAt = i % 3 == 0 ? new DateTime(2024, 6, 1) : null,
        Department = $"Dept{i % 5}"
    };

    [Benchmark(Baseline = true)]
    public List<SimpleDestination> ForgeMap() => _forger.Forge(_items);

    [Benchmark]
    public List<SimpleDestination> Mapperly() => _mapper.Map(_items);

    [Benchmark]
    public List<SimpleDestination> AutoMapper()
        => _autoMapper.Map<List<SimpleDestination>>(_items);
}
