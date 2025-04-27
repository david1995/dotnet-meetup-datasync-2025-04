using System.Linq.Expressions;
using CommunityToolkit.Datasync.Server;
using CommunityToolkit.Datasync.Server.EntityFrameworkCore;
using CommunityToolkit.Datasync.Server.InMemory;
using Microsoft.AspNetCore.Authorization;
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
        var userName = httpContextAccessor.HttpContext!.Request.Query["UserName"].FirstOrDefault(); // please do not do this in real projects
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
        var userName = httpContextAccessor.HttpContext!.Request.Query["UserName"].FirstOrDefault(); // please do not do this in real projects
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

[Route("tables/orders")]
[Authorize]
public class OrdersController : TableController<Order>
{
    public OrdersController(
        EntityTableRepository<Order> repository,
        OrdersAccessControlProvider accessControlProvider)
        : base(repository, accessControlProvider)
    {
    }
}

[Route("tables/customers")]
[Authorize]
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
    public int OrdersCreatedInThisMonth { get; set; }

    public int WorkerCountForOrders { get; set; }
}

[Route("tables/inmemorycustomerstats")]
[Authorize]
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
                    OrdersCreatedInThisMonth = t.OrdersCreatedThisMonth,
                    WorkerCountForOrders = t.WorkerCountForOrders
                }));
    }

    [NonAction]
    public override Task<IActionResult> CreateAsync(CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotSupportedException();
    }

    [NonAction]
    public override Task<IActionResult> DeleteAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotSupportedException();
    }

    [NonAction]
    public override Task<IActionResult> ReplaceAsync(string id, CancellationToken cancellationToken = new CancellationToken())
    {
        throw new NotSupportedException();
    }
}
