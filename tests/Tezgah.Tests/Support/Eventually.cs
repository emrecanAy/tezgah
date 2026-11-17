namespace Tezgah.Tests.Support;

/// <summary>Waits for something that happens asynchronously, such as an outbox delivery.</summary>
internal static class Eventually
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static async Task TrueAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Condition was still false after {Timeout.TotalSeconds:0} seconds.");
            await Task.Delay(100);
        }
    }

    /// <summary>Polls <paramref name="read"/> until the value satisfies <paramref name="done"/>, then returns it.</summary>
    public static async Task<T> ValueAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        await TrueAsync(async () =>
        {
            value = await read();
            return done(value);
        });
        return value;
    }
}
