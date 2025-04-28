using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using WorkerClient.Models;

namespace WorkerClient;

public partial class MainViewModel : ObservableValidator
{
    private readonly SynchronisationAction _synchronisationAction;
    private readonly UserNameStore _userNameStore;
    private readonly LoadDataMainViewModelQuery _loadDataMainViewModelQuery;

    public MainViewModel(
        SynchronisationAction synchronisationAction,
        UserNameStore userNameStore,
        LoadDataMainViewModelQuery loadDataMainViewModelQuery)
    {
        _synchronisationAction = synchronisationAction;
        _userNameStore = userNameStore;
        _loadDataMainViewModelQuery = loadDataMainViewModelQuery;
        UserName = _userNameStore.UserName;
    }

    [ObservableProperty]
    public partial string UserName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSynchronise))]
    public partial bool IsSynchronising { get; private set; }

    public bool CanSynchronise => !IsSynchronising;

    public ObservableCollection<OrderViewModel> Orders { get; } = new();

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanSynchronise))]
    public async Task SynchroniseAsync()
    {
        IsSynchronising = true;

        var pushResult = await _synchronisationAction.PushAllAsync();
        if (pushResult is null)
        {
            MessageBox.Show(App.CurrentApp.MainWindow!, "Push: could not reach server", "Datasync error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var pullResult = await _synchronisationAction.PullAllAsync();
        if (pullResult is null)
        {
            MessageBox.Show(App.CurrentApp.MainWindow!, "Pull: could not reach server", "Datasync error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await LoadDataAsync();
        IsSynchronising = false;
    }

    public async Task LoadDataAsync()
    {
        var query = await _loadDataMainViewModelQuery.LoadAsync();

        _ = App.CurrentApp.MainWindow!.Dispatcher.BeginInvoke(() =>
        {
            Orders.Clear();

            foreach (var order in query)
            {
                Orders.Add(order);
            }
        });
    }

    partial void OnUserNameChanged(string value)
    {
        if (value is null or "")
        {
            return;
        }

        _userNameStore.UserName = value;
    }
}

public class LoadDataMainViewModelQuery
{
    private readonly IDbContextFactory<ClientDataContext> _contextFactory;
    private readonly CompleteOrderAction _completeOrderAction;
    private readonly CancelOrderAction _cancelOrderAction;

    public LoadDataMainViewModelQuery(
        IDbContextFactory<ClientDataContext> contextFactory,
        CompleteOrderAction completeOrderAction,
        CancelOrderAction cancelOrderAction)
    {
        _contextFactory = contextFactory;
        _completeOrderAction = completeOrderAction;
        _cancelOrderAction = cancelOrderAction;
    }

    public async ValueTask<IReadOnlyCollection<OrderViewModel>> LoadAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        return context.Orders
            .AsNoTracking()
            .AsEnumerable()
            .Select(o => new OrderViewModel(_completeOrderAction, _cancelOrderAction)
            {
                Id = o.Id,
                CreatedAt = o.CreatedAt,
                IsCanceled = o.Status is OrderStatus.Cancelled,
                IsCompleted = o.Status is OrderStatus.Delivered,
                CustomerName = o.Customer.Name
            })
            .ToArray();
    }
}

public partial class OrderViewModel : ObservableValidator
{
    private readonly CompleteOrderAction _completeOrderAction;
    private readonly CancelOrderAction _cancelOrderAction;

    public OrderViewModel(CompleteOrderAction completeOrderAction, CancelOrderAction cancelOrderAction)
    {
        _completeOrderAction = completeOrderAction;
        _cancelOrderAction = cancelOrderAction;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    public partial string Id { get; set; } = null!;

    [ObservableProperty]
    public partial string CustomerName { get; set; } = null!;

    [ObservableProperty]
    public partial DateTimeOffset CreatedAt { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    public partial bool IsCompleted { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanComplete))]
    [NotifyPropertyChangedFor(nameof(CanCancel))]
    public partial bool IsCanceled { get; set; }

    public bool CanComplete => !IsCompleted && !IsCanceled && Id is { Length: > 0 };

    public bool CanCancel => !IsCanceled && !IsCompleted && Id is { Length: > 0 };

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanComplete))]
    public async Task CompleteAsync()
    {
        await _completeOrderAction.CompleteAsync(Id);
    }

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanCancel))]
    public async Task CancelAsync()
    {
        await _cancelOrderAction.CancelAsync(Id);
    }
}

public class CancelOrderAction
{
    private readonly IDbContextFactory<ClientDataContext> _contextFactory;

    public CancelOrderAction(IDbContextFactory<ClientDataContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async ValueTask<bool> CancelAsync(string orderId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var order = await context.Orders.FirstAsync(o => o.Id == orderId);

        if (order is { Status: OrderStatus.Cancelled or OrderStatus.Delivered })
        {
            return false;
        }

        order.Status = OrderStatus.Cancelled;
        await context.SaveChangesAsync();
        return true;
    }
}

public class CompleteOrderAction
{
    private readonly IDbContextFactory<ClientDataContext> _contextFactory;

    public CompleteOrderAction(IDbContextFactory<ClientDataContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async ValueTask<bool> CompleteAsync(string orderId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();

        var order = await context.Orders.FirstAsync(o => o.Id == orderId);

        if (order is { Status: OrderStatus.Cancelled or OrderStatus.Delivered })
        {
            return false;
        }

        order.Status = OrderStatus.Delivered;
        await context.SaveChangesAsync();
        return true;
    }
}
