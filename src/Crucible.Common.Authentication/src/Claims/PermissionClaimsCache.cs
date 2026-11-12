// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Collections.Concurrent;
using System.Security.Claims;

namespace Crucible.Common.Authentication.Claims;

/// <summary>
/// What an app's permission loader returns: the claims it built from the database, and whether they may be kept.
/// </summary>
/// <param name="Claims">The permission claims to add to the principal.</param>
/// <param name="Cacheable">
/// False when the result must not be kept, for example because the user has no row yet and the claims are empty
/// only for that reason. Callers that were waiting on the same load still receive it.
/// </param>
public readonly record struct PermissionClaimsLoadResult(IReadOnlyList<Claim> Claims, bool Cacheable = true);

/// <summary>
/// Caches the permission claims an app builds from its own database, per user and per fingerprint of the token
/// claims those permissions depend on (see <see cref="PermissionClaimsFingerprint"/>), rather than per token. A
/// renewed token, or another browser tab's token, with the same roles and groups reuses the entry.
/// </summary>
/// <remarks>
/// <para>
/// Concurrent misses for one key share a single load: the first caller runs it and the rest await its result.
/// A load that throws is not kept, so the next caller loads again.
/// </para>
/// <para>
/// The cache is in memory and per process. <see cref="Invalidate"/> and <see cref="InvalidateAll"/> only reach the
/// pod they run on; on the others a change takes effect when the entry expires, so
/// <see cref="PermissionClaimsCacheOptions.Expiration"/> bounds how stale another pod's claims can be.
/// </para>
/// </remarks>
public interface IPermissionClaimsCache
{
    /// <summary>
    /// The cached claims for the user and fingerprint, or the result of <paramref name="load"/> when there are none.
    /// </summary>
    /// <param name="userId">The user the claims belong to.</param>
    /// <param name="fingerprint">
    /// A stable value for every token claim the loader reads, so tokens that would load different claims never share
    /// an entry. Use <see cref="PermissionClaimsFingerprint.Create(IEnumerable{string}?[])"/>. Never the token id.
    /// </param>
    /// <param name="load">
    /// Builds the claims from the database. Runs at most once at a time per key, on the calling (first) request, and
    /// that caller always waits for it to finish, so it may use the caller's scoped services.
    /// <para>
    /// Do not pass the calling request's cancellation token into the load: other requests share its result, and an
    /// aborted request would fail them all. (If a load is cancelled anyway, the callers waiting on it load again
    /// rather than rethrow another request's cancellation.)
    /// </para>
    /// <para>
    /// The load must not ask this cache for the same key, directly or from anything it triggers, such as an entity
    /// event handler its <c>SaveChanges</c> fires: that call waits for the load it is part of, and never returns.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Stops a caller waiting on another caller's load; the load itself carries on.</param>
    Task<IReadOnlyList<Claim>> GetOrLoadAsync(
        Guid userId,
        string fingerprint,
        Func<Task<PermissionClaimsLoadResult>> load,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every entry for the user, whatever its fingerprint, and detaches any load in flight for them so the next
    /// caller loads again. Callers already waiting on that load still receive it.
    /// </summary>
    void Invalidate(Guid userId);

    /// <summary>Drops every entry, for a change that reaches users the caller cannot list (a role's permissions).</summary>
    void InvalidateAll();
}

/// <summary>Settings for <see cref="PermissionClaimsCache"/>.</summary>
public sealed class PermissionClaimsCacheOptions
{
    /// <summary>The default for <see cref="Expiration"/>: five minutes.</summary>
    public static readonly TimeSpan DefaultExpiration = TimeSpan.FromSeconds(300);

    /// <summary>
    /// How long a loaded entry is kept, counted from when its load finished. Zero or less keeps nothing, but
    /// concurrent misses still share one load.
    /// </summary>
    public TimeSpan Expiration { get; set; } = DefaultExpiration;
}

/// <inheritdoc cref="IPermissionClaimsCache"/>
public sealed class PermissionClaimsCache : IPermissionClaimsCache
{
    private readonly ConcurrentDictionary<CacheKey, Entry> _entries = new();
    private readonly TimeSpan _expiration;
    private readonly TimeProvider _time;
    private readonly TimeSpan _sweepInterval;
    private long _lastSweepTimestamp;

