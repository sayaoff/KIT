namespace KIT.Core.Models;

public enum TrackingState
{
    NotConfigured,
    Watching,
    GameRunning,
    Stopped
}

public sealed record TrackingStatus(
    TrackingState State,
    string Message,
    GameSession? Session = null);

