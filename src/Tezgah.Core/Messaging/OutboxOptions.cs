namespace Tezgah.Core.Messaging;

public sealed class OutboxOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failures a message is left in the table for a human to look at.</summary>
    public int MaxAttempts { get; set; } = 10;
}
