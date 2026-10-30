namespace LogAnalytics.Core.Events;

public static class Levels
{
    private static readonly Dictionary<string, LogLevel> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Verbose"] = LogLevel.Verbose, ["Trace"] = LogLevel.Verbose, ["VRB"] = LogLevel.Verbose, ["TRC"] = LogLevel.Verbose,
        ["Debug"] = LogLevel.Debug, ["DBG"] = LogLevel.Debug,
        ["Information"] = LogLevel.Information, ["Info"] = LogLevel.Information, ["INF"] = LogLevel.Information,
        ["Warning"] = LogLevel.Warning, ["Warn"] = LogLevel.Warning, ["WRN"] = LogLevel.Warning,
        ["Error"] = LogLevel.Error, ["ERR"] = LogLevel.Error,
        ["Fatal"] = LogLevel.Fatal, ["Critical"] = LogLevel.Fatal, ["FTL"] = LogLevel.Fatal, ["CRT"] = LogLevel.Fatal,
    };

    /// <summary>Serilog and Microsoft.Extensions.Logging names and their short forms; a missing level is Information.</summary>
    public static LogLevel? Parse(string? name) =>
        string.IsNullOrEmpty(name) ? LogLevel.Information : Names.TryGetValue(name, out var level) ? level : null;
}
