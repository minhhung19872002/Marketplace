using System.Reflection;

namespace ShopHub.UnitTests;

public class SolutionStructureTests
{
    // Domain must not depend on outer layers (Clean Architecture rule)
    [Fact]
    public void Domain_has_no_reference_to_outer_layers()
    {
        var domain = Assembly.Load("ShopHub.Domain");
        var refs = domain.GetReferencedAssemblies().Select(a => a.Name).ToList();

        Assert.DoesNotContain("ShopHub.Application", refs);
        Assert.DoesNotContain("ShopHub.Infrastructure", refs);
        Assert.DoesNotContain("ShopHub.Api", refs);
    }
}
