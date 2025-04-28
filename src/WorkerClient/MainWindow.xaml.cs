using System.Windows;

namespace WorkerClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = App.CurrentApp.GetRequiredService<MainViewModel>();
        Task.Run(async () => await ViewModel.LoadDataAsync());
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
