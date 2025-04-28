using System.Windows;

namespace WorkerClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        ViewModel = App.CurrentApp.GetRequiredService<MainViewModel>();
        InitializeComponent();
    }

    public MainViewModel ViewModel { get; }
}
