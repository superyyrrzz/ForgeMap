using BenchmarkDotNet.Attributes;
using ForgeMap.Benchmarks.Mappers;
using ForgeMap.Benchmarks.Models;

namespace ForgeMap.Benchmarks.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class DeepGraphBenchmark
{
    private CompanySource _source = null!;
    private BenchmarkForger _forger = null!;
    private BenchmarkMapper _mapper = null!;
    private AutoMapper.IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _source = new CompanySource
        {
            Id = 1,
            Name = "Contoso Ltd",
            Department = new DepartmentSource
            {
                Id = 10,
                Name = "Engineering",
                Code = "ENG",
                Team = new TeamSource
                {
                    Id = 100,
                    Name = "Platform",
                    Lead = new EmployeeSource
                    {
                        Id = 1000,
                        FirstName = "Alice",
                        LastName = "Johnson",
                        Title = "Staff Engineer"
                    }
                }
            }
        };

        _forger = new BenchmarkForger();
        _mapper = new BenchmarkMapper();
        _autoMapper = AutoMapperConfig.CreateMapper();
    }

    [Benchmark(Baseline = true)]
    public CompanyDestination ForgeMap() => _forger.Forge(_source);

    [Benchmark]
    public CompanyDestination Mapperly() => _mapper.Map(_source);

    [Benchmark]
    public CompanyDestination AutoMapper() => _autoMapper.Map<CompanyDestination>(_source);
}
