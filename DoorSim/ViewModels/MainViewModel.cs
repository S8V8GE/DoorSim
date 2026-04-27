using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Services;
using DoorSim.Views;
using System.Windows;
using System.Windows.Media;

namespace DoorSim.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISoftwireService _softwireService;
    private readonly Window _owner;

    [ObservableProperty]
    private bool isConnected;

    [ObservableProperty]
    private string statusText = "Not connected to Softwire";

    [ObservableProperty]
    private string mainMessage = "Use 'Connect' to connect to Softwire";

    [ObservableProperty]
    private Brush statusColor = new SolidColorBrush(Color.FromRgb(220, 80, 80));

    [ObservableProperty]
    private bool canConnect = true;

    public MainViewModel(ISoftwireService softwireService, Window owner)
    {
        _softwireService = softwireService;
        _owner = owner;
    }

    [RelayCommand]
    private async Task Connect()
    {
        var connectWindow = new ConnectWindow
        {
            Owner = _owner
        };

        var result = connectWindow.ShowDialog();

        if (result != true)
        {
            return;
        }

        StatusText = "Connecting to Softwire...";
        MainMessage = "Connecting...";

        var success = await _softwireService.LoginAsync(
            connectWindow.Hostname,
            "admin",
            connectWindow.Password);

        if (!success)
        {
            IsConnected = false;
            CanConnect = true;
            StatusText = "Not connected to Softwire";
            StatusColor = new SolidColorBrush(Color.FromRgb(220, 80, 80));
            MainMessage = "Unable to connect. Check the password, hostname, and Softwire status.";
            return;
        }

        IsConnected = true;
        CanConnect = false;
        StatusText = "Connected to Softwire";
        StatusColor = new SolidColorBrush(Color.FromRgb(40, 200, 120));
        MainMessage = "Connected. Select a door to begin."; 
    }
}