using System.Text;
using FluentAssertions;
using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Signatures;
using HookGuard.Domain.Time;
using HookGuard.Infrastructure;
using HookGuard.Infrastructure.Deliveries;
using HookGuard.Infrastructure.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HookGuard.IntegrationTests.Deliveries;

/// <summary>
/// M2.3. What the sender promises is about what crosses the wire -- the bytes, the signature over
/// them, the idempotency key, and how long it waits -- so the other end of that wire is a real HTTP
/// endpoint rather than a double that can only agree with itself.
/// </summary>
public sealed class HttpDeliverySenderTests
{
    private const string Secret = "destination-signing-secret-32-chars";
    private const string IdempotencyKey = "0191f0a0-0000-7000-8000-000000000101";

    private static readonly ReadOnlyMemory<byte> Payload =
        Encoding.UTF8.GetBytes("""{"TransID":"QK123456","Amount":1500}""");

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Signs_the_bytes_it_sends_and_carries_the_key_it_was_given()
    {
        var captured = new TaskCompletionSource<CapturedRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var destination = await DestinationStub.StartAsync(async context =>
        {
            captured.TrySetResult(await CapturedRequest.ReadAsync(context));
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        await using var provider = Provider();

        var result = await provider.GetRequiredService<IDeliverySender>()
            .SendAsync(Request(destination.Url), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.StatusCode.Should().Be(204);

        var request = await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Signed over the bytes that were sent, so the destination can verify them rather than a
        // re-serialisation that happens to look the same.
        request.Body.Should().Equal(Payload.ToArray());

        var verification = new SignatureVerifier(new SystemClock())
            .Verify(Secret, request.Headers, request.Body, TimeSpan.FromMinutes(5));

        verification.IsValid.Should().BeTrue("an outgoing request has to verify under the destination's own secret");
        request.Headers[HttpDeliverySender.IdempotencyKeyHeader].Should().Be(IdempotencyKey);
    }

    [Fact]
    public async Task Reports_a_refusal_as_a_failed_attempt_rather_than_throwing()
    {
        await using var destination = await DestinationStub.StartAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

            return Task.CompletedTask;
        });

        await using var provider = Provider();

        var result = await provider.GetRequiredService<IDeliverySender>()
            .SendAsync(Request(destination.Url), CancellationToken.None);

        // A refusal is something to record and retry, so it has to come back as a result: an
        // exception here would lose the attempt instead of scheduling the next one.
        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().Be(503);
        result.Error.Should().Contain("503");
    }

    [Fact]
    public async Task Gives_up_on_a_destination_that_does_not_answer_in_time()
    {
        await using var destination = await DestinationStub.StartAsync(async context =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The caller gave up, which is exactly what this endpoint is here to provoke.
            }
        });

        await using var provider = Provider();

        var result = await provider.GetRequiredService<IDeliverySender>()
            .SendAsync(Request(destination.Url, timeout: TimeSpan.FromSeconds(1)), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().BeNull("the destination never answered, so there is no status to record");
        result.Error.Should().Contain("did not answer within");
    }

    [Fact]
    public async Task Reports_an_unreachable_host_rather_than_throwing()
    {
        await using var provider = Provider();

        // Nothing is listening on port 1, which is what a destination that is down looks like.
        var result = await provider.GetRequiredService<IDeliverySender>()
            .SendAsync(Request("http://127.0.0.1:1/hooks"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.Error.Should().Contain("could not be reached");
    }

    [Fact]
    public void Leaves_the_client_timeout_off_so_a_slow_destination_is_not_cut_short()
    {
        using var provider = Provider();

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IDeliverySender));

        // Each attempt carries its own budget. A fixed client timeout would silently override both
        // the destination's configuration and the per-request deadline.
        client.Timeout.Should().Be(
            System.Threading.Timeout.InfiniteTimeSpan,
            "the attempt's own deadline is the only thing that may end it");
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection();

        // The composition root is what supplies the clock, so the sender's dependencies have to be
        // supplied the same way here.
        services.AddSingleton<IClock, SystemClock>();
        services.AddHookGuardDelivery();

        return services.BuildServiceProvider();
    }

    private static DeliveryRequest Request(string url, TimeSpan? timeout = null) =>
        new(IdempotencyKey, url, Secret, timeout ?? AttemptTimeout, Payload);

    /// <summary>What the destination received, as a verifier on the other side of the wire would see it.</summary>
    private sealed record CapturedRequest(IReadOnlyDictionary<string, string> Headers, byte[] Body)
    {
        public static async Task<CapturedRequest> ReadAsync(HttpContext context)
        {
            using var buffer = new MemoryStream();
            await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var header in context.Request.Headers)
            {
                headers[header.Key] = header.Value.ToString();
            }

            return new CapturedRequest(headers, buffer.ToArray());
        }
    }

    /// <summary>
    /// A real HTTP endpoint for one test, on a port the operating system picks so parallel runs
    /// cannot collide.
    /// </summary>
    private sealed class DestinationStub : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private DestinationStub(WebApplication app, string url)
        {
            _app = app;
            Url = url;
        }

        public string Url { get; }

        public static async Task<DestinationStub> StartAsync(RequestDelegate handler)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");

            var app = builder.Build();
            app.Run(handler);

            await app.StartAsync();

            var addresses = app.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()!
                .Addresses;

            return new DestinationStub(app, addresses.First());
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
