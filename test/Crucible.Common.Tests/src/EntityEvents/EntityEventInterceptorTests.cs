// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Crucible.Common.EntityEvents.Abstractions;
using Crucible.Common.EntityEvents.Events;
using Crucible.Common.EntityEvents.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Crucible.Common.Tests.EntityEvents;

public class EntityEventInterceptorTests
{
    private readonly EntityEventInterceptor _interceptor;
    private readonly TestDbContext _context;

    public EntityEventInterceptorTests()
    {
        var logger = NullLogger<EntityEventInterceptor>.Instance;
        _interceptor = new EntityEventInterceptor(logger);

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(_interceptor)
            .Options;

        _context = new TestDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityAdded_CreatesEntityCreatedEvent()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test" };
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Test", evt.Entity.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityModified_CreatesEntityUpdatedEvent()
    {
        // Arrange
        var entity = new TestEntity { Name = "Original" };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.PublishedEvents.Clear();

        // Act
        entity.Name = "Modified";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityUpdated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Modified", evt.Entity.Name);
        Assert.Contains("Name", evt.ModifiedProperties);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityDeleted_CreatesEntityDeletedEvent()
    {
        // Arrange
        var entity = new TestEntity { Name = "ToDelete" };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.PublishedEvents.Clear();

        // Act
        _context.TestEntities.Remove(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityDeleted<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("ToDelete", evt.Entity.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenMultipleEntitiesChanged_CreatesMultipleEvents()
    {
        // Arrange
        var entity1 = new TestEntity { Name = "Entity1" };
        var entity2 = new TestEntity { Name = "Entity2" };
        _context.TestEntities.AddRange(entity1, entity2);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, _context.PublishedEvents.Count);
        Assert.All(_context.PublishedEvents, e => Assert.IsType<EntityCreated<TestEntity>>(e));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoChanges_CreatesNoEvents()
    {
        // Arrange - nothing

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_context.PublishedEvents);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEventHandlerCallsSaveChanges_PublishesBothEvents()
    {
        // Arrange - configure the context to add a new entity during event publishing (re-entrancy)
        _context.OnPublish = async (events, ctx, ct) =>
        {
            // Only trigger nested save for the first entity creation, to avoid infinite recursion
            if (events.Any(e => e is EntityCreated<TestEntity> c && c.Entity.Name == "Original"))
            {
                ctx.TestEntities.Add(new TestEntity { Name = "CreatedByHandler" });
                await ctx.SaveChangesAsync(ct);
            }
        };

        var entity = new TestEntity { Name = "Original" };
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert - both the original event and the nested event should be published
        Assert.Equal(2, _context.PublishedEvents.Count);

        var originalEvent = _context.PublishedEvents.OfType<EntityCreated<TestEntity>>()
            .Single(e => e.Entity.Name == "Original");
        var nestedEvent = _context.PublishedEvents.OfType<EntityCreated<TestEntity>>()
            .Single(e => e.Entity.Name == "CreatedByHandler");

        Assert.NotNull(originalEvent);
        Assert.NotNull(nestedEvent);
    }
}

public class EntityEventInterceptorTransactionTests : IDisposable
{
    private readonly EntityEventInterceptor _interceptor;
    private readonly SqliteTestDbContext _context;
    private readonly SqliteConnection _connection;

    public EntityEventInterceptorTransactionTests()
    {
        var logger = NullLogger<EntityEventInterceptor>.Instance;
        _interceptor = new EntityEventInterceptor(logger);

        // SQLite in-memory requires an open connection kept alive for the lifetime of the test
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<SqliteTestDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new SqliteTestDbContext(options);
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task Transaction_SingleSaveChanges_PublishesEventsAfterCommit()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test" };

        // Act
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Events should not be published yet (transaction still open)
        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert - events published after commit
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Test", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_MultipleSaveChanges_DeduplicatesEntries()
    {
        // Arrange
        var entity = new TestEntity { Name = "Original" };

        // Act
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        // First save - create
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second save - modify the same entity
        entity.Name = "Modified";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert - entity was created then modified in the same transaction,
        // so the interceptor deduplicates to a single Created event with final state
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Modified", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_MultipleSaveChanges_MultipleEntities_PublishesAllEvents()
    {
        // Arrange & Act
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        // First save - create entity1
        var entity1 = new TestEntity { Name = "Entity1" };
        _context.TestEntities.Add(entity1);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Second save - create entity2
        var entity2 = new TestEntity { Name = "Entity2" };
        _context.TestEntities.Add(entity2);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert - both entities tracked across SaveChanges calls
        Assert.Equal(2, _context.PublishedEvents.Count);
        Assert.All(_context.PublishedEvents, e => Assert.IsType<EntityCreated<TestEntity>>(e));
    }

    [Fact]
    public async Task Transaction_Rollback_ClearsStateWithNoEvents()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test" };

        // Act
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert - no events published, state cleared
        Assert.Empty(_context.PublishedEvents);
        Assert.Empty(_context.TrackedEntries);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSaveChangesFails_ClearsTrackedEntries()
    {
        // Arrange - insert a row directly via SQL so EF doesn't know about it
        var id = Guid.NewGuid();
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO TestEntities (Id, Name) VALUES ({0}, {1})", id, "Existing");

        // Act - try to add an entity with the same PK, which will fail at the DB level
        var duplicate = new TestEntity { Id = id, Name = "Duplicate" };
        _context.TestEntities.Add(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _context.SaveChangesAsync(TestContext.Current.CancellationToken));

        // SaveChangesFailed should have cleared TrackedEntries
        Assert.Empty(_context.TrackedEntries);
        Assert.Empty(_context.PublishedEvents);

        // Detach the failed entity so we can do a clean save
        _context.Entry(duplicate).State = EntityState.Detached;

        // Add a new valid entity and save successfully
        var validEntity = new TestEntity { Name = "AfterFailure" };
        _context.TestEntities.Add(validEntity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert - only the new entity's event (no ghost from failed save)
        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("AfterFailure", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_EventHandlerCanQueryDbAfterCommit()
    {
        // Arrange - configure the handler to query the DB during event publishing
        TestEntity? queriedEntity = null;
        _context.OnPublish = async (events, ctx, ct) =>
        {
            // This query would fail with "Transaction is already completed" if
            // CurrentTransaction wasn't cleared before publishing
            queriedEntity = await ctx.TestEntities
                .FirstOrDefaultAsync(e => e.Name == "Queryable", ct);
        };

        var entity = new TestEntity { Name = "Queryable" };

        // Act
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        // Assert - event was published and handler could query the DB
        Assert.Single(_context.PublishedEvents);
        Assert.NotNull(queriedEntity);
        Assert.Equal("Queryable", queriedEntity.Name);
    }

    [Fact]
    public async Task NoTransaction_PublishesEventsImmediately()
    {
        // Arrange
        var entity = new TestEntity { Name = "Test" };
        _context.TestEntities.Add(entity);

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert - events published immediately (no transaction)
        Assert.Single(_context.PublishedEvents);
        Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
    }
}

public class SqliteTestDbContext : DbContext, IEventPublishingDbContext
{
    public IServiceProvider? ServiceProvider { get; set; }
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];
    public List<IEntityEvent> PublishedEvents { get; } = [];

    public Func<IReadOnlyList<IEntityEvent>, SqliteTestDbContext, CancellationToken, Task>? OnPublish { get; set; }

    public DbSet<TestEntity> TestEntities { get; set; }

    public SqliteTestDbContext(DbContextOptions<SqliteTestDbContext> options) : base(options) { }

    public async Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken = default)
    {
        PublishedEvents.AddRange(events);

        if (OnPublish != null)
        {
            await OnPublish(events, this, cancellationToken);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}

public class TestEntity
{
    [Key]
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class TestDbContext : DbContext, IEventPublishingDbContext
{
    public IServiceProvider? ServiceProvider { get; set; }
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];
    public List<IEntityEvent> PublishedEvents { get; } = [];

    /// <summary>
    /// Optional callback for testing re-entrancy (event handler calling SaveChanges).
    /// </summary>
    public Func<IReadOnlyList<IEntityEvent>, TestDbContext, CancellationToken, Task>? OnPublish { get; set; }

    public DbSet<TestEntity> TestEntities { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public async Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken = default)
    {
        PublishedEvents.AddRange(events);

        if (OnPublish != null)
        {
            await OnPublish(events, this, cancellationToken);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:latest")
        .WithUsername("test")
        .WithPassword("test")
        .WithAutoRemove(true)
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var context = new PostgresTestDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

public class PostgresTestDbContext : DbContext, IEventPublishingDbContext
{
    public IServiceProvider? ServiceProvider { get; set; }
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];
    public List<IEntityEvent> PublishedEvents { get; } = [];

    public Func<IReadOnlyList<IEntityEvent>, PostgresTestDbContext, CancellationToken, Task>? OnPublish { get; set; }

    public DbSet<TestEntity> TestEntities { get; set; }

    public PostgresTestDbContext(DbContextOptions<PostgresTestDbContext> options) : base(options) { }

    public async Task PublishEventsAsync(IReadOnlyList<IEntityEvent> events, CancellationToken cancellationToken = default)
    {
        PublishedEvents.AddRange(events);

        if (OnPublish != null)
        {
            await OnPublish(events, this, cancellationToken);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}

public class EntityEventInterceptorPostgresTests : IClassFixture<PostgresFixture>
{
    private readonly EntityEventInterceptor _interceptor;
    private readonly PostgresTestDbContext _context;

    public EntityEventInterceptorPostgresTests(PostgresFixture fixture)
    {
        var logger = NullLogger<EntityEventInterceptor>.Instance;
        _interceptor = new EntityEventInterceptor(logger);

        var options = new DbContextOptionsBuilder<PostgresTestDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new PostgresTestDbContext(options);
    }

    [Fact]
    public async Task Transaction_SingleSaveChanges_PublishesEventsAfterCommit()
    {
        var entity = new TestEntity { Name = "Test" };

        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Test", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_MultipleSaveChanges_DeduplicatesEntries()
    {
        var entity = new TestEntity { Name = "Original" };

        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity.Name = "Modified";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("Modified", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_MultipleSaveChanges_MultipleEntities_PublishesAllEvents()
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var entity1 = new TestEntity { Name = "Entity1" };
        _context.TestEntities.Add(entity1);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entity2 = new TestEntity { Name = "Entity2" };
        _context.TestEntities.Add(entity2);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);

        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _context.PublishedEvents.Count);
        Assert.All(_context.PublishedEvents, e => Assert.IsType<EntityCreated<TestEntity>>(e));
    }

    [Fact]
    public async Task Transaction_Rollback_ClearsStateWithNoEvents()
    {
        var entity = new TestEntity { Name = "Test" };

        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await transaction.RollbackAsync(TestContext.Current.CancellationToken);

        Assert.Empty(_context.PublishedEvents);
        Assert.Empty(_context.TrackedEntries);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenSaveChangesFails_ClearsTrackedEntries()
    {
        var id = Guid.NewGuid();
        await _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"TestEntities\" (\"Id\", \"Name\") VALUES ({0}, {1})", id, "Existing");

        var duplicate = new TestEntity { Id = id, Name = "Duplicate" };
        _context.TestEntities.Add(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(
            () => _context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Empty(_context.TrackedEntries);
        Assert.Empty(_context.PublishedEvents);

        _context.Entry(duplicate).State = EntityState.Detached;

        var validEntity = new TestEntity { Name = "AfterFailure" };
        _context.TestEntities.Add(validEntity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Single(_context.PublishedEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.PublishedEvents[0]);
        Assert.Equal("AfterFailure", evt.Entity.Name);
    }

    [Fact]
    public async Task Transaction_EventHandlerCanQueryDbAfterCommit()
    {
        TestEntity? queriedEntity = null;
        var entityName = $"Queryable-{Guid.NewGuid()}";
        _context.OnPublish = async (events, ctx, ct) =>
        {
            queriedEntity = await ctx.TestEntities
                .FirstOrDefaultAsync(e => e.Name == entityName, ct);
        };

        var entity = new TestEntity { Name = entityName };

        await using var transaction = await _context.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await transaction.CommitAsync(TestContext.Current.CancellationToken);

        Assert.Single(_context.PublishedEvents);
        Assert.NotNull(queriedEntity);
        Assert.Equal(entityName, queriedEntity.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEventHandlerCallsSaveChanges_PublishesBothEvents()
    {
        var originalName = $"Original-{Guid.NewGuid()}";
        var handlerName = $"CreatedByHandler-{Guid.NewGuid()}";

        _context.OnPublish = async (events, ctx, ct) =>
        {
            if (events.Any(e => e is EntityCreated<TestEntity> c && c.Entity.Name == originalName))
            {
                ctx.TestEntities.Add(new TestEntity { Name = handlerName });
                await ctx.SaveChangesAsync(ct);
            }
        };

        var entity = new TestEntity { Name = originalName };
        _context.TestEntities.Add(entity);

        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, _context.PublishedEvents.Count);

        var originalEvent = _context.PublishedEvents.OfType<EntityCreated<TestEntity>>()
            .Single(e => e.Entity.Name == originalName);
        var nestedEvent = _context.PublishedEvents.OfType<EntityCreated<TestEntity>>()
            .Single(e => e.Entity.Name == handlerName);

        Assert.NotNull(originalEvent);
        Assert.NotNull(nestedEvent);
    }
}
