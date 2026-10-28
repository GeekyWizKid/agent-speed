using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AgentSpeed.Core.TaskParsers;

/// <summary>
/// The pattern instance a thread matches with. A <see cref="Regex"/> keeps one cached runner that every match swaps out
/// and back atomically, so several threads matching the same patterns line by line contend on it. A thread that opted
/// in (<see cref="UseOwnCopies"/>: the history workers of a large analysis; <see cref="Borrow"/>: the readers of a large
/// startup backlog) matches with its own compiled instance of the same pattern and options — the same engine and results
/// — and every other thread with the shared source-generated one.
/// </summary>
internal static class ThreadRegex
{
    private static readonly ConcurrentBag<Dictionary<Regex, Regex>> Pool = [];

    [ThreadStatic]
    private static Dictionary<Regex, Regex>? t_copies;

    /// <summary>The current thread compiles and keeps its own copies from now on (they go with the thread).</summary>
    public static void UseOwnCopies() => t_copies ??= NewSet();

    /// <summary>
    /// Lends the current thread a set of copies until the lease is disposed (pool threads); sets go back to a pool that
    /// <see cref="ReleasePool"/> empties. Nothing when the thread already has copies.
    /// </summary>
    public static Lease Borrow()
    {
        if (t_copies is not null)
        {
            return default;
        }

        var set = Pool.TryTake(out var pooled) ? pooled : NewSet();
        t_copies = set;
        return new Lease(set);
    }

    /// <summary>Drops every pooled set (the burst that needed them ended).</summary>
    public static void ReleasePool() => Pool.Clear();

    public static Regex Of(Regex shared)
    {
        if (t_copies is not { } copies)
        {
            return shared;
        }

        if (!copies.TryGetValue(shared, out var copy))
        {
            copy = new Regex(shared.ToString(), shared.Options | RegexOptions.Compiled, shared.MatchTimeout);
            copies[shared] = copy;
        }

        return copy;
    }

    private static Dictionary<Regex, Regex> NewSet() => new(ReferenceEqualityComparer.Instance);

    /// <summary>A borrowed set: disposing returns it to the pool.</summary>
    public readonly struct Lease(Dictionary<Regex, Regex>? set) : IDisposable
    {
        public void Dispose()
        {
            if (set is not null)
            {
                t_copies = null;
                Pool.Add(set);
            }
        }
    }
}
