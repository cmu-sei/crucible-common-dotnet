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
/// Consuming apps implement <see cref="PublishEventsAsync"/> to define how events are published.
/// The <see cref="Interceptors.EntityEventInterceptor"/> handles calling this method at the right time.
/// </summary>
public abstract class EventPublishingDbContext : DbContext, IEventPublishingDbContext
{
    /// <inheritdoc/>
    public IServiceProvider? ServiceProvider { get; set; }

    /// <inheritdoc/>
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];

    /// <summary>
    /// Creates a new EventPublishingDbContext with the specified options.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    protected EventPublishingDbContext(DbContextOptions options) : base(options) { }

    /// <summary>
    /// Publishes the entity events.
    /// Implement this method to define how events are published (e.g., via MediatR).
    /// </summary>
    /// <param name="events">The events to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public abstract Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken);
}
