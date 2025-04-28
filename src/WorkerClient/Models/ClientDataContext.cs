using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using CommunityToolkit.Datasync.Client.Http;
using CommunityToolkit.Datasync.Client.Offline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Datasync.Client.Authentication;

namespace WorkerClient.Models;

public abstract class DatasyncClientData
{
    [StringLength(200)]
    public string Id { get; set; } = null!;

    public DateTimeOffset UpdatedAt { get; set; }

    public bool Deleted { get; set; }

    public string? Version { get; set; }
}

public enum OrderStatus
{
    Ready = 1,
    Delivered = 2,
    Cancelled = 3
}

public class Order : DatasyncClientData
{
    public OrderStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long? AssignedUserId { get; set; }

    [StringLength(200)]
    public string CustomerId { get; set; } = null!;

    [JsonIgnore]
    public virtual Customer Customer { get; set; } = null!;
}

public class Customer : DatasyncClientData
{
    [StringLength(200)]
    public string Name { get; set; } = null!;

    [StringLength(200)]
    public required string StreetAndNumber { get; set; }

    public required int PostalCode { get; set; }

    [StringLength(200)]
    public required string City { get; set; }

    [JsonIgnore]
    public virtual ICollection<Order> Orders { get; set; } = [];

    [JsonIgnore]
    public virtual InMemoryCustomerStats Stats { get; set; } = null!;
}

public class InMemoryCustomerStats : DatasyncClientData
{
    [StringLength(200)]
    public string CustomerId { get; set; } = null!;

    [JsonIgnore]
    public virtual Customer Customer { get; set; } = null!;

    public int OrdersCreatedInThisMonth { get; set; }
}

public class UserNameStore
{
    public string UserName { get; set; } = "David";
}

public class ClientDataContext : OfflineDbContext
{
    private readonly UserNameStore _userNameStore;

    public ClientDataContext(UserNameStore userNameStore, DbContextOptions<ClientDataContext> options)
        : base(options)
    {
        _userNameStore = userNameStore;
    }

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<InMemoryCustomerStats> CustomerStats => Set<InMemoryCustomerStats>();

    protected override void OnDatasyncInitialization(DatasyncOfflineOptionsBuilder optionsBuilder)
    {
        GenericAuthenticationProvider authProvider = new(
            _ => Task.FromResult(new AuthenticationToken
            {
                UserId = _userNameStore.UserName,
                DisplayName = _userNameStore.UserName,
                ExpiresOn = DateTimeOffset.MaxValue,
                Token = _userNameStore.UserName
            }));

        optionsBuilder.UseHttpClientOptions(new HttpClientOptions
        {
            Endpoint = new Uri("https://localhost:51368"),
            HttpPipeline = [ authProvider ]
        });
        optionsBuilder.Entity<Order>(o =>
        {
            o.Endpoint = new Uri("tables/orders", UriKind.Relative);
        });

        optionsBuilder.Entity<Customer>(o =>
        {
            o.Endpoint = new Uri("tables/customers", UriKind.Relative);
        });

        optionsBuilder.Entity<InMemoryCustomerStats>(o =>
        {
            o.Endpoint = new Uri("tables/inmemorycustomerstats", UriKind.Relative);
        });

        //GenericAuthenticationProvider authProvider = new(_identityService.GetAuthenticationTokenAsync);

        //HttpClientOptions clientOptions = new()
        //{
        //    Endpoint = new Uri(_serviceOptions.Value.ServiceUrl),
        //    HttpPipeline = [authProvider, _loggingHandler]
        //};
    }
}

public class SynchronisationAction
{
    private readonly IDbContextFactory<ClientDataContext> _contextFactory;

    public SynchronisationAction(IDbContextFactory<ClientDataContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async ValueTask<PushResult?> PushAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        try
        {
            return await context.PushAsync(
            [
                typeof(Order),
                typeof(Customer),
            ]);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async ValueTask<PullResult?> PullAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        try
        {
            return await context.PullAsync(
            [
                typeof(Order),
                typeof(Customer),
                typeof(InMemoryCustomerStats),
            ]);
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _logger;

    public LoggingHandler(ILogger<LoggingHandler> logger)
    {
        _logger = logger;
    }

    public LoggingHandler(ILogger<LoggingHandler> logger, HttpMessageHandler innerHandler) : base(innerHandler)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = default;
        try
        {
            await LogRequest(request);

            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            await LogResponse(response);
        }
        catch (Exception e)
        {
            _logger.LogError("Exception thrown: {exception}", e);
        }

        return response!;
    }

    private async Task LogResponse(HttpResponseMessage response)
    {
        StringBuilder stringBuilder = new();

        stringBuilder.AppendLine($"[HTTP] <<< {response.StatusCode} {response.ReasonPhrase}");
        PrintHeaders("<<<", response.Headers, stringBuilder);
        await PrintContentAsync("<<<", response.Content, stringBuilder);

        _logger.LogDebug(stringBuilder.ToString());
    }

    private async Task LogRequest(HttpRequestMessage request)
    {
        StringBuilder stringBuilder = new();

        stringBuilder.AppendLine($"[HTTP] >>> {request.Method} {request.RequestUri}");
        PrintHeaders(">>>", request.Headers, stringBuilder);
        await PrintContentAsync(">>>", request.Content, stringBuilder);

        _logger.LogDebug(stringBuilder.ToString());
    }

    private void PrintHeaders(string prefix, HttpHeaders headers, StringBuilder stringBuilder)
    {
        foreach (var header in headers)
        {
            foreach (var hdrVal in header.Value)
            {
                stringBuilder.AppendLine($"[HTTP] {prefix} {header.Key}: {hdrVal}");
            }
        }
    }

    private async Task PrintContentAsync(string prefix, HttpContent? content, StringBuilder stringBuilder)
    {
        if (content is null)
        {
            return;
        }

        PrintHeaders(prefix, content.Headers, stringBuilder);
        var contentAsString = await content.ReadAsStringAsync().ConfigureAwait(false);
        stringBuilder.AppendLine($"[HTTP] {prefix} {contentAsString}");
    }
}
