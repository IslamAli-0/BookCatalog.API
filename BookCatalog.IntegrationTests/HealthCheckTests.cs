using System.Net;

namespace BookCatalog.IntegrationTests;

/// <summary>
/// Integration tests for the health check endpoints.
/// </summary>
public class HealthCheckTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public HealthCheckTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LivenessEndpoint_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health/live");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }

    [Fact]
    public async Task ReadinessEndpoint_WhenDatabaseIsUp_ReturnsHealthy()
    {
        // Act
        var response = await _client.GetAsync("/health/ready");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Healthy", body);
    }
}
