# Crucible.Common.Authentication

Some handy helpers for authentication tasks.

## Permission claims cache

`IPermissionClaimsCache` caches the permission claims an app builds from its own database during claims
transformation, so a page load does not rebuild them on every request.

- **Keyed by user and token inputs, not by token.** An entry is keyed by the user id and a fingerprint of the
  token claims the app's loader reads (the roles and groups from the identity provider, when the app uses them).
  A renewed token, or another tab's token, with the same roles and groups reuses the entry. Build the fingerprint
  with `PermissionClaimsFingerprint.Create(...)`, passing one set per input; never include the token id (`jti`).
- **One load per key at a time.** Built on `HybridCache` (`Microsoft.Extensions.Caching.Hybrid`): concurrent misses
  for a key share one load, and a load that throws is not kept. `AddPermissionClaimsCache` adds `HybridCache` if
  the app has not registered one.
- **Expiry.** `PermissionClaimsCacheOptions.Expiration` (default 300 seconds).
  `Timeout.InfiniteTimeSpan` keeps entries until invalidated; zero or negative keeps nothing (concurrent misses
  still share one load).
- **Invalidation.** `await Invalidate(userId)` drops every entry for a user; `await InvalidateAll()` drops
  everything. Both are tag removals, and a load already in flight when one runs is not kept. Call them after the
  membership or role change has been committed.
- **In memory, per process by default.** Invalidation only reaches the pod it runs on. On other pods a change takes
  effect when the entry expires, so `Expiration` bounds how stale another pod's claims can be. Registering an
  `IDistributedCache` (or a `HybridCache` implementation with a backplane, such as FusionCache) shares entries
  between pods without changing the apps.

The loader runs on the request that missed. If that request stops waiting while others still wait on the same load,
the load carries on after the request has ended, so the loader should create its own scope instead of using the
request's scoped services. The claims the token itself carries are not cached; add the cached claims to each
request's principal.

- **Don't pass the request's cancellation token into the loader.** Other requests share its result.
- **Don't re-enter.** A loader that asks the cache for the same key, directly or through something it triggers (an
  entity event handler fired by its `SaveChanges`, for example), waits on itself and never returns.

```csharp
services.AddPermissionClaimsCache(options => options.Expiration = TimeSpan.FromSeconds(300));
// or bind it like any other options: services.Configure<PermissionClaimsCacheOptions>(configuration.GetSection(...))

// in the app's claims service
var fingerprint = PermissionClaimsFingerprint.Create(tokenRoles, tokenGroups);
var claims = await _cache.GetOrLoadAsync(userId, fingerprint, async () =>
{
    var user = await ValidateUser(...);
    return user is null
        ? new PermissionClaimsLoadResult([], Cacheable: false)
        : new PermissionClaimsLoadResult(await GetPermissionClaims(...));
});
identity.AddClaims(claims);

// when a membership changes
await _cache.Invalidate(userId);
```
