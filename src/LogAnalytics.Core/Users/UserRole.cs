namespace LogAnalytics.Core.Users;

public enum UserRole
{
    /// <summary>Searches logs and reads statistics and alerts.</summary>
    Viewer,

    /// <summary>Also manages sources, alert rules and users.</summary>
    Admin,
}
