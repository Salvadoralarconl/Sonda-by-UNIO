namespace Sonda.Access;

// Constructed by validated server authentication, never bound from an HTTP body.
public sealed record Actor(Guid AccountId, string TeamId, string Role, long Revision);
public sealed class AccessFault(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public sealed record ProvisionedResource(string Id, long Revision, string State);
public sealed record CreateApplication(Guid OperationId, string Name, string Description = "");
public sealed record CreateProfile(Guid OperationId, string Name);
