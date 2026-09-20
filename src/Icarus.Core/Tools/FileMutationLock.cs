using Icarus.Core.Platform;

namespace Icarus.Core.Tools;

/// <summary>
/// Process-wide per-file mutation serialization (ICARUS-104). Tool calls in one
/// assistant turn run concurrently, and <c>write</c>/<c>edit</c> are
/// read-modify-write operations, so mutations of the same file must not
/// interleave. The lock is keyed on the canonical path, so two symlinks to one
/// file share a lock while distinct files still mutate in parallel.
/// </summary>
internal static class FileMutationLock
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, (SemaphoreSlim Semaphore, int Refs)> Locks = [];

    public static T Run<T>(string path, Func<T> action)
    {
        var key = Posix.RealPath(path) ?? Path.GetFullPath(path);

        SemaphoreSlim semaphore;
        lock (Gate)
        {
            if (Locks.TryGetValue(key, out var existing))
            {
                Locks[key] = (existing.Semaphore, existing.Refs + 1);
                semaphore = existing.Semaphore;
            }
            else
            {
                semaphore = new SemaphoreSlim(1, 1);
                Locks[key] = (semaphore, 1);
            }
        }

        semaphore.Wait();
        try
        {
            return action();
        }
        finally
        {
            semaphore.Release();
            lock (Gate)
            {
                if (Locks.TryGetValue(key, out var existing))
                {
                    if (existing.Refs <= 1)
                    {
                        Locks.Remove(key);
                    }
                    else
                    {
                        Locks[key] = (existing.Semaphore, existing.Refs - 1);
                    }
                }
            }
        }
    }
}
