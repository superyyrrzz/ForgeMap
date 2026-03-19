using FluentAssertions;
using ForgeMap;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ForgeMap.Tests;

#region DI Test Models

public class DiSourceEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class DiDestDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

#endregion

#region Forgers for DI tests

[ForgeMap]
public partial class DiSimpleForger
{
    public partial DiDestDto Forge(DiSourceEntity source);
}

[ForgeMap]
public partial class DiServiceProviderForger
{
    // Stored for future DI-based converter resolution (v1.1+)
    public IServiceProvider Services { get; }

    public DiServiceProviderForger(IServiceProvider services) => Services = services;

    public partial DiDestDto Forge(DiSourceEntity source);
}

#endregion

public class DiIntegrationTests
{
    [Fact]
    public void AddForgeMaps_ShouldRegisterAllForgers_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps();

        using var provider = services.BuildServiceProvider();

        // Should be able to resolve DiSimpleForger
        var simpleForger = provider.GetService<DiSimpleForger>();
        simpleForger.Should().NotBeNull();

        // Should be able to resolve DiServiceProviderForger
        var spForger = provider.GetService<DiServiceProviderForger>();
        spForger.Should().NotBeNull();
    }

    [Fact]
    public void AddForgeMaps_Singleton_ShouldReturnSameInstance()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();

        var forger1 = provider.GetRequiredService<DiSimpleForger>();
        var forger2 = provider.GetRequiredService<DiSimpleForger>();

        forger1.Should().BeSameAs(forger2);
    }

    [Fact]
    public void AddForgeMaps_Scoped_ShouldReturnDifferentInstancesAcrossScopes()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps(ServiceLifetime.Scoped);

        using var provider = services.BuildServiceProvider();

        DiSimpleForger forger1;
        DiSimpleForger forger1Again;
        DiSimpleForger forger2;

        using (var scope1 = provider.CreateScope())
        {
            forger1 = scope1.ServiceProvider.GetRequiredService<DiSimpleForger>();
            forger1Again = scope1.ServiceProvider.GetRequiredService<DiSimpleForger>();
        }
        using (var scope2 = provider.CreateScope())
        {
            forger2 = scope2.ServiceProvider.GetRequiredService<DiSimpleForger>();
        }

        forger1.Should().BeSameAs(forger1Again);
        forger1.Should().NotBeSameAs(forger2);
    }

    [Fact]
    public void AddForgeMaps_ResolvedForger_ShouldWorkCorrectly()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps();

        using var provider = services.BuildServiceProvider();
        var forger = provider.GetRequiredService<DiSimpleForger>();

        var entity = new DiSourceEntity { Id = 42, Name = "Test" };
        var dto = forger.Forge(entity);

        dto.Should().NotBeNull();
        dto.Id.Should().Be(42);
        dto.Name.Should().Be("Test");
    }

    [Fact]
    public void AddForgeMaps_ServiceProviderForger_ShouldReceiveProvider()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps();

        using var provider = services.BuildServiceProvider();
        var forger = provider.GetRequiredService<DiServiceProviderForger>();

        var entity = new DiSourceEntity { Id = 7, Name = "DI Test" };
        var dto = forger.Forge(entity);

        forger.Services.Should().NotBeNull();
        dto.Should().NotBeNull();
        dto.Id.Should().Be(7);
        dto.Name.Should().Be("DI Test");
    }

    [Fact]
    public void AddForgeMaps_Transient_ShouldReturnNewInstanceEachTime()
    {
        var services = new ServiceCollection();
        services.AddForgeMaps(ServiceLifetime.Transient);

        using var provider = services.BuildServiceProvider();

        var forger1 = provider.GetRequiredService<DiSimpleForger>();
        var forger2 = provider.GetRequiredService<DiSimpleForger>();

        forger1.Should().NotBeSameAs(forger2);
    }
}
