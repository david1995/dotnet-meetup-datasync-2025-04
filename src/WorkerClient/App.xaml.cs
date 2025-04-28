using System.Net.Http;
using CommunityToolkit.Datasync.Client.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Windows;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkerClient.Models;

namespace WorkerClient;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = SetupHost();

        _host.StartAsync();

        InitializeComponent();
    }

    private IHost SetupHost()
    {
        // set default Datasync serializer options
        DatasyncSerializer.JsonSerializerOptions.TypeInfoResolver = new CastleProxyResolver(
            DatasyncSerializer.JsonSerializerOptions.TypeInfoResolver
            ?? new DefaultJsonTypeInfoResolver()
        );

        var hostBuilder = Host.CreateApplicationBuilder();
        RegisterServices(hostBuilder);
        var host = hostBuilder.Build();

        using var serviceScope = host.Services.CreateScope();

        var contextFactory = serviceScope.ServiceProvider.GetRequiredService<IDbContextFactory<ClientDataContext>>();
        using var context = contextFactory.CreateDbContext();

        context.Database.EnsureCreated();

        return host;
    }

    private void RegisterServices(HostApplicationBuilder hostBuilder)
    {
        hostBuilder.Services
            .AddHttpClient("Default")
            .AddHttpMessageHandler(svc =>
            {
                var httpClientHandler = new HttpClientHandler();
                httpClientHandler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, sslPolicyErrors) => true;
                return new LoggingHandler(svc.GetRequiredService<ILogger<LoggingHandler>>(), httpClientHandler);
            });

        hostBuilder.Services.AddDbContextFactory<ClientDataContext>(
            builder => builder
                .UseLazyLoadingProxies()
                .UseSqlite("Data Source=client.db;Foreign Keys=False"));

        hostBuilder.Services.AddSingleton<UserNameStore>();
        hostBuilder.Services.AddSingleton<SynchronisationAction>();
        hostBuilder.Services.AddSingleton<CompleteOrderAction>();
        hostBuilder.Services.AddSingleton<CancelOrderAction>();
        hostBuilder.Services.AddTransient<MainViewModel>();
    }

    public static App CurrentApp => (App)Current;

    public T GetRequiredService<T>() where T : notnull
        => _host.Services.GetRequiredService<T>();
}

public class CastleProxyResolver : IJsonTypeInfoResolver
{
    private const BindingFlags PropertyBindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const string CastleProxiesNamespace = "Castle.Proxies";
    private readonly IJsonTypeInfoResolver _baseResolver;

    public CastleProxyResolver(IJsonTypeInfoResolver baseResolver)
    {
        _baseResolver = baseResolver;
    }

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = _baseResolver.GetTypeInfo(type, options);

        if (typeInfo is null)
        {
            return null;
        }

        if (type.Namespace?.Contains(CastleProxiesNamespace) == true)
        {
            var realPropertyInfos =
                type.BaseType!.GetProperties(PropertyBindingFlags);

            var propertyInfosIntroducedByCastleCoreProxy =
                typeInfo.Properties
                    .ExceptBy(realPropertyInfos.Select(pi => pi.Name.ToUpper()), t => t.Name.ToUpper());

            var propertyInfosToRemove = typeInfo.Properties
                .Join(realPropertyInfos,
                    p => p.Name.ToUpper(),
                    pi => pi.Name.ToUpper(),
                    (p, pi) => (p, pi))
                .Where(t => t.pi.CustomAttributes.Any(ca => ca.AttributeType == typeof(JsonIgnoreAttribute)))
                .Select(t => t.p)
                .Concat(propertyInfosIntroducedByCastleCoreProxy)
                .DistinctBy(p => p.Name)
                .ToArray();

            foreach (var propertyInfo in propertyInfosToRemove)
            {
                typeInfo.Properties.Remove(propertyInfo);
            }
        }

        return typeInfo;
    }
}