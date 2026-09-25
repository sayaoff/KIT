using KIT.Core.Abstractions;

namespace KIT.Infrastructure.Windows;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

