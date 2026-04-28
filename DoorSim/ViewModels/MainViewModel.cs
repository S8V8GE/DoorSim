using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Services;
using DoorSim.Views;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DoorSim.ViewModels;

// ViewModel for the main application window.
//
// Responsible for:
// - Managing connection state to Softwire
// - Providing UI text and status (connected / not connected)
// - Handling top-level user actions (e.g., Connect)
//
// This class acts as the bridge between:
// - The View (MainWindow.xaml)
// - The Service layer (SoftwireService)

public partial class MainViewModel : ObservableObject
{
    // --- Dependencies (external services and UI references) ---

    // Service used to communicate with Softwire (HTTP API)
    private readonly ISoftwireService _softwireService;

    // Reference to the main window (used to own popup dialogs)
    private readonly Window _owner;

    // Timer used to periodically check Softwire connection
    private DispatcherTimer? _connectionTimer;

    // Service used to retrieve cardholders from the Directory SQL database
    private readonly CardholderSqlService _cardholderSqlService = new CardholderSqlService();

    // --- UI State (bound to the View) ---
    // These properties are automatically updated in the UI via data binding.

    // ViewModel for the cardholders panel
    public CardholdersViewModel Cardholders { get; } = new CardholdersViewModel();

    // Indicates whether the application is currently connected to Softwire
    [ObservableProperty]
    private bool isConnected;

    // Text displayed in the status bar (e.g., connected / not connected)
    [ObservableProperty]
    private string statusText = "Not connected to Softwire";

    // Central message shown in the main content area (guides the user)
    [ObservableProperty]
    private string mainMessage = "Use 'Connect' to connect to Softwire";

    // Colour of the status text (red = disconnected, green = connected)
    [ObservableProperty]
    private Brush statusColor = new SolidColorBrush(Color.FromRgb(220, 80, 80));

    // Controls whether the Connect menu option is enabled
    // Disabled once a successful connection is established
    [ObservableProperty]
    private bool canConnect = true;

    // --- Constructor ---
    // Injects required services and initialises the ViewModel.
    // Store references to service and owning window
    public MainViewModel(ISoftwireService softwireService, Window owner)
    {
        _softwireService = softwireService;
        _owner = owner;
    }

    // Refreshes the cardholder list from SQL and updates the Cardholders panel
    private async Task RefreshCardholdersAsync()
    {
        var cards = await _cardholderSqlService.GetCardholdersAsync();

        Cardholders.LoadCardholders(cards);
    }

    // Starts a timer that checks connection status every 5 seconds
    private void StartConnectionMonitoring()
    {
        _connectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };

        _connectionTimer.Tick += async (s, e) =>
        {
            // Ask service if connection is still valid
            var stillConnected = await _softwireService.CheckConnectionAsync();

            if (!stillConnected)
            {
                // Stop timer to avoid repeated checks
                _connectionTimer?.Stop();

                // Reset UI to disconnected state
                IsConnected = false;
                CanConnect = true;

                StatusText = "Connection lost";
                StatusColor = new SolidColorBrush(Color.FromRgb(220, 80, 80));

                MainMessage = "Connection lost. Use 'Connect' to reconnect.";
            }
            else
            {
                // Connection is still valid, so refresh cardholders from SQL
                await RefreshCardholdersAsync();
            }
        };

        _connectionTimer.Start();
    }

    // --- Commands (triggered by user actions in the UI) ---
    // Opens the connection dialog, attempts to log in to Softwire,
    // and updates the UI based on success or failure.
    [RelayCommand]
    private async Task Connect()
    {
        // Create and display the connection dialog
        var connectWindow = new ConnectWindow
        {
            Owner = _owner
        };

        var result = connectWindow.ShowDialog();

        // If user cancels the dialog, do nothing
        if (result != true)
        {
            return;
        }

        // Attempt login via Softwire service
        StatusText = "Connecting to Softwire...";
        MainMessage = "Connecting...";

        var success = await _softwireService.LoginAsync(
            connectWindow.Hostname,
            "admin",
            connectWindow.Password);

        // Login failed → reset UI to disconnected state
        if (!success)
        {
            IsConnected = false;
            CanConnect = true;
            StatusText = "Not connected to Softwire";
            StatusColor = new SolidColorBrush(Color.FromRgb(220, 80, 80));
            MainMessage = "Unable to connect. Check the password, hostname, and Softwire status.";
            return;
        }

        // Login successful → update UI to connected state
        IsConnected = true;
        CanConnect = false;
        StatusText = "Connected to Softwire";
        StatusColor = new SolidColorBrush(Color.FromRgb(40, 200, 120));
        MainMessage = "Connected. Loading cardholders...";

        try
        {
            // Query SQL and show how many cardholders were found.
            await RefreshCardholdersAsync();

            MainMessage = "Connected. Select a door to begin.";
        }
        catch (Exception ex)
        {
            // If SQL fails, show the error in the main window so we can diagnose it.
            MainMessage = $"SQL error: {ex.Message}";
        }

        // Start connection monitoring only after the initial load attempt
        StartConnectionMonitoring();
    }
}