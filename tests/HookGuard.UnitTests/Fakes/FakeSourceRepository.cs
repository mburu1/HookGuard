using HookGuard.Application.Abstractions;
using HookGuard.Domain.Sources;

namespace HookGuard.UnitTests.Fakes;

/// <summary>A fixed set of configured sources, looked up by id as the real repository would.</summary>
internal sealed class FakeSourceRepository(params Source[] sources) : ISourceRepository
{
    public Task<Source?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(sources.FirstOrDefault(source => source.Id == id));
}
