using Soenneker.Tests.HostedUnit;

namespace Soenneker.Plex.Runners.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class PlexOpenApiClientRunnerTests : HostedUnitTest
{
    public PlexOpenApiClientRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
