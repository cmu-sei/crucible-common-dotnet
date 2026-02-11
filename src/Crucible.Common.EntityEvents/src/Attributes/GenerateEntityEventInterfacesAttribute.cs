// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

#nullable enable

using System;

namespace Crucible.Common.EntityEvents;

/// <summary>
/// When applied to a DbContext class, generates partial class declarations for EntityCreated,
/// EntityUpdated, and EntityDeleted that implement the specified interface types.
/// </summary>
/// <remarks>
/// This attribute works with a source generator to automatically create partial class
/// extensions, eliminating the need to manually create partial class files.
/// </remarks>
/// <example>
/// <code>
/// [GenerateEntityEventInterfaces(typeof(INotification), typeof(IMyCustomInterface))]
/// public class MyDbContext : EventPublishingDbContext
/// {
///     // ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class GenerateEntityEventInterfacesAttribute : Attribute
{
    /// <summary>
    /// The interface types that the entity event classes should implement.
    /// </summary>
    public Type[] InterfaceTypes { get; }

    /// <summary>
    /// Creates a new GenerateEntityEventInterfacesAttribute.
    /// </summary>
    /// <param name="interfaceTypes">The interface types to implement (e.g., typeof(INotification)).</param>
    public GenerateEntityEventInterfacesAttribute(params Type[] interfaceTypes)
    {
        InterfaceTypes = interfaceTypes ?? throw new ArgumentNullException(nameof(interfaceTypes));
    }
}
