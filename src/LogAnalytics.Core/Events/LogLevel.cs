namespace LogAnalytics.Core.Events;

/// <summary>Serilog's levels, stored as their number so "at least Warning" is a simple comparison.</summary>
public enum LogLevel : short
{
    Verbose = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Fatal = 5,
}
