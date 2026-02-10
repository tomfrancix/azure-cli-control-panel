namespace AzureCliControlPanel.Core.Util;

public static class Retry
{
    public static async Task<T> WithBackoffAsync<T>(
        Func<CancellationToken, Task<T>> action,
        int maxAttempts,
        Func<T, bool> isSuccess,
        TimeSpan initialDelay,
        CancellationToken cancellationToken)
    {
        if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        var delay = initialDelay;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action(cancellationToken).ConfigureAwait(false);
            if (isSuccess(result)) return result;

            if (attempt == maxAttempts) return result;

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2000));
        }

        throw new InvalidOperationException("Unreachable.");
    }
}
