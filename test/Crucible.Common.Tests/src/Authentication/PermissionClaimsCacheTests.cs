// Copyright 2026 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.Security.Claims;
using Crucible.Common.Authentication.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace Crucible.Common.Tests.Authentication;

public class PermissionClaimsCacheTests
{
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private readonly ManualTimeProvider _time = new();
    private int _loads;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PermissionClaimsCache NewCache(TimeSpan? expiration = null) =>
        new(new PermissionClaimsCacheOptions { Expiration = expiration ?? TimeSpan.FromMinutes(5) }, _time);

    private Func<Task<PermissionClaimsLoadResult>> Load(string value, bool cacheable = true) => () =>
    {
        Interlocked.Increment(ref _loads);
        return Task.FromResult(new PermissionClaimsLoadResult([new Claim("permission", value)], cacheable));
    };

    private static string Value(IReadOnlyList<Claim> claims) => Assert.Single(claims).Value;

    [Fact]
    public async Task A_second_call_for_the_same_user_and_fingerprint_is_answered_from_the_cache()
    {
        var cache = NewCache();

        await cache.GetOrLoadAsync(UserA, "fp", Load("first"), Ct);
        var claims = await cache.GetOrLoadAsync(UserA, "fp", Load("second"), Ct);

        Assert.Equal("first", Value(claims));
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task Parallel_misses_for_one_key_share_a_single_load()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        const int callers = 64;
        using var start = new Barrier(callers);

        async Task<PermissionClaimsLoadResult> SlowLoad()
        {
            Interlocked.Increment(ref _loads);
            await gate.Task;
            return new PermissionClaimsLoadResult([new Claim("permission", "shared")]);
        }

        var calls = Enumerable.Range(0, callers)
            // a thread each, released together, so the calls really do race for the key
            .Select(_ => Task.Factory.StartNew(() =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                return cache.GetOrLoadAsync(UserA, "fp", SlowLoad, TestContext.Current.CancellationToken);
            }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap())
            .ToArray();

        // every caller has either started the load or joined it before it is allowed to finish
        await WaitUntil(() => Volatile.Read(ref _loads) > 0);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        gate.SetResult();
        var results = await Task.WhenAll(calls);

        Assert.Equal(1, _loads);
        Assert.All(results, claims => Assert.Equal("shared", Value(claims)));
    }

    [Fact]
    public async Task Callers_arriving_while_a_load_is_running_wait_for_it_instead_of_loading()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PermissionClaimsLoadResult> SlowLoad()
        {
            Interlocked.Increment(ref _loads);
            await gate.Task;
            return new PermissionClaimsLoadResult([new Claim("permission", "shared")]);
        }

        var calls = Enumerable.Range(0, 20).Select(_ => cache.GetOrLoadAsync(UserA, "fp", SlowLoad, Ct)).ToArray();

