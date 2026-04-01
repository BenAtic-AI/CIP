using Cip.Application.Features.Health;
using Xunit;

namespace Cip.UnitTests;

public sealed class ApplicationHealthServiceTests
{
    [Fact]
    public void GetStatus_ReturnsExpectedModuleNames()
    {
        var service = new ApplicationHealthService();

        var status = service.GetStatus("Development");

        Assert.Equal("cip-api", status.Service);
        Assert.Contains("Profiles", status.Modules);
    }
}
