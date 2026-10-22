using Testcontainers.RabbitMq;

namespace HookGuard.IntegrationTests.Messaging;

/// <summary>
/// One broker for every consumer test in the class. A broker is slow to start and nothing here
/// depends on the previous test's traffic, so the container is shared and the queue is emptied
/// between tests instead.
/// </summary>
public sealed class RabbitMqFixture : IAsyncLifetime
{
    private readonly RabbitMqContainer _container = new RabbitMqBuilder("rabbitmq:4-management-alpine")
        .WithUsername("hookguard")
        .WithPassword("hookguard")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
