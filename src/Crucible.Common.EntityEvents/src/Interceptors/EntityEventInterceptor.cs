// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Crucible.Common.EntityEvents.Abstractions;
using Crucible.Common.EntityEvents.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crucible.Common.EntityEvents.Interceptors;

/// <summary>
/// Intercepts saves to the database and generates entity events from entity changes.
///
/// As of EF7, transactions are not always created by SaveChanges for performance reasons, so we have to
/// handle both TransactionCommitted and SavedChanges. If a transaction is in progress,
/// SavedChanges will not generate the events and it will instead happen in TransactionCommitted.
///
/// Note: Entity tracking state is stored on the DbContext (via <see cref="IEventPublishingDbContext.TrackedEntries"/>)
/// rather than on the interceptor for thread safety when using pooled contexts.
/// </summary>
public class EntityEventInterceptor : DbTransactionInterceptor, ISaveChangesInterceptor
{
    private readonly ILogger<EntityEventInterceptor> _logger;

    /// <summary>
    /// Creates a new EntityEventInterceptor.
    /// </summary>
    /// <param name="logger">Logger for error reporting.</param>
    public EntityEventInterceptor(ILogger<EntityEventInterceptor> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        TransactionCommittedInternal(eventData);
        await base.TransactionCommittedAsync(transaction, eventData, cancellationToken);
    }

    /// <inheritdoc/>
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        TransactionCommittedInternal(eventData);
        base.TransactionCommitted(transaction, eventData);
    }

    private void TransactionCommittedInternal(TransactionEndEventData eventData)
    {
        try
        {
            // Store events in the context to be published after SaveChangesAsync completes
            // This avoids the Npgsql 10+ "Transaction is already completed" error
            SaveEvents(eventData.Context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TransactionCommitted");
        }
    }

    /// <inheritdoc/>
    public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SavedChangesInternal(eventData);
        return result;
    }

    /// <inheritdoc/>
    public ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        SavedChangesInternal(eventData);
        return new(result);
    }

    private void SavedChangesInternal(SaveChangesCompletedEventData eventData)
    {
        try
        {
            if (eventData.Context?.Database.CurrentTransaction == null)
            {
                SaveEvents(eventData.Context);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SavedChanges");
        }
    }

    /// <summary>
    /// Called before SaveChanges is performed. This saves the changed entities to be used at the end of the
    /// transaction for creating events from the final set of changes. May be called multiple times for a single
    /// transaction.
    /// </summary>
    public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SaveEntries(eventData.Context);
        return result;
    }

    /// <inheritdoc/>
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SaveEntries(eventData.Context);
        return new(result);
    }

    /// <summary>
    /// Creates and stores events in the context to be published after transaction cleanup.
    /// </summary>
    /// <param name="dbContext">The DbContext used for this transaction.</param>
    private void SaveEvents(DbContext? dbContext)
    {
        try
        {
            if (dbContext is IEventPublishingDbContext context)
            {
                var events = CreateEvents(context);
                context.EntityEvents.AddRange(events);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SaveEvents");
        }
    }

    private List<IEntityEvent> CreateEvents(IEventPublishingDbContext context)
    {
        var events = new List<IEntityEvent>();
        var entries = GetEntries(context);

        foreach (var entry in entries)
        {
            var entityType = entry.Entity.GetType();
            IEntityEvent? evt = null;

            switch (entry.State)
            {
                case EntityState.Added:
                    // Make sure properties generated by the db are set
                    var generatedProps = entry.Properties
                        .Where(x => x.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)
                        .ToList();

                    foreach (var prop in generatedProps)
                    {
                        entityType.GetProperty(prop.Metadata.Name)?.SetValue(entry.Entity, prop.CurrentValue);
                    }

                    evt = CreateEvent(typeof(EntityCreated<>), entityType, entry.Entity);
                    break;
                case EntityState.Modified:
                    var modifiedProperties = entry.GetModifiedProperties();
                    evt = CreateEvent(typeof(EntityUpdated<>), entityType, entry.Entity, modifiedProperties);
                    break;
                case EntityState.Deleted:
                    evt = CreateEvent(typeof(EntityDeleted<>), entityType, entry.Entity);
                    break;
            }

            if (evt != null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    private static IEntityEvent? CreateEvent(Type openGenericType, Type entityType, params object[] args)
    {
        var closedType = openGenericType.MakeGenericType(entityType);
        return Activator.CreateInstance(closedType, args) as IEntityEvent;
    }

    private TrackedEntityEntry[] GetEntries(IEventPublishingDbContext context)
    {
        var entries = context.TrackedEntries
            .Where(x => x.State == EntityState.Added ||
                        x.State == EntityState.Modified ||
                        x.State == EntityState.Deleted)
            .ToList();

        context.TrackedEntries.Clear();
        return entries.ToArray();
    }

    /// <summary>
    /// Keeps track of changes across multiple SaveChanges calls in a transaction, without duplicates.
    /// </summary>
    private void SaveEntries(DbContext? db)
    {
        if (db is not IEventPublishingDbContext context)
            return;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            try
            {
                // Find value of id property (typically a property with ValueGenerated.OnAdd)
                var id = entry.Properties
                    .FirstOrDefault(x =>
                        x.Metadata.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)?.CurrentValue;

                // Find matching existing entry, if any
                TrackedEntityEntry? e = null;

                if (id is not null)
                {
                    e = context.TrackedEntries.FirstOrDefault(x => id.Equals(x.Properties?.FirstOrDefault(y =>
                        y.Metadata?.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd)?.CurrentValue));
                }

                if (e is not null)
                {
                    // If entry already exists, mark which properties were previously modified,
                    // remove old entry and add new one, to avoid duplicates
                    var newEntry = new TrackedEntityEntry(entry, e);
                    context.TrackedEntries.Remove(e);
                    context.TrackedEntries.Add(newEntry);
                }
                else
                {
                    context.TrackedEntries.Add(new TrackedEntityEntry(entry));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing entry in SaveEntries");
            }
        }
    }
}
