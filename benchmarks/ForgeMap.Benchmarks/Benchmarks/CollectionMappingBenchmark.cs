using BenchmarkDotNet.Attributes;
using ForgeMap.Benchmarks.Mappers;
using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CollectionMappingBenchmark
{
    private List<SimpleSource> _small = null!;
    private List<SimpleSource> _large = null!;
    private BenchmarkForger _forger = null!;
    private BenchmarkMapper _mapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _forger = new BenchmarkForger();
        _mapper = new BenchmarkMapper();
        _autoMapper = AutoMapperConfig.CreateMapper();

        _small = Enumerable.Range(0, 100).Select(CreateSource).ToList();
        _large = Enumerable.Range(0, 1000).Select(CreateSource).ToList();
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
    public List<SimpleDestination> ForgeMap_100() => _forger.Forge(_small);

    [Benchmark]
    public List<SimpleDestination> Mapperly_100() => _mapper.Map(_small);

    [Benchmark]
    public List<SimpleDestination> AutoMapper_100()
        => _autoMapper.Map<List<SimpleDestination>>(_small);

    [Benchmark]
    public List<SimpleDestination> ForgeMap_1000() => _forger.Forge(_large);

    [Benchmark]
    public List<SimpleDestination> Mapperly_1000() => _mapper.Map(_large);

    [Benchmark]
    public List<SimpleDestination> AutoMapper_1000()
        => _autoMapper.Map<List<SimpleDestination>>(_large);
}
