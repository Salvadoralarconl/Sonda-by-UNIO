using Sonda.Application.Simulation;
using Sonda.Domain.Processing;
using Sonda.Domain.Metrics;
using Sonda.Domain.Incidents;

namespace Sonda.Application.Persistence;

public sealed record ProcessingScope(string TeamId, Guid SessionId, string ApplicationId);
public sealed record ProcessingRequest(ProcessingScope Scope, string ProfileId, Guid RequestId,
    string SourceKey, string Generation, long SourceOrdinal, int Line, long Sequence,
    string Raw, DateTimeOffset ProcessedAt, DateOnly? SampleDate);
public sealed record ProcessingReceipt(Guid Id, int Version, EntryTrace Trace, bool Replayed);
public sealed record DurableState(InterpreterState Interpreter, long IdCounter, bool Blocked);
public interface IProcessingStore
{
    Task<ProcessingReceipt> ProcessAsync(ProcessingRequest request, CancellationToken cancellationToken = default);
    Task<DurableState> ReadStateAsync(ProcessingScope scope, string profileId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RunMetricFact>> ReadMetricFactsAsync(ProcessingScope scope, CancellationToken cancellationToken = default);
    Task<ApplicationHealth?> ReadHealthAsync(ProcessingScope scope, CancellationToken cancellationToken = default);
}
public sealed class PersistenceConflict(string message) : Exception(message);
public enum PersistenceBoundary { AfterEvidence, AfterRuns, AfterFacts, BeforeCommit, AfterCommit }
