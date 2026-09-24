using Sonda.Application.Processing;
namespace Sonda.Application.Persistence;

public interface IPolicyProcessingStore
{
    Task<PolicyReceipt> ExecuteAsync(ProcessingScope scope, string profileId, PolicyCommand command, CancellationToken ct = default);
    Task<PolicySessionState> ReadAsync(ProcessingScope scope, string profileId, CancellationToken ct = default);
}
