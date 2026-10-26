using System.Security.Cryptography;

namespace LilithRuntime;

internal static class BoundedComponentFileHashing
{
    // Bound simultaneous disk reads and hash buffers on both SSDs and HDDs.
    // Callers still verify components in their existing order; this does not
    // cache trust or authorize publishing any partially verified component.
    internal const int MaximumConcurrency = 4;

    internal sealed record Input(string Path, long? ExpectedLength = null);
    internal sealed record Result(long Length, string Sha256);

    internal static async Task<Result[]> HashAsync(
        IReadOnlyList<Input> inputs,
        CancellationToken cancellationToken,
        int concurrency = MaximumConcurrency,
        Func<string, Stream>? openRead = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (concurrency is < 1 or > MaximumConcurrency)
            throw new ArgumentOutOfRangeException(nameof(concurrency));
        cancellationToken.ThrowIfCancellationRequested();
        var results = new Result[inputs.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, inputs.Count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = concurrency,
                CancellationToken = cancellationToken
            },
            async (index, workerCancellation) =>
            {
                var input = inputs[index];
                await using var stream = openRead is null
                    ? OpenRegularFile(input.Path)
                    : openRead(input.Path);
                var length = stream.Length;
                if (input.ExpectedLength is { } expected && length != expected)
                    throw new IOException("The component file length does not match.");
                var sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, workerCancellation)
                        .ConfigureAwait(false));
                if (stream.Length != length)
                    throw new IOException("The component file length changed while hashing.");
                results[index] = new Result(length, sha256);
            }).ConfigureAwait(false);
        // ForEachAsync waits for every active worker, including disposal, on
        // cancellation or failure. Results retain input order, never completion
        // order, so the caller's canonical closure bytes remain unchanged.
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static Stream OpenRegularFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new IOException("The component hash target must be a regular file.");
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }
}
