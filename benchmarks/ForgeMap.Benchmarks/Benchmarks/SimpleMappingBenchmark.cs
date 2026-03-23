using BenchmarkDotNet.Attributes;
using ForgeMap.Benchmarks.Mappers;
using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class SimpleMappingBenchmark
{
    private SimpleSource _source = null!;
    private BenchmarkForger _forger = null!;
    private BenchmarkMapper _mapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new SimpleSource
        {
            Id = 1,
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            Age = 30,
            Salary = 75000.50m,
            IsActive = true,
            CreatedAt = new DateTime(2024, 1, 15),
            UpdatedAt = new DateTime(2024, 6, 1),
            Department = "Engineering"
        };

        _forger = new BenchmarkForger();
        _mapper = new BenchmarkMapper();
        _autoMapper = AutoMapperConfig.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public SimpleDestination ForgeMap() => _forger.Forge(_source);

    [Benchmark]
    public SimpleDestination Mapperly() => _mapper.Map(_source);

    [Benchmark]
    public SimpleDestination AutoMapper() => _autoMapper.Map<SimpleDestination>(_source);
}
