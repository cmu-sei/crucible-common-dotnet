// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Crucible.Common.EntityEvents.Abstractions;

/// <summary>
/// Abstract base class for DbContext classes that support entity event publishing.
/// Handles the SaveChanges override pattern - consuming apps just implement <see cref="PublishEventsAsync"/>.
/// </summary>
public abstract class EventPublishingDbContext : DbContext, IEventPublishingDbContext
{
    /// <inheritdoc/>
    public IServiceProvider? ServiceProvider { get; set; }

    /// <inheritdoc/>
    public List<IEntityEvent> EntityEvents { get; } = [];

    /// <inheritdoc/>
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];

    /// <summary>
    /// Creates a new EventPublishingDbContext with the specified options.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    protected EventPublishingDbContext(DbContextOptions options) : base(options) { }

    /// <inheritdoc/>
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    /// <inheritdoc/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        PublishEvents();
        ClearTrackedState();
        return result;
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    /// <inheritdoc/>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        await PublishEventsAsync(cancellationToken);
        ClearTrackedState();
        return result;
    }

    /// <summary>
    /// Clears the tracked entity events and entries after publishing.
    /// </summary>
    private void ClearTrackedState()
    {
        EntityEvents.Clear();
        TrackedEntries.Clear();
    }

    /// <summary>
    /// Publishes the accumulated entity events synchronously.
    /// Default implementation calls <see cref="PublishEventsAsync"/> and blocks.
    /// Override this if you need a different synchronous implementation.
    /// </summary>
    protected virtual void PublishEvents()
    {
        PublishEventsAsync(default).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Publishes the accumulated entity events.
    /// Implement this method to define how events are published (e.g., via MediatR).
    /// Events are available in <see cref="EntityEvents"/>. The base class automatically
    /// clears <see cref="EntityEvents"/> and <see cref="TrackedEntries"/> after this method completes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    protected abstract Task PublishEventsAsync(CancellationToken cancellationToken);
}
