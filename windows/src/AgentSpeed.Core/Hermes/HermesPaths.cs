using AgentSpeed.Core.IO;

namespace AgentSpeed.Core.Hermes;

/// <summary>Only the native usage log and numeric rotations, including named profiles.</summary>
internal static class HermesPaths
{
    public static string DefaultHome(string userHome)
    {
        if (Environment.GetEnvironmentVariable("HERMES_HOME") is { Length: > 0 } configured)
        {
            return configured;
        }

        var legacy = Path.Combine(userHome, ".hermes");
        if (!OperatingSystem.IsWindows()) return legacy;
        var localData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var current = Path.Combine(string.IsNullOrWhiteSpace(localData) ? Path.Combine(userHome, "AppData", "Local") : localData, "hermes");
        return Directory.Exists(current) || !Directory.Exists(legacy) ? current : legacy;
    }

    public static bool IsLogName(ReadOnlySpan<char> name)
    {
        if (name.SequenceEqual("agent.log")) return true;
        const string prefix = "agent.log.";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.Length == prefix.Length) return false;
        foreach (var character in name[prefix.Length..])
        {
            if (!char.IsAsciiDigit(character)) return false;
        }

        return true;
    }

    public static bool IsLogLayout(string root, string path)
    {
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return false;
            var parts = Path.GetRelativePath(root, path).Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1 && Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Equals("logs", StringComparison.OrdinalIgnoreCase)
                || parts.Length == 2 && parts[0] == "logs"
                || parts.Length == 4 && parts[0] == "profiles" && !parts[1].StartsWith('.') && parts[2] == "logs";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rotation reuses filenames, so history stores each physical log segment independently. This preserves already
    /// counted usage when the oldest rotation is replaced; a rename retains the same key.
    /// </summary>
    public static string? HistoryKey(string path)
    {
        try
        {
            using var handle = TranscriptTailer.Open(path);
            var identity = FileIdentity.From(handle) ?? new FileIdentity(File.GetCreationTimeUtc(handle).Ticks, 0, 0);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"hermes-log:{Path.GetDirectoryName(Path.GetFullPath(path))}#{identity.CreationTicks}:{identity.Volume}:{identity.FileIndex}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    public static List<DiscoveredFile> Enumerate(string root, int limit, CancellationToken cancellationToken)
    {
        var files = new List<DiscoveredFile>();
        var scanCap = limit == int.MaxValue ? int.MaxValue : 200_000;
        var scanned = 0;
        try
        {
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return files;
            var logs = Path.GetFileName(Path.TrimEndingDirectorySeparator(root)).Equals("logs", StringComparison.OrdinalIgnoreCase)
                ? root : Path.Combine(root, "logs");
            AddLogs(logs);
            var profiles = Path.Combine(root, "profiles");
            if (Directory.Exists(profiles) && (File.GetAttributes(profiles) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var profile in Directory.EnumerateDirectories(profiles))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scanned++ >= scanCap) break;
                    if ((File.GetAttributes(profile) & FileAttributes.ReparsePoint) == 0)
                    {
                        AddLogs(Path.Combine(profile, "logs"));
                    }
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return [.. files.OrderByDescending(file => file.LastWriteUtc).Take(Math.Max(0, limit))];

        void AddLogs(string directory)
        {
            try
            {
                if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
                foreach (var path in Directory.EnumerateFiles(directory, "agent.log*"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (scanned++ >= scanCap) break;
                    if (!TranscriptDiscovery.IsAcceptablePath(root, path, TranscriptNaming.HermesLog)) continue;
                    var info = new FileInfo(path);
                    files.Add(new DiscoveredFile(path, info.Length, info.LastWriteTimeUtc));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
