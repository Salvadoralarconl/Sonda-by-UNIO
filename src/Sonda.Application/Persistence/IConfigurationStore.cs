using Sonda.Application.Simulation;
using Sonda.Domain.Profiles;

namespace Sonda.Application.Persistence;

public interface IConfigurationStore
{
    Task<Guid> CreateSessionAsync(SimulationRequest request, string kind = "DurabilityHarness", CancellationToken ct = default);
    Task<long> EditDraftAsync(Profile profile, long expectedRevision, Guid commandId, CancellationToken ct = default);
    Task PublishAsync(SimulationRequest request, long expectedRevision, Guid commandId, CancellationToken ct = default);
    Task<long> EditSourceAsync(string teamId, string profileId, string sourceKey, string configuration, long expectedRevision, Guid commandId, CancellationToken ct = default);
    Task ActivateAsync(ProcessingScope scope, string profileId, int version, long expectedRevision, int expectedVersion, Guid commandId, CancellationToken ct = default);
}
