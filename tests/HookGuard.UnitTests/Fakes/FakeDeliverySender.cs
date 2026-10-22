using HookGuard.Application.Abstractions;
using HookGuard.Application.Deliveries;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// Stands in for the network. The answer is settable, so a test can make an attempt succeed, be
/// refused or time out without any real HTTP being involved, and can inspect exactly what was sent.
/// </summary>
internal sealed class FakeDeliverySender : IDeliverySender
{
    public Func<DeliveryRequest, DeliveryResult> Answer { get; set; } = _ => DeliveryResult.Accepted(200);

    /// <summary>Every attempt the worker made, in order.</summary>
    public List<DeliveryRequest> Sent { get; } = [];

    public Task<DeliveryResult> SendAsync(DeliveryRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Sent.Add(request);

        return Task.FromResult(Answer(request));
    }
}
