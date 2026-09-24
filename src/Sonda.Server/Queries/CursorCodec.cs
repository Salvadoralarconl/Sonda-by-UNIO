using Microsoft.AspNetCore.DataProtection;
using Sonda.Access;
using Sonda.Application.Simulation;

namespace Sonda.Server.Queries;

public sealed record CursorPosition(string Team, string FilterHash, DateTimeOffset ExpiresAt, string Key, DateTimeOffset? Timestamp = null, Guid? Session = null);
public sealed class CursorCodec(IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("SONDA.QueryCursor.v1");
    public string Encode(string team, object filter, string key, DateTimeOffset? timestamp = null, Guid? session = null) =>
        protector.Protect(SimulationJson.Serialize(new CursorPosition(team, SimulationJson.Hash(filter), clock.GetUtcNow().AddMinutes(15), key, timestamp, session)));
    public CursorPosition? Decode(string? text, string team, object filter)
    {
        if (text is null) return null;
        try
        {
            if (text.Length > 4096) throw new ArgumentException();
            var value = SimulationJson.Deserialize<CursorPosition>(protector.Unprotect(text));
            if (value.Team != team || value.FilterHash != SimulationJson.Hash(filter) || value.ExpiresAt <= clock.GetUtcNow()) throw new ArgumentException();
            return value;
        }
        catch (Exception e) when (e is ArgumentException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
        { throw new AccessFault(400, "invalid_cursor"); }
    }
    public static int Size(int? size)
    {
        var result = size ?? 50;
        if (result is < 1 or > 200) throw new AccessFault(422, "page_size_out_of_range");
        return result;
    }
}
