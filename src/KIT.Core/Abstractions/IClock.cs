namespace KIT.Core.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

