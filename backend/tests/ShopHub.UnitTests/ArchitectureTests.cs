using System.Reflection;
using FluentAssertions;
using ShopHub.Domain.Common;

namespace ShopHub.UnitTests;

public class ArchitectureTests
{
    // Domain must not depend on outer layers (Clean Architecture rule)
    [Fact]
    public void Domain_has_no_reference_to_outer_layers()
    {
        var refs = typeof(Money).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();

        refs.Should().NotContain(["ShopHub.Application", "ShopHub.Infrastructure", "ShopHub.Api"]);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var refs = Assembly.Load("ShopHub.Application").GetReferencedAssemblies().Select(a => a.Name).ToList();

        refs.Should().NotContain(["ShopHub.Infrastructure", "ShopHub.Api"]);
    }
}