        Assert.All(calls, call => Assert.False(call.IsCompleted));
        gate.SetResult();
        await Task.WhenAll(calls);
        Assert.Equal(1, _loads);
    }

    [Fact]
    public async Task An_entry_is_loaded_again_once_it_expires()
    {
        var cache = NewCache(TimeSpan.FromSeconds(300));
        await cache.GetOrLoadAsync(UserA, "fp", Load("first"), Ct);

        _time.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal("first", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("second"), Ct)));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("second", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("second"), Ct)));
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task A_zero_expiration_keeps_nothing()
    {
        var cache = NewCache(TimeSpan.Zero);

        await cache.GetOrLoadAsync(UserA, "fp", Load("first"), Ct);
        await cache.GetOrLoadAsync(UserA, "fp", Load("second"), Ct);

        Assert.Equal(2, _loads);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task Invalidating_a_user_drops_every_fingerprint_of_theirs_and_nobody_elses()
    {
        var cache = NewCache();
        await cache.GetOrLoadAsync(UserA, "fp-1", Load("a1"), Ct);
        await cache.GetOrLoadAsync(UserA, "fp-2", Load("a2"), Ct);
        await cache.GetOrLoadAsync(UserB, "fp-1", Load("b1"), Ct);

        cache.Invalidate(UserA);

        Assert.Equal("a1-new", Value(await cache.GetOrLoadAsync(UserA, "fp-1", Load("a1-new"), Ct)));
        Assert.Equal("a2-new", Value(await cache.GetOrLoadAsync(UserA, "fp-2", Load("a2-new"), Ct)));
        Assert.Equal("b1", Value(await cache.GetOrLoadAsync(UserB, "fp-1", Load("b1-new"), Ct)));
        Assert.Equal(5, _loads);
    }

    [Fact]
    public async Task Invalidating_everything_drops_every_user()
    {
        var cache = NewCache();
        await cache.GetOrLoadAsync(UserA, "fp", Load("a"), Ct);
        await cache.GetOrLoadAsync(UserB, "fp", Load("b"), Ct);

        cache.InvalidateAll();

        Assert.Equal("a-new", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("a-new"), Ct)));
        Assert.Equal("b-new", Value(await cache.GetOrLoadAsync(UserB, "fp", Load("b-new"), Ct)));
    }

    /// <summary>
    /// A load that read the database before a membership change must not be kept once the change has invalidated
    /// the user: the next caller loads again, and the stale load does not overwrite it.
    /// </summary>
    [Fact]
    public async Task Invalidating_during_a_load_keeps_that_loads_result_out_of_the_cache()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PermissionClaimsLoadResult> StaleLoad()
        {
            await gate.Task;
            return new PermissionClaimsLoadResult([new Claim("permission", "stale")]);
        }

        var inFlight = cache.GetOrLoadAsync(UserA, "fp", StaleLoad, Ct);
        cache.Invalidate(UserA);
        var fresh = await cache.GetOrLoadAsync(UserA, "fp", Load("fresh"), Ct);
        gate.SetResult();

        Assert.Equal("stale", Value(await inFlight));
        Assert.Equal("fresh", Value(fresh));
        Assert.Equal("fresh", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("later"), Ct)));
    }

    [Fact]
    public async Task A_failed_load_is_not_cached_and_every_waiting_caller_sees_the_failure()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<PermissionClaimsLoadResult> FailingLoad()
        {
            Interlocked.Increment(ref _loads);
            await gate.Task;
            throw new InvalidOperationException("database unavailable");
        }

        var first = cache.GetOrLoadAsync(UserA, "fp", FailingLoad, Ct);
        var joined = cache.GetOrLoadAsync(UserA, "fp", FailingLoad, Ct);
        gate.SetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => joined);
        Assert.Equal(1, _loads);
        Assert.Equal(0, cache.Count);

        Assert.Equal("recovered", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("recovered"), Ct)));
    }

    [Fact]
    public async Task A_result_marked_not_cacheable_is_shared_with_waiting_callers_but_not_kept()
    {
        var cache = NewCache();

        await cache.GetOrLoadAsync(UserA, "fp", Load("unknown user", cacheable: false), Ct);
        var claims = await cache.GetOrLoadAsync(UserA, "fp", Load("known user"), Ct);

        Assert.Equal("known user", Value(claims));
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task Different_fingerprints_for_one_user_are_separate_entries()
    {
        var cache = NewCache();
        var observer = PermissionClaimsFingerprint.Create(["observer"], []);
        var admin = PermissionClaimsFingerprint.Create(["administrator"], []);

        await cache.GetOrLoadAsync(UserA, observer, Load("observer"), Ct);
        var claims = await cache.GetOrLoadAsync(UserA, admin, Load("administrator"), Ct);

        Assert.Equal("administrator", Value(claims));
        Assert.Equal("observer", Value(await cache.GetOrLoadAsync(UserA, observer, Load("other"), Ct)));
        Assert.Equal(2, _loads);
    }

    [Fact]
    public async Task A_waiting_caller_can_stop_waiting_without_cancelling_the_load()
    {
        var cache = NewCache();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();

        async Task<PermissionClaimsLoadResult> SlowLoad()
        {
            await gate.Task;
            return new PermissionClaimsLoadResult([new Claim("permission", "loaded")]);
        }

        var owner = cache.GetOrLoadAsync(UserA, "fp", SlowLoad, Ct);
        var waiter = cache.GetOrLoadAsync(UserA, "fp", SlowLoad, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        gate.SetResult();
        Assert.Equal("loaded", Value(await owner));
        Assert.Equal("loaded", Value(await cache.GetOrLoadAsync(UserA, "fp", Load("again"), Ct)));
    }

    [Fact]
    public async Task Expired_entries_nobody_asks_for_again_are_swept()
    {
        var cache = NewCache(TimeSpan.FromMinutes(5));
        await cache.GetOrLoadAsync(UserA, "old", Load("a"), Ct);

        _time.Advance(TimeSpan.FromMinutes(6));
        await cache.GetOrLoadAsync(UserB, "new", Load("b"), Ct);

        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Fingerprints_ignore_order_duplicates_and_case_but_not_which_set_a_value_is_in()
    {
        Assert.Equal(
            PermissionClaimsFingerprint.Create(["Admin", "observer"], ["blue"]),
            PermissionClaimsFingerprint.Create(["observer", "admin", "ADMIN"], ["Blue"]));

        Assert.NotEqual(
            PermissionClaimsFingerprint.Create(["blue"], []),
            PermissionClaimsFingerprint.Create([], ["blue"]));

        Assert.NotEqual(
            PermissionClaimsFingerprint.Create(["a,b"]),
            PermissionClaimsFingerprint.Create(["a", "b"]));

        Assert.NotEqual(
            PermissionClaimsFingerprint.Create(ignoreCase: false, ["Admin"]),
            PermissionClaimsFingerprint.Create(ignoreCase: false, ["admin"]));

        Assert.Equal(PermissionClaimsFingerprint.Create([], null), PermissionClaimsFingerprint.Create(null, []));
    }

    [Fact]
    public void AddPermissionClaimsCache_registers_one_shared_cache()
    {
        using var provider = new ServiceCollection()
            .AddPermissionClaimsCache(options => options.Expiration = TimeSpan.FromSeconds(30))
            .BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IPermissionClaimsCache>(), provider.GetRequiredService<IPermissionClaimsCache>());
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "timed out");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
