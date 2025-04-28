using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CommunityToolkit.Datasync.Server.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace Web.Models;

public class User
{
    public long Id { get; set; }

    [StringLength(50)]
    public required string UserName { get; set; }
}

public enum OrderStatus
{
    Ready = 1,
    Delivered = 2,
    Cancelled = 3
}

public class Order : EntityTableData
{
    public DateTimeOffset CreatedAt { get; set; }

    public OrderStatus Status { get; set; }

    public long? AssignedUserId { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [JsonIgnore]
    public virtual User? AssignedUser { get; set; }

    [StringLength(200)]
    public string CustomerId { get; set; } = null!;

    [JsonIgnore]
    public virtual Customer Customer { get; set; } = null!;
}

public class Customer : EntityTableData
{
    [StringLength(200)]
    public required string StreetAndNumber { get; set; }

    public required int Plz { get; set; }

    [StringLength(200)]
    public required string City { get; set; }

    [JsonIgnore]
    public virtual ICollection<Order> Orders { get; set; } = [];
}

public class ServerDataContext
    : DbContext
{
    public ServerDataContext(DbContextOptions<ServerDataContext> options)
        : base(options)
    {
    }

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Do any database initialization required.
    /// </summary>
    /// <returns>A task that completes when the database is initialized</returns>
    public async Task InitializeDatabaseAsync()
    {
        await Database.EnsureCreatedAsync().ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var type in modelBuilder.Model.GetEntityTypes()
                     .Where(t => t.ClrType.IsSubclassOf(typeof(EntityTableData))))
        {
            modelBuilder.Entity(type.ClrType)
                .HasKey(nameof(EntityTableData.Id))
                .IsClustered(false);

            modelBuilder.Entity(type.ClrType)
                .Property(nameof(EntityTableData.Id))
                .HasValueGenerator(typeof(GuidStringValueGenerator))
                .HasMaxLength(200);
        }
    }
}

public class GuidStringValueGenerator : ValueGenerator
{
    protected override object? NextValue(EntityEntry entry)
    {
        return Guid.CreateVersion7().ToString();
    }

    public override bool GeneratesTemporaryValues => false;
}