    public PermissionClaimsCache(PermissionClaimsCacheOptions? options = null, TimeProvider? timeProvider = null)
    {
        _expiration = (options ?? new PermissionClaimsCacheOptions()).Expiration;
        _time = timeProvider ?? TimeProvider.System;
        _sweepInterval = _expiration > TimeSpan.FromMinutes(1) ? _expiration : TimeSpan.FromMinutes(1);
        _lastSweepTimestamp = _time.GetTimestamp();
    }

    /// <summary>The number of entries held, loaded or loading. For diagnostics and tests.</summary>
    public int Count => _entries.Count;

    // Expired entries are dropped when they are next asked for; the sweep removes the ones nobody asks for again
    // (a user who left, or a fingerprint a user no longer has), so the dictionary cannot grow without bound.

    public async Task<IReadOnlyList<Claim>> GetOrLoadAsync(
        Guid userId,
        string fingerprint,
        Func<Task<PermissionClaimsLoadResult>> load,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(load);
        var key = new CacheKey(userId, fingerprint ?? string.Empty);

        while (true)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                var task = existing.Completion.Task;

                if (!task.IsCompleted)
                {
                    // another caller is loading this key: share its result
                    try
                    {
                        return (await task.WaitAsync(cancellationToken).ConfigureAwait(false)).Claims;
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        // the owner's load was cancelled, not this caller: its entry is gone, so load again
                        continue;
                    }
                }

                if (task.IsCompletedSuccessfully && IsFresh(existing))
                {
                    return task.Result.Claims;
                }

                // expired (or, defensively, a failed load that was not removed): only remove this exact entry, in
                // case another caller has already replaced it
                _entries.TryRemove(new KeyValuePair<CacheKey, Entry>(key, existing));
                continue;
            }

            var entry = new Entry();

            if (!_entries.TryAdd(key, entry))
            {
                continue;
            }

            SweepIfDue();
            return await LoadAsync(key, entry, load).ConfigureAwait(false);
        }
    }

    public void Invalidate(Guid userId)
    {
        foreach (var pair in _entries)
        {
            if (pair.Key.UserId == userId)
            {
                _entries.TryRemove(pair);
            }
        }
    }

    public void InvalidateAll() => _entries.Clear();

    private async Task<IReadOnlyList<Claim>> LoadAsync(CacheKey key, Entry entry, Func<Task<PermissionClaimsLoadResult>> load)
    {
        PermissionClaimsLoadResult result;

        try
        {
            result = await load().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // not kept: the next caller loads again
            _entries.TryRemove(new KeyValuePair<CacheKey, Entry>(key, entry));
            entry.Completion.SetException(ex);
            // waiting callers rethrow it; mark it observed so a load nobody waited on is not reported as unobserved
            _ = entry.Completion.Task.Exception;
            throw;
        }

        result = result with { Claims = result.Claims?.ToArray() ?? [] };

        if (result.Cacheable && _expiration > TimeSpan.Zero)
        {
            // set before the result is published, so nobody sees a completed entry without its load time
            entry.LoadedAtTimestamp = _time.GetTimestamp();
            entry.Kept = true;
        }
        else
        {
            _entries.TryRemove(new KeyValuePair<CacheKey, Entry>(key, entry));
        }

        entry.Completion.SetResult(result);
        return result.Claims;
    }

    private void SweepIfDue()
    {
        var last = Volatile.Read(ref _lastSweepTimestamp);

        if (_time.GetElapsedTime(last) < _sweepInterval ||
            Interlocked.CompareExchange(ref _lastSweepTimestamp, _time.GetTimestamp(), last) != last)
        {
            return;
        }

        foreach (var pair in _entries)
        {
            var task = pair.Value.Completion.Task;

            if (task.IsCompleted && !IsFresh(pair.Value))
            {
                _entries.TryRemove(pair);
            }
        }
    }

    // elapsed time on the monotonic timestamp, so a wall clock change cannot expire or extend entries; and no
    // expiry instant is ever computed, so no expiration is large enough to overflow
    private bool IsFresh(Entry entry) =>
        entry.Kept && _time.GetElapsedTime(entry.LoadedAtTimestamp) < _expiration;

    private readonly record struct CacheKey(Guid UserId, string Fingerprint);

    private sealed class Entry
    {
        public readonly TaskCompletionSource<PermissionClaimsLoadResult> Completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        // set when a cacheable load finishes; written before Kept, which is volatile, so a reader that sees Kept
        // sees the load time too
        public long LoadedAtTimestamp;

        // false while loading, and for a load that was not kept, so neither reads as fresh
        public volatile bool Kept;
    }
}
