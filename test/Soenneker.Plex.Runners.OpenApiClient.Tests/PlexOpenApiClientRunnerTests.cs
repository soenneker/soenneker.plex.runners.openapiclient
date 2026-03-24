using Soenneker.Tests.FixturedUnit;
using Xunit;

namespace Soenneker.Plex.Runners.OpenApiClient.Tests;

[Collection("Collection")]
public sealed class PlexOpenApiClientRunnerTests : FixturedUnitTest
{
    public PlexOpenApiClientRunnerTests(Fixture fixture, ITestOutputHelper output) : base(fixture, output)
    {
    }

    [Fact]
    public void Default()
    {

    }
}
