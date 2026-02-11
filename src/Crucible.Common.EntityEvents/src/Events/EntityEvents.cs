// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

// These are partial classes - use the source generator with
// [GenerateEntityEventInterfaces(typeof(INotification))] on your DbContext
// to automatically add interface implementations (e.g., MediatR's INotification).

#nullable enable

using Crucible.Common.EntityEvents.Abstractions;

namespace Crucible.Common.EntityEvents.Events;

/// <summary>Event raised when an entity is created.</summary>
/// <typeparam name="TEntity">The type of entity that was created.</typeparam>
public partial class EntityCreated<TEntity>(TEntity entity) : IEntityEvent
{
    /// <summary>The entity that was created.</summary>
    public TEntity Entity { get; } = entity;
}

/// <summary>Event raised when an entity is updated.</summary>
/// <typeparam name="TEntity">The type of entity that was updated.</typeparam>
public partial class EntityUpdated<TEntity>(TEntity entity, string[] modifiedProperties) : IEntityEvent
{
    /// <summary>The entity that was updated.</summary>
    public TEntity Entity { get; } = entity;

    /// <summary>The names of the properties that were modified.</summary>
    public string[] ModifiedProperties { get; } = modifiedProperties;
}

/// <summary>Event raised when an entity is deleted.</summary>
/// <typeparam name="TEntity">The type of entity that was deleted.</typeparam>
public partial class EntityDeleted<TEntity>(TEntity entity) : IEntityEvent
{
    /// <summary>The entity that was deleted.</summary>
    public TEntity Entity { get; } = entity;
}
