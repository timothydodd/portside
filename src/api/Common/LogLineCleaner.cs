using System.Text.RegularExpressions;

namespace PortsideApi.Common;

/// <summary>
/// Strips redundant prefixes from log lines so the viewer doesn't show information
/// already broken out into its own columns (timestamp, level).
///
/// Handles the common cases:
///   "13:24:54 info: Microsoft.Hosting...."          -> "Microsoft.Hosting...."
///   "[INFO] starting up"                             -> "starting up"
///   "WARN: cache miss"                               -> "cache miss"
///   "<6>2026-05-04T19:13:32Z some message"           -> "some message"
///   ANSI color escapes are stripped wholesale.
/// </summary>
public static partial class LogLineCleaner
{
    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled)]
    private static partial Regex AnsiRegex();

    // Leading time-of-day "13:24:54" or "1:24:54" (with optional fractional seconds).
    [GeneratedRegex(@"^\d{1,2}:\d{2}:\d{2}(?:\.\d+)?\s+", RegexOptions.Compiled)]
    private static partial Regex LeadingTimeRegex();

    // Leading level prefix: "info:", "[INFO]", "WARN:", optionally bracketed.
    [GeneratedRegex(
        @"^\[?(?:trace|trc|debug|dbg|dbug|info|inf|information|warn|wrn|warning|error|err|fail|fatal|critical|crit|verbose)\]?\s*[:|-]?\s+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex LeadingLevelRegex();

    // Case-insensitive marker checks. Callers scan every log line, so these avoid
    // the per-line string copy a ToUpperInvariant() approach would allocate.
    public static bool HasErrorMarker(string line) =>
        line.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
        || line.Contains("EXCEPTION", StringComparison.OrdinalIgnoreCase)
        || line.Contains("FATAL", StringComparison.OrdinalIgnoreCase);

    public static bool HasWarningMarker(string line) =>
        line.Contains("WARN", StringComparison.OrdinalIgnoreCase);

    public static string DetectLevel(string line)
    {
        if (HasErrorMarker(line)) return "Error";
        if (HasWarningMarker(line)) return "Warning";
        if (line.Contains("DEBUG", StringComparison.OrdinalIgnoreCase)) return "Debug";
        if (line.Contains("TRACE", StringComparison.OrdinalIgnoreCase)) return "Trace";
        return "Information";
    }

    public static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        var s = AnsiRegex().Replace(raw, string.Empty);
        // Apply each prefix-strip up to twice in case both a time AND level lead.
        for (var i = 0; i < 2; i++)
        {
            var before = s;
            s = LeadingTimeRegex().Replace(s, string.Empty, 1);
            s = LeadingLevelRegex().Replace(s, string.Empty, 1);
            if (ReferenceEquals(s, before) || s == before) break;
        }
        return s;
    }
}
