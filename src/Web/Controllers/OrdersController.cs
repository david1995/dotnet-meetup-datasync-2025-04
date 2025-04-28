using System.Linq.Expressions;
using CommunityToolkit.Datasync.Server;
using CommunityToolkit.Datasync.Server.EntityFrameworkCore;
using CommunityToolkit.Datasync.Server.InMemory;
using Microsoft.AspNetCore.Mvc;
using Web.Models;

namespace Web.Controllers;

public class OrdersAccessControlProvider : IAccessControlProvider<Order>
{
    private readonly long _userId;
    private readonly ServerDataContext _context;

    public OrdersAccessControlProvider(ServerDataContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        
        // please do not do this in production, use real authentication
        var userName = httpContextAccessor.HttpContext!.Request.Headers["Authorization"].FirstOrDefault()?["Bearer ".Length ..];

        _userId = context.Users.First(u => u.UserName == userName).Id;
    }

    public Expression<Func<Order, bool>> GetDataView()
    {
        return
            order => order.AssignedUserId == _userId
                && (
                    order.Status == OrderStatus.Ready
                    || order.Status == OrderStatus.Delivered
                );
    }

    public ValueTask<bool> IsAuthorizedAsync(
        TableOperation operation,
        Order? entity,
        CancellationToken cancellationToken = new()
    )
    {
        return ValueTask.FromResult(_context.Orders.Any(o => o.AssignedUserId == _userId));
    }

    public ValueTask PreCommitHookAsync(
        TableOperation operation,
        Order entity,
        CancellationToken cancellationToken = new()) => ValueTask.CompletedTask;

    public ValueTask PostCommitHookAsync(
        TableOperation operation,
        Order entity,
        CancellationToken cancellationToken = new()) => ValueTask.CompletedTask;
}

public class CustomerAccessControlProvider : IAccessControlProvider<Customer>
{
    private readonly ServerDataContext _context;
    private readonly long _userId;

    public CustomerAccessControlProvider(ServerDataContext context, IHttpContextAccessor httpContextAccessor)
    {
        _context = context;
        // please do not do this in production, use real authentication
        var userName = httpContextAccessor.HttpContext!.Request.Headers["Authorization"].FirstOrDefault()?["Bearer ".Length..];

        _userId = context.Users.First(u => u.UserName == userName).Id;
    }

    public Expression<Func<Customer, bool>> GetDataView()
    {
        return entity => entity.Orders.Any(o => o.AssignedUserId == _userId);
    }

    public ValueTask<bool> IsAuthorizedAsync(
        TableOperation operation,
        Customer? entity,
        CancellationToken cancellationToken = new()
    )
    {
        return ValueTask.FromResult(_context.Orders.Any(o => o.AssignedUserId == _userId));
    }

    public ValueTask PreCommitHookAsync(
        TableOperation operation,
        Customer entity,
        CancellationToken cancellationToken = new()) => ValueTask.CompletedTask;

    public ValueTask PostCommitHookAsync(
        TableOperation operation,
        Customer entity,
        CancellationToken cancellationToken = new()) => ValueTask.CompletedTask;
}

[Route("management")]
public class ManagementController : Controller
{
    private readonly ServerDataContext _context;
    private readonly TimeProvider _timeProvider;

    public ManagementController(ServerDataContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    [HttpPost]
    public async Task<ActionResult> RecreateDatabase()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.Database.EnsureCreatedAsync();

        IReadOnlyList<User> users =
        [
            new() { UserName = "David" },
            new() { UserName = "Raoul" }
        ];

        await _context.Users.AddRangeAsync(users);

        await _context.SaveChangesAsync();

        await _context.Customers.AddAsync(new()
        {
            Name = "TechTalk",
            StreetAndNumber = "Leonard-Bernstein-Straﬂe 10/16",
            PostalCode = 1220,
            City = "Vienna",
            Orders = new List<Order>([
                new()
                {
                    AssignedUser = users[0],
                    CreatedAt = _timeProvider.GetUtcNow(),
                    Status = OrderStatus.Ready
                },
                new()
                {
                    AssignedUser = users[1],
                    CreatedAt = _timeProvider.GetUtcNow().AddDays(-10),
                    Status = OrderStatus.Ready
                }
            ])
        });

        await _context.Customers.AddAsync(new()
        {
            Name = "Some Random Company",
            StreetAndNumber = "Random Street 1",
            PostalCode = 1220,
            City = "Vienna",
            Orders = new List<Order>([
                new()
                {
                    AssignedUser = users[0],
                    CreatedAt = _timeProvider.GetUtcNow(),
                    Status = OrderStatus.Ready
                },
                new()
                {
                    AssignedUser = users[1],
                    CreatedAt = _timeProvider.GetUtcNow().AddDays(-10),
                    Status = OrderStatus.Ready
                }
            ])
        });

        await _context.SaveChangesAsync();

        return Ok();
    }
}

[Route("tables/orders")]
public class OrdersController : TableController<Order>
{
    public OrdersController(
        EntityTableRepository<Order> repository,
        OrdersAccessControlProvider accessControlProvider)
        : base(repository, accessControlProvider)
    {
    }

    protected override async ValueTask PostCommitHookAsync(
        TableOperation operation,
        Order entity,
        CancellationToken cancellationToken = new())
    {
        if (operation is TableOperation.Update
            && entity is
            {
                Status: OrderStatus.Cancelled,
                Deleted: false
            })
        {
            entity.Deleted = true;
        }

        await base.PostCommitHookAsync(operation, entity, cancellationToken);
    }

}

[Route("tables/customers")]
public class CustomersController : TableController<Customer>
{
    public CustomersController(
        EntityTableRepository<Customer> repository,
        CustomerAccessControlProvider accessControlProvider)
        : base(repository, accessControlProvider)
    {
    }
}

public class InMemoryCustomerStats : InMemoryTableData
{
    public required string CustomerId { get; set; }

    public int OrdersCreatedInThisMonth { get; set; }
}

[Route("tables/inmemorycustomerstats")]
public class CustomerStatsController : TableController<InMemoryCustomerStats>
{
    public CustomerStatsController(ServerDataContext context, TimeProvider timeProvider)
    {
        var now = timeProvider.GetLocalNow();
        var thisMonthBegin = now.Date.AddDays(-(now.Day - 1));

        Repository = new InMemoryRepository<InMemoryCustomerStats>(
            context.Customers
                .Select(c => new
                {
                    c.Id,
                    OrdersCreatedThisMonth = c.Orders.Count(o => o.CreatedAt > thisMonthBegin),
                    WorkerCountForOrders = c.Orders.Select(o => o.AssignedUserId).Distinct().Count()
                })
                .AsEnumerable()
                .Select(t => new InMemoryCustomerStats
                {
                    Id = t.Id,
                    CustomerId = t.Id,
                    OrdersCreatedInThisMonth = t.OrdersCreatedThisMonth
                }));
    }

    [NonAction]
    public override Task<IActionResult> CreateAsync(CancellationToken cancellationToken = new())
    {
        throw new NotSupportedException();
    }

    [NonAction]
    public override Task<IActionResult> DeleteAsync(string id, CancellationToken cancellationToken = new())
    {
        throw new NotSupportedException();
    }

    [NonAction]
    public override Task<IActionResult> ReplaceAsync(string id, CancellationToken cancellationToken = new())
    {
        throw new NotSupportedException();
    }
}
