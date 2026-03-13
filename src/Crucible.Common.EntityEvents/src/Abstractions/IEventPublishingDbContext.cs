// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Crucible.Common.EntityEvents.Abstractions;

/// <summary>
/// Interface for DbContext classes that support entity event publishing.
/// Implement this interface on your DbContext to enable the <see cref="Interceptors.EntityEventInterceptor"/>
/// to track entity changes and publish events.
/// </summary>
public interface IEventPublishingDbContext
{
    /// <summary>
    /// Service provider used to resolve services for event publishing.
    /// This should be set by a context factory after creating the context from a pool.
    /// </summary>
    IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// Tracked entity entries used internally by <see cref="Interceptors.EntityEventInterceptor"/>
    /// to track changes across multiple SaveChanges calls within a transaction.
    /// This must be stored on the DbContext (not the interceptor) for thread safety
    /// when using pooled contexts.
    /// </summary>
    List<TrackedEntityEntry> TrackedEntries { get; }

    /// <summary>
    /// Publishes the entity events created by <see cref="Interceptors.EntityEventInterceptor"/>.
    /// Called by the interceptor after events have been created from tracked changes.
    /// </summary>
    /// <param name="events">The events to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken = default);
}
