using Booking.Service.Domain.Aggregates;
using NetArchTest.Rules;
using System.Reflection;

namespace Booking.Service.ArchitectureTests;

public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(Event).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Booking.Service.Application.Contracts.Persistence.IBookingRepository).Assembly;
    // Any public type in the Infrastructure assembly works as the assembly-handle anchor.
    // EfBookingRepository is the canonical persistence implementation, so referencing it here
    // both grabs the assembly and reads as a pointer to the real implementation.
    private static readonly Assembly InfrastructureAssembly = typeof(Booking.Service.Infrastructure.Persistence.EfBookingRepository).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Service_Projects()
    {
        var references = DomainAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.Contains("EventHub.BuildingBlocks", references);
        Assert.DoesNotContain("Booking.Service.Application", references);
        Assert.DoesNotContain("Booking.Service.Infrastructure", references);
        Assert.DoesNotContain("Booking.Service.Api", references);

        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Booking.Service.Application", "Booking.Service.Infrastructure", "Booking.Service.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Domain dependency rule failed: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Application_Should_Reference_Only_Domain_And_BuildingBlocks()
    {
        var references = ApplicationAssembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.Contains("Booking.Service.Domain", references);
        Assert.Contains("EventHub.BuildingBlocks", references);
        Assert.DoesNotContain("Booking.Service.Infrastructure", references);
        Assert.DoesNotContain("Booking.Service.Api", references);

        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Booking.Service.Infrastructure", "Booking.Service.Api")
            .GetResult();

        Assert.True(result.IsSuccessful, $"Application dependency rule failed: {Format(result.FailingTypeNames)}");
    }

    [Fact]
    public void Infrastructure_Should_Implement_Interfaces_From_Application()
    {
        var contracts = ApplicationAssembly
            .GetTypes()
            .Where(type => type.IsInterface && type.Namespace is not null && type.Namespace.StartsWith("Booking.Service.Application.Contracts", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(contracts);

        var implementations = InfrastructureAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .ToArray();

        foreach (var contract in contracts)
        {
            Assert.Contains(implementations, implementation => contract.IsAssignableFrom(implementation));
        }
    }

    [Fact]
    public void Api_Should_Be_The_Only_Project_That_References_Infrastructure()
    {
        var domainRefs = DomainAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        var appRefs = ApplicationAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();
        var apiRefs = ApiAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain("Booking.Service.Infrastructure", domainRefs);
        Assert.DoesNotContain("Booking.Service.Infrastructure", appRefs);
        Assert.Contains("Booking.Service.Infrastructure", apiRefs);
    }

    private static string Format(IEnumerable<string>? failingTypeNames)
    {
        if (failingTypeNames is null)
        {
            return "No failing types returned.";
        }

        var typeNames = failingTypeNames as string[] ?? failingTypeNames.ToArray();
        return typeNames.Length == 0 ? "No failing types returned." : string.Join(", ", typeNames);
    }
}
