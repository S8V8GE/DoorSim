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

    /*
      #############################################################################
               Dependencies (services and child ViewModels - UI references)
      #############################################################################
    */

    // Service used to communicate with Softwire (HTTP API)
    private readonly ISoftwireService _softwireService;

    // Reference to the main window (used to own popup dialogs)
    private readonly Window _owner;

    // Service used to retrieve cardholders from the Directory SQL database
    private readonly CardholderSqlService _cardholderSqlService = new CardholderSqlService();


    // Child ViewModels bound to UI panels:

    // ViewModel for the cardholders panel
    public CardholdersViewModel Cardholders { get; } = new CardholdersViewModel();

    // ViewModel for the door selector and door display area
    public DoorsViewModel Doors { get; } = new DoorsViewModel();


    /*
      #############################################################################
                                       Timers
      #############################################################################
    */

    // Timer for connection + slow refresh every 3 seconds (doors + cardholders)
    private DispatcherTimer? _connectionTimer;

    // Timer for selected door + hardware state (fast polling every 1 second) - hoping I dont either melt the VM or crash Softwire... If this comment still exists all was ok!)
    private DispatcherTimer? _selectedDoorTimer;


    /*
     #############################################################################
                             UI State (bound to the View)
     #############################################################################
   */

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


    /*
     #############################################################################
                                  Constructor
     #############################################################################
   */

    // Initialises the ViewModel with required services
    public MainViewModel(ISoftwireService softwireService, Window owner)
    {
        _softwireService = softwireService;
        _owner = owner;
    }


    /*
     #############################################################################
                            Refresh helpers/methods
     #############################################################################
   */

    // Refreshes the cardholder list from SQL and updates the Cardholders panel
    private async Task RefreshCardholdersAsync()
    {
        var cards = await _cardholderSqlService.GetCardholdersAsync();

        Cardholders.LoadCardholders(cards);
    }

    // Refreshes doors from Softwire and returns count
    private async Task<int> RefreshDoorsAsync()
    {
        var doors = await _softwireService.GetDoorsAsync();

        Doors.LoadDoors(doors);

        return doors.Count;
    }


    /*
     #############################################################################
                       Connection Monitoring (slow loop)
     #############################################################################
   */

    // Starts a timer that checks connection status and refreshes doors/cardholders every 3 seconds
    private void StartConnectionMonitoring()
    {
        _connectionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
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
                // Connection is still valid, so refresh live data (cardholders from SQL and doors from Softwire)
                await RefreshCardholdersAsync();

                var doorCount = await RefreshDoorsAsync();

                if (doorCount == 0)
                {
                    MainMessage = "Connected to Softwire, but no doors are configured. Please create a door in Config Tool, using Softwire simulated hardware.";
                }
                else
                {
                    MainMessage = $"Connected to Softwire and loaded {doorCount} doors, select a door to begin.";
                }
            }
        };

        _connectionTimer.Start();
    }


    /*
     #############################################################################
                      Selected Door Monitoring (fast loop)
     #############################################################################
   */

    // Starts a fast timer to refresh the selected door state every second
    private void StartSelectedDoorMonitoring()
    {
        _selectedDoorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _selectedDoorTimer.Tick += async (s, e) =>
        {
            if (!IsConnected || Doors.SelectedDoor == null)
                return;

            // For now, refresh all doors and find the selected one.
            // Later this should/could be replaced with a dedicated GetDoorByIdAsync call.
            var updatedDoors = await _softwireService.GetDoorsAsync();

            var updated = updatedDoors
                .FirstOrDefault(d => d.Id == Doors.SelectedDoor.Id);

            if (updated != null)
            {
                var isOpen = false;
                var isShunted = false;

                if (!string.IsNullOrWhiteSpace(Doors.SelectedDoor.DoorSensorDevicePath))
                {
                    var sensorState = await _softwireService.GetInputStateAsync(
                        Doors.SelectedDoor.DoorSensorDevicePath);

                    if (sensorState != null)
                    {
                        // Preserve stable behaviour:
                        // if shunted, ignore Active so the UI does not flicker.
                        isShunted = sensorState.IsShunted;

                        if (!isShunted)
                        {
                            isOpen = sensorState.Active;
                        }
                    }
                }

                // Read In REX live state from Softwire.
                // Uses the same input-state pattern as the door sensor.
                if (!string.IsNullOrWhiteSpace(Doors.SelectedDoor.RexSideInDevicePath))
                {
                    var inRexState = await _softwireService.GetInputStateAsync(
                        Doors.SelectedDoor.RexSideInDevicePath);

                    if (inRexState != null)
                    {
                        // Preserve stable behaviour:
                        // if shunted, ignore Active so the UI does not flicker.
                        var inRexIsShunted = inRexState.IsShunted;
                        var inRexIsActive = inRexIsShunted ? false : inRexState.Active;

                        Doors.UpdateInRexState(inRexIsActive, inRexIsShunted);
                    }
                }

                // Read Out REX live state from Softwire.
                // Uses the same input-state pattern as the In REX.
                if (!string.IsNullOrWhiteSpace(Doors.SelectedDoor.RexSideOutDevicePath))
                {
                    var outRexState = await _softwireService.GetInputStateAsync(
                        Doors.SelectedDoor.RexSideOutDevicePath);

                    if (outRexState != null)
                    {
                        // Preserve stable behaviour:
                        // if shunted, ignore Active so the UI does not flicker.
                        var outRexIsShunted = outRexState.IsShunted;
                        var outRexIsActive = outRexIsShunted ? false : outRexState.Active;

                        Doors.UpdateOutRexState(outRexIsActive, outRexIsShunted);
                    }
                }

                // Read No-side REX live state from Softwire.
                if (!string.IsNullOrWhiteSpace(Doors.SelectedDoor.RexNoSideDevicePath))
                {
                    var noSideRexState = await _softwireService.GetInputStateAsync(
                        Doors.SelectedDoor.RexNoSideDevicePath);

                    if (noSideRexState != null)
                    {
                        var noSideRexIsShunted = noSideRexState.IsShunted;
                        var noSideRexIsActive = noSideRexIsShunted ? false : noSideRexState.Active;

                        Doors.UpdateNoSideRexState(noSideRexIsActive, noSideRexIsShunted);
                    }
                }

                Doors.UpdateSelectedDoorState(updated.DoorIsLocked, isOpen, isShunted);
            }
            else
            {
                Doors.SelectedDoor = null;
            }
        };

        _selectedDoorTimer.Start();
    }


    /*
     #############################################################################
                           Commands (user actions)
     #############################################################################
   */

    // Opens the connection dialog, attempts to log in to Softwire, and updates the UI based on success or failure.
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
            // Load cardholders from SQL and pass them to the CardholdersViewModel.
            await RefreshCardholdersAsync();

            // Load doors from Softwire and pass them to the DoorsViewModel.
            var doorCount = await RefreshDoorsAsync();

            if (doorCount == 0)
            {
                MainMessage = "Connected to Softwire, but no doors are configured. Please create a door in Config Tool, using Softwire simulated hardware.";
            }
            else
            {
                MainMessage = $"Connected to Softwire and loaded {doorCount} doors, select a door to begin.";
            }
        }
        catch (Exception ex)
        {
            // If SQL fails, show the error in the main window so we can diagnose it.
            MainMessage = $"SQL error: {ex.Message}";
        }

        // Start slow refresh loop (connection + doors + cardholders) - this one will check every 3 seconds that the connection to Softwire is still alive, and updates the cardholders and doors if it is. If the connection is lost, it resets the UI to the disconnected state.
        StartConnectionMonitoring();
        // Start fast refresh loop (selected door + hardware state) -   this one will check every 1 seconds for updates to the selected door (e.g., if it was locked/unlocked from another client or the Config Tool), and updates the door state in the UI accordingly.
        StartSelectedDoorMonitoring();
    }

}