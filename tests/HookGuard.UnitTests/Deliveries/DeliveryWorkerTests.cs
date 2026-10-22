using System.Text;
using FluentAssertions;
using HookGuard.Application.Deliveries;
using HookGuard.Domain.Deliveries;
using HookGuard.Domain.Destinations;
using HookGuard.Domain.Events;
using HookGuard.Domain.Retry;
using HookGuard.UnitTests.Fakes;

namespace HookGuard.UnitTests.Deliveries;

/// <summary>
/// The worker's contract: an attempt happens only for a delivery it managed to claim, and whatever
/// came back -- acceptance, refusal, or nothing at all -- ends up on the row.
/// </summary>
public class DeliveryWorkerTests
{
    private const string Secret = "destination-signing-secret";
    private const string DestinationUrl = "https://merchant.example/hooks";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
    private static readonly ReadOnlyMemory<byte> Payload = Encoding.UTF8.GetBytes("""{"TransID":"QK123456"}""");

    private readonly FakeClock _clock = new(Now);

    [Fact]
    public async Task Sends_a_claimed_delivery_and_records_the_acceptance()
    {
        var claimed = Claimed();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender();
        var worker = new DeliveryWorker(store, sender, Retry());

        var processed = await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None);

        processed.Should().BeTrue();
        sender.Sent.Should().HaveCount(1);
        claimed.Delivery.Status.Should().Be(DeliveryStatus.Succeeded);

        store.Recorded.Should().HaveCount(1);
        store.Recorded[0].Should().BeSameAs(claimed.Delivery, "the row is what the outcome is written to");
    }

    [Fact]
    public async Task Sends_nothing_when_there_is_no_delivery_to_claim()
    {
        var store = new FakeDeliveryStore();
        var sender = new FakeDeliverySender();
        var worker = new DeliveryWorker(store, sender, Retry());

        var processed = await worker.ProcessAsync(Guid.CreateVersion7(), CancellationToken.None);

        processed.Should().BeFalse("a duplicate or stale message names a delivery nobody can claim");
        sender.Sent.Should().BeEmpty();
        store.Recorded.Should().BeEmpty();
    }

    [Fact]
    public async Task Does_not_attempt_a_delivery_the_first_message_already_finished()
    {
        var claimed = Claimed();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender();
        var worker = new DeliveryWorker(store, sender, Retry());

        // The same message twice, which is exactly what at-least-once delivery allows. Only the
        // claim decides, so the second one is harmless rather than a second delivery.
        (await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None)).Should().BeTrue();
        (await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None)).Should().BeFalse();

        sender.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task Records_a_refusal_and_schedules_the_retry_the_policy_asked_for()
    {
        var claimed = Claimed();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender { Answer = _ => DeliveryResult.Refused(503) };
        var worker = new DeliveryWorker(store, sender, Retry(draw: 750));

        (await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None)).Should().BeTrue();

        claimed.Delivery.Status.Should().Be(DeliveryStatus.Pending, "a refused attempt is still owed a retry");
        claimed.Delivery.LastError.Should().Be("The destination answered HTTP 503.");

        // The worker only carries out the policy's answer: the wait is drawn from the first window, a
        // second wide, so a jitter of 750 ms puts the next attempt at now plus 750 ms.
        claimed.Delivery.NextAttemptAt.Should().Be(Now.AddMilliseconds(750));
        store.Recorded.Should().HaveCount(1);
    }

    [Fact]
    public async Task Parks_the_delivery_once_the_destination_has_had_every_attempt_it_allows()
    {
        var claimed = WithTheLastAttemptLeft();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender { Answer = _ => DeliveryResult.Refused(503) };
        var worker = new DeliveryWorker(store, sender, Retry());

        (await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None)).Should().BeTrue();

        // The attempts the destination allows are spent, so the delivery is parked for somebody to
        // look at instead of being retried again or dropped without trace.
        claimed.Delivery.Status.Should().Be(DeliveryStatus.DeadLettered);
        claimed.Delivery.LastError.Should().Be("The destination answered HTTP 503.");
        claimed.Delivery.ClaimedAt.Should().BeNull("a parked delivery is nobody's to work on");
        store.Recorded.Should().HaveCount(1, "the decision has to reach the row, not just the object");
    }

    [Fact]
    public async Task Stands_in_a_reason_when_the_attempt_failed_without_one()
    {
        var claimed = Claimed();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender
        {
            Answer = _ => new DeliveryResult(Succeeded: false, StatusCode: null, Error: null),
        };
        var worker = new DeliveryWorker(store, sender, Retry());

        await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None);

        claimed.Delivery.LastError.Should()
            .NotBeNullOrWhiteSpace("the row always carries a reason, so a failure can be diagnosed from it");
    }

    [Fact]
    public async Task Sends_the_payload_verbatim_under_a_key_that_survives_a_retry()
    {
        var claimed = Claimed();
        var store = new FakeDeliveryStore(claimed);
        var sender = new FakeDeliverySender();
        var worker = new DeliveryWorker(store, sender, Retry());

        await worker.ProcessAsync(claimed.Delivery.Id, CancellationToken.None);

        var request = sender.Sent.Should().ContainSingle().Subject;

        // The destination verifies its signature over exactly these bytes, so the payload is passed
        // through rather than re-serialised.
        request.Payload.ToArray().Should().Equal(claimed.Event.Payload.ToArray());
        request.DestinationUrl.Should().Be(DestinationUrl);
        request.SigningSecret.Should().Be(Secret);
        request.Timeout.Should().Be(claimed.Destination.Timeout);

        // The delivery id, not a fresh one per attempt: a retry has to look like the same intent to a
        // destination deduplicating on this header.
        request.IdempotencyKey.Should().Be(claimed.Delivery.Id.ToString());
    }

    private static ClaimedDelivery Claimed()
    {
        var destination = Destination.Configure(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            DestinationUrl,
            Secret,
            maxAttempts: 5,
            timeout: TimeSpan.FromSeconds(10));

        var @event = Event.Record(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "QK123456",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Payload,
            Now,
            signatureValid: true);

        return new ClaimedDelivery(
            Delivery.Schedule(Guid.CreateVersion7(), @event.Id, destination.Id, Now),
            @event,
            destination);
    }

    /// <summary>
    /// The same delivery after every attempt but the last has been spent, so the claim the worker
    /// makes is the final one the destination allows.
    /// </summary>
    private static ClaimedDelivery WithTheLastAttemptLeft()
    {
        var claimed = Claimed();

        for (var attempt = 1; attempt < claimed.Destination.MaxAttempts; attempt++)
        {
            claimed.Delivery.BeginAttempt(Now);
            claimed.Delivery.Fail("The destination answered HTTP 503.", Now);
        }

        return claimed;
    }

    /// <param name="draw">The jitter the policy will settle on, in milliseconds.</param>
    private RetryPolicy Retry(int draw = 0) => new(new FakeRandom(draw), _clock);
}
