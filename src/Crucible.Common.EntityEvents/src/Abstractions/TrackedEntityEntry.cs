// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Crucible.Common.EntityEvents.Abstractions;

/// <summary>
/// Tracks entity state and property modifications across multiple SaveChanges calls within a transaction.
/// Used by <see cref="Interceptors.EntityEventInterceptor"/> to generate accurate entity events.
/// Must be stored on the DbContext (via <see cref="IEventPublishingDbContext.TrackedEntries"/>)
/// for thread safety when using pooled contexts.
/// </summary>
public class TrackedEntityEntry
{
    /// <summary>
    /// The tracked entity instance.
    /// </summary>
    public object Entity { get; set; }

    /// <summary>
    /// The state of the entity (Added, Modified, Deleted, etc.).
    /// </summary>
    public EntityState State { get; set; }

    /// <summary>
    /// The property entries for this entity.
    /// </summary>
    public IEnumerable<PropertyEntry> Properties { get; set; }

    private Dictionary<string, bool> IsPropertyModified { get; set; } = new();

    /// <summary>
    /// Creates a new tracked entry from an EF Core EntityEntry.
    /// </summary>
    /// <param name="entry">The EF Core entity entry to track.</param>
    /// <param name="oldEntry">Optional previous entry to merge modification state from.</param>
    public TrackedEntityEntry(EntityEntry entry, TrackedEntityEntry? oldEntry = null)
    {
        Entity = entry.Entity;
        State = entry.State;
        Properties = entry.Properties;

        ProcessOldEntry(oldEntry);

        foreach (var prop in Properties)
        {
            IsPropertyModified[prop.Metadata.Name] = prop.IsModified;
        }
    }

    private void ProcessOldEntry(TrackedEntityEntry? oldEntry)
    {
        if (oldEntry == null) return;

        if (oldEntry.State != EntityState.Unchanged && oldEntry.State != EntityState.Detached)
        {
            State = oldEntry.State;
        }

        var modifiedProperties = oldEntry.GetModifiedProperties();

        foreach (var property in Properties)
        {
            if (modifiedProperties.Contains(property.Metadata.Name))
            {
                property.IsModified = true;
            }
        }
    }

    /// <summary>
    /// Gets the names of all properties that were modified.
    /// </summary>
    /// <returns>An array of property names that were modified.</returns>
    public string[] GetModifiedProperties()
    {
        return IsPropertyModified
            .Where(x => x.Value)
            .Select(x => x.Key)
            .ToArray();
    }
}
