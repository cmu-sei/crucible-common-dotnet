// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using System.ComponentModel.DataAnnotations;
using Crucible.Common.EntityEvents.Abstractions;
using Crucible.Common.EntityEvents.Events;
using Crucible.Common.EntityEvents.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

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
        Assert.Single(_context.EntityEvents);
        var evt = Assert.IsType<EntityCreated<TestEntity>>(_context.EntityEvents[0]);
        Assert.Equal("Test", evt.Entity.Name);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenEntityModified_CreatesEntityUpdatedEvent()
    {
        // Arrange
        var entity = new TestEntity { Name = "Original" };
        _context.TestEntities.Add(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);
        _context.EntityEvents.Clear();

        // Act
        entity.Name = "Modified";
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_context.EntityEvents);
        var evt = Assert.IsType<EntityUpdated<TestEntity>>(_context.EntityEvents[0]);
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
        _context.EntityEvents.Clear();

        // Act
        _context.TestEntities.Remove(entity);
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(_context.EntityEvents);
        var evt = Assert.IsType<EntityDeleted<TestEntity>>(_context.EntityEvents[0]);
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
        Assert.Equal(2, _context.EntityEvents.Count);
        Assert.All(_context.EntityEvents, e => Assert.IsType<EntityCreated<TestEntity>>(e));
    }

    [Fact]
    public async Task SaveChangesAsync_WhenNoChanges_CreatesNoEvents()
    {
        // Arrange - nothing

        // Act
        await _context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(_context.EntityEvents);
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
    public List<IEntityEvent> EntityEvents { get; } = [];
    public List<TrackedEntityEntry> TrackedEntries { get; } = [];

    public DbSet<TestEntity> TestEntities { get; set; }

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>()
            .Property(e => e.Id)
            .ValueGeneratedOnAdd();
    }
}
