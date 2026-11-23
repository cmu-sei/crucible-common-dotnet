// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.ComponentModel;
using Microsoft.Extensions.Caching.Hybrid;
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
/// Built on <see cref="HybridCache"/>. With only its default in-memory level, the cache is per process, and
/// <see cref="Invalidate"/> and <see cref="InvalidateAll"/> only reach the pod they run on; on the others a change
/// takes effect when the entry expires, so <see cref="PermissionClaimsCacheOptions.Expiration"/> bounds how stale
/// another pod's claims can be. Registering an <c>IDistributedCache</c> (or a HybridCache implementation with a
/// backplane) shares entries between pods without changing the apps.
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
    /// Builds the claims from the database. Runs at most once at a time per key, shared by every caller waiting on it.
    /// It runs on the first caller's request, but if that caller stops waiting while others still do, it carries on
    /// after the first request has ended, so it must not use that request's scoped services: create its own scope.
    /// <para>
    /// Do not pass the calling request's cancellation token into the load: other requests share its result, and an
    /// aborted request would fail them all.
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
    /// Drops every entry for the user, whatever its fingerprint. A load already in flight is not kept once it
    /// finishes, so the next caller loads again. Callers already waiting on that load still receive it. Call this
    /// after the change has been committed.
    /// </summary>
    ValueTask Invalidate(Guid userId);

    /// <summary>Drops every entry, for a change that reaches users the caller cannot list (a role's permissions).</summary>
    ValueTask InvalidateAll();
}

/// <summary>Settings for <see cref="PermissionClaimsCache"/>.</summary>
public sealed class PermissionClaimsCacheOptions
{
    /// <summary>The default for <see cref="Expiration"/>: five minutes.</summary>
    public static readonly TimeSpan DefaultExpiration = TimeSpan.FromSeconds(300);

    /// <summary>
    /// How long a loaded entry is kept.
    /// <see cref="Timeout.InfiniteTimeSpan"/> keeps entries until they are invalidated (as does any very large value,
    /// which is capped at ten years). Zero or any other negative value keeps nothing, but concurrent misses
    /// still share one load.
    /// </summary>
    public TimeSpan Expiration { get; set; } = DefaultExpiration;
}

/// <inheritdoc cref="IPermissionClaimsCache"/>
public sealed class PermissionClaimsCache : IPermissionClaimsCache
{
    private const string AllTag = "permission-claims";
    private static readonly TimeSpan MaxExpiration = TimeSpan.FromDays(3650);

    private readonly HybridCache _cache;
    private readonly HybridCacheEntryOptions _entryOptions;

    // bumped by every invalidation, so a load that started before one is not kept: tag removal only covers entries
    // created before it, and whether a load already in flight counts is not specified
    private long _generation;

    /// <summary>A cache over the given <see cref="HybridCache"/>, with the given settings or the defaults.</summary>
    /// <param name="cache">The hybrid cache entries are stored in.</param>
    /// <param name="options">Settings; read once, here. Defaults to a <see cref="PermissionClaimsCacheOptions"/> as constructed.</param>
    /// <remarks>
    /// Usually registered as a singleton with
    /// <see cref="PermissionClaimsCacheServiceCollectionExtensions.AddPermissionClaimsCache"/>: one cache per process,
    /// shared by every request.
    /// </remarks>
    public PermissionClaimsCache(HybridCache cache, PermissionClaimsCacheOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        _cache = cache;
        var expiration = (options ?? new PermissionClaimsCacheOptions()).Expiration;

        // InfiniteTimeSpan is -1 ms, so it has to be told apart from the negative values that mean "keep nothing"
        _entryOptions = expiration == Timeout.InfiniteTimeSpan
            ? new HybridCacheEntryOptions { Expiration = MaxExpiration, LocalCacheExpiration = MaxExpiration }
            : expiration <= TimeSpan.Zero
                ? new HybridCacheEntryOptions
                {
                    // concurrent misses still share one load, but nothing is written
                    Flags = HybridCacheEntryFlags.DisableLocalCacheWrite | HybridCacheEntryFlags.DisableDistributedCacheWrite
                }
                : new HybridCacheEntryOptions
                {
                    Expiration = expiration < MaxExpiration ? expiration : MaxExpiration,
                    LocalCacheExpiration = expiration < MaxExpiration ? expiration : MaxExpiration
                };
    }

    public async Task<IReadOnlyList<Claim>> GetOrLoadAsync(
        Guid userId,
        string fingerprint,
        Func<Task<PermissionClaimsLoadResult>> load,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(load);
        var key = $"{userId:N}:{fingerprint ?? string.Empty}";
        var generation = Volatile.Read(ref _generation);
        var keep = true;

        var cached = await _cache.GetOrCreateAsync(
            key,
            async _ =>
            {
                var result = await load().ConfigureAwait(false);
                keep = result.Cacheable;
                return new CachedClaims((result.Claims ?? []).Select(c => new CachedClaim(c.Type, c.Value)).ToArray());
            },
            _entryOptions,
            [AllTag, UserTag(userId)],
            cancellationToken).ConfigureAwait(false);

        // not cacheable, or invalidated while it loaded: drop it so the next caller loads again
        if (!keep || Volatile.Read(ref _generation) != generation)
        {
            await _cache.RemoveAsync(key, CancellationToken.None).ConfigureAwait(false);
        }

        return cached.Claims.Select(c => new Claim(c.Type, c.Value)).ToArray();
    }

    public async ValueTask Invalidate(Guid userId)
    {
        Interlocked.Increment(ref _generation);
        await _cache.RemoveByTagAsync(UserTag(userId)).ConfigureAwait(false);
    }

    public async ValueTask InvalidateAll()
    {
        Interlocked.Increment(ref _generation);
        await _cache.RemoveByTagAsync(AllTag).ConfigureAwait(false);
    }

    // the key is the user id and the fingerprint, so a tag per user reaches every fingerprint of theirs
    private static string UserTag(Guid userId) => $"user:{userId:N}";

    // Claim does not round-trip through System.Text.Json, and the loaders only use the type and value
    [ImmutableObject(true)]
    internal sealed record CachedClaims(CachedClaim[] Claims);

    internal sealed record CachedClaim(string Type, string Value);
}
