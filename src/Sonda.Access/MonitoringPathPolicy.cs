using System.Text.RegularExpressions;
using Sonda.Application.Acquisition;

namespace Sonda.Access;

/// <summary>Configuration validation only. Never probes a path or contacts a UNC authority.</summary>
public sealed class MonitoringPathPolicy
{
    private readonly string[] roots;
    public MonitoringPathPolicy(IEnumerable<string> allowedRoots) => roots = allowedRoots.Select(Canonical).ToArray();

    public string ValidatePath(string path)
    {
        var normalized = Canonical(path);
        if (!normalized.StartsWith("\\\\", StringComparison.Ordinal) && OperatingSystem.IsWindows() && new DriveInfo(normalized[..3]).DriveType == DriveType.Network)
            throw new AccessFault(422, "mapped_drive_not_supported");
        if (!roots.Any(root => normalized.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            throw new AccessFault(422, "monitoring_path_not_allowed");
        return normalized;
    }

    public SourceConfiguration Validate(SourceConfiguration source)
    {
        if (source is null || source.Include is null || source.Exclude is null || source.Include.Concat(source.Exclude).Any(x => x is null)) throw new AccessFault(422, "invalid_source_configuration");
        var result = source with
        {
            Root = ValidatePath(source.Root),
            ProducerManifestPath = string.IsNullOrEmpty(source.ProducerManifestPath) ? "" : ValidatePath(source.ProducerManifestPath),
            RotationManifestPath = string.IsNullOrEmpty(source.RotationManifestPath) ? "" : ValidatePath(source.RotationManifestPath)
        };
        if (result.Include.Concat(result.Exclude).Any(p => p.Contains('/') || p.Contains('\\') || p.Any(char.IsControl)))
            throw new AccessFault(422, "invalid_include_pattern");
        try { result.Validate(); }
        catch (ArgumentException) { throw new AccessFault(422, "invalid_source_configuration"); }
        return result;
    }

    private static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 2048 || path.Any(char.IsControl) ||
            path.Contains('/') || path.StartsWith("\\\\?", StringComparison.Ordinal) || path.StartsWith("\\\\.", StringComparison.Ordinal))
            throw new AccessFault(422, "invalid_monitoring_path");
        var unc = path.StartsWith("\\\\", StringComparison.Ordinal);
        if (!unc && !Regex.IsMatch(path, @"^[A-Za-z]:\\")) throw new AccessFault(422, "absolute_monitoring_path_required");
        var tail = unc ? path[2..] : path[3..];
        var segments = tail.TrimEnd('\\').Split('\\');
        if (unc && segments.Length < 2) throw new AccessFault(422, "unc_share_required");
        foreach (var part in segments)
        {
            if ((!unc && segments.Length == 1 && part.Length == 0)) continue;
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new AccessFault(422, "invalid_monitoring_path");
        }
        return !unc && path.Length == 3 ? path : path.TrimEnd('\\');
    }
}
