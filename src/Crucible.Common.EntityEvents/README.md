# Crucible.Common.EntityEvents

Entity event infrastructure for Entity Framework Core. Automatically generates events when entities are created, updated, or deleted.

## Features

- **Automatic event generation** - Events are created from EF Core change tracking
- **Transaction-aware** - Events are generated after transaction commit to ensure consistency
- **Framework-agnostic** - No dependency on specific event publishing libraries (MediatR, etc.)
- **Source generator included** - Automatically add interfaces like `INotification` via attribute
- **Simple registration** - One method call configures everything

## Installation

```bash
dotnet add package Crucible.Common.EntityEvents
```

## Quick Start

### 1. Create Your DbContext

Inherit from `EventPublishingDbContext` and implement the `PublishEventsAsync` method:

```csharp
using Crucible.Common.EntityEvents.Abstractions;
using Microsoft.Extensions.Logging;

[GenerateEntityEventInterfaces(typeof(INotification))]
public class MyContext : EventPublishingDbContext
{
    public DbSet<MyEntity> MyEntities { get; set; }

    public MyContext(DbContextOptions<MyContext> options) : base(options) { }

    public override async Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken)
    {
        if (ServiceProvider is not null)
        {
            var mediator = ServiceProvider.GetRequiredService<IMediator>();
            var logger = ServiceProvider.GetRequiredService<ILogger<MyContext>>();

            foreach (var evt in events.Cast<INotification>())
            {
                try
                {
                    await mediator.Publish(evt, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error publishing entity event {EventType}", evt.GetType().Name);
                }
            }
        }
    }
}
```

The `EntityEventInterceptor` handles calling `PublishEventsAsync` at the right time (after SaveChanges for non-transactional operations, or after transaction commit). Events are passed as a parameter — the interceptor creates the events and clears its tracked state before calling your method. You can use the publishing mechanism of your choice. The example above uses MediatR.

### 2. Register Services

Use the `AddEventPublishingDbContextFactory` extension method for simple registration:

```csharp
using Crucible.Common.EntityEvents.Extensions;

services.AddEventPublishingDbContextFactory<MyContext>((sp, builder) =>
    builder.UseNpgsql(connectionString));
```

This single call:

- Registers the `EntityEventInterceptor`
- Creates a pooled DbContext factory with the interceptor configured
- Registers a scoped `MyContext` with `ServiceProvider` injected

#### Alternative: Manual Registration

If you need more control over registration:

```csharp
services.AddEntityEventInterceptor();
services.AddPooledDbContextFactory<MyContext>((sp, builder) => builder
    .AddInterceptors(sp.GetRequiredService<EntityEventInterceptor>())
    .UseNpgsql(connectionString));

// Register scoped context with ServiceProvider injection
services.AddScoped(sp =>
{
    var factory = sp.GetRequiredService<IDbContextFactory<MyContext>>();
    var context = factory.CreateDbContext();
    context.ServiceProvider = sp;
    return context;
});
```

## Publishing Integration

Publishing libraries may require specific interfaces be present on their published classes. To support this without tying this library to any specific publishing mechanism, all IEntityEvent classes are partial, allowing for them to be extended in the consuming application with any required interfaces.

### Automatic (Recommended) - Source Generator

Add the `GenerateEntityEventInterfaces` attribute directly on your DbContext class:

```csharp
using Crucible.Common.EntityEvents;
using MediatR;

[GenerateEntityEventInterfaces(typeof(INotification))]
public class MyContext : EventPublishingDbContext
{
    // ...
}
```

The source generator automatically creates partial class declarations that implement the passed in types for all entity event types. You can pass multiple types:

```csharp
[GenerateEntityEventInterfaces(typeof(INotification), typeof(IMyCustomInterface))]
```

### Manual (Legacy)

If the source generator doesn't work in your environment, create partial class extensions manually:

```csharp
using Crucible.Common.EntityEvents.Events;
using MediatR;

namespace Crucible.Common.EntityEvents.Events;

public partial class EntityCreated<TEntity> : INotification { }
public partial class EntityUpdated<TEntity> : INotification { }
public partial class EntityDeleted<TEntity> : INotification { }
```

## Event Types

- **EntityCreated&lt;TEntity&gt;** - Raised when an entity is added
- **EntityUpdated&lt;TEntity&gt;** - Raised when an entity is modified (includes `ModifiedProperties`)
- **EntityDeleted&lt;TEntity&gt;** - Raised when an entity is deleted

## How It Works

1. `EntityEventInterceptor` intercepts `SavingChanges` to capture entity states
2. After transaction commits (or SaveChanges completes if no transaction), events are created
3. The interceptor calls your `PublishEventsAsync` implementation to publish the events
4. The interceptor automatically clears events and tracked state after publishing
5. If a transaction is rolled back, tracked state is cleared without publishing

This ensures events are only published for changes that actually persisted to the database.

## Advanced: Manual IEventPublishingDbContext Implementation

If you need full control, implement `IEventPublishingDbContext` directly:

```csharp
using Crucible.Common.EntityEvents.Abstractions;

public class MyContext : DbContext, IEventPublishingDbContext
{
    public IServiceProvider? ServiceProvider { get; set; }
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];

    public Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken ct = default)
    {
        // Your event publishing logic here.
        // The interceptor creates events and passes them to this method.
        return Task.CompletedTask;
    }
}
```

> **Note:** `TrackedEntries` is used internally by the interceptor to track entity changes across multiple `SaveChanges` calls within a transaction. It must be stored on the DbContext (not the interceptor) for thread safety when using pooled contexts.
