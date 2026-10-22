using HookGuard.Application.Abstractions;
using HookGuard.Domain.Destinations;

namespace HookGuard.UnitTests.Fakes;

/// <summary>
/// A fixed set of configured destinations, filtered the way the real repository filters them: by
/// source and by whether they are still active.
/// </summary>
internal sealed class FakeDestinationRepository(params Destination[] destinations) : IDestinationRepository
{
    public Task<IReadOnlyList<Destination>> ListActiveAsync(Guid sourceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Destination>>(
        [
            .. destinations
                .Where(destination => destination.SourceId == sourceId && destination.IsActive)
                .OrderBy(destination => destination.Id),
        ]);
}
