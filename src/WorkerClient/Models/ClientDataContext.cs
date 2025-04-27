using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CommunityToolkit.Datasync.Client.Http;
using CommunityToolkit.Datasync.Client.Offline;
using Microsoft.EntityFrameworkCore;

namespace WorkerClient.Models;

public abstract class DatasyncClientData
{
    [StringLength(200)]
    public string Id { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    [Timestamp]
    public byte[] Version { get; set; } = [];
}

public enum OrderStatus
{
    Ready = 1,
    Assigned = 2,
    Delivered = 3,
    Cancelled = 4
}

public class Order : DatasyncClientData
{
    public OrderStatus Status { get; set; }

    public long? AssignedUserId { get; set; }

    [StringLength(200)]
    public string CustomerId { get; set; } = null!;

    [JsonIgnore]
    public virtual Customer Customer { get; set; } = null!;
}

public class Customer : DatasyncClientData
{
    [StringLength(200)]
    public required string StreetAndNumber { get; set; }

    public required int Plz { get; set; }

    [StringLength(200)]
    public required string City { get; set; }

    [JsonIgnore]
    public virtual ICollection<Order> Orders { get; set; } = [];
}

public class InMemoryCustomerStats : DatasyncClientData
{
    public int OrdersCreatedInThisMonth { get; set; }

    public int WorkerCountForOrders { get; set; }
}

public class UserNameStore
{
    public string UserName { get; set; } = "David";
}

public class ClientDataContext : OfflineDbContext
{
    private readonly UserNameStore _userNameStore;

    public ClientDataContext(UserNameStore userNameStore)
    {
        _userNameStore = userNameStore;
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<InMemoryCustomerStats> CustomerStats => Set<InMemoryCustomerStats>();

    protected override void OnDatasyncInitialization(DatasyncOfflineOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseHttpClientOptions(new HttpClientOptions { Endpoint = new Uri("https://localhost:51368") });
        optionsBuilder.Entity<Order>(o => o.Query.WithParameter("UserName", _userNameStore.UserName));
        optionsBuilder.Entity<Customer>(o => o.Query.WithParameter("UserName", _userNameStore.UserName));
        optionsBuilder.Entity<InMemoryCustomerStats>(o => o.Query.WithParameter("UserName", _userNameStore.UserName));
    }
}

public class SynchronizationAction
{
    public async ValueTask<PushResult> PushAllAsync()
    {
    }

    public async ValueTask<PullResult> PullAllAsync()
    {
    }
}
