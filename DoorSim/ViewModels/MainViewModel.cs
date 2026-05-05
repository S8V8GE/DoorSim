using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Models;
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

    // Centralised application sound service. Used here for access granted / denied decision sounds because MainViewModel is where Softwire access decisions are detected.
    private readonly SoundService _soundService = new SoundService();


    // Child ViewModels bound to UI panels:

    // ViewModel for the cardholders panel
    public CardholdersViewModel Cardholders { get; } = new CardholdersViewModel();

    // ViewModel for the door selector and door display area
    public DoorsViewModel Doors { get; } = new DoorsViewModel();

    // ViewModel for the Two Door View.
    //
    // This wraps the shared door/cardholder ViewModels for now.
    // Later it will own independent left/right selected-door state.
    public TwoDoorViewModel TwoDoor { get; }

    // Used by the View menu to show a checkmark next to Single Door.
    public bool IsSingleDoorViewSelected => CurrentViewMode == "SingleDoor";

    // Used by the View menu to show a checkmark next to Two Door.
    public bool IsTwoDoorViewSelected => CurrentViewMode == "TwoDoor";


    /*
      #############################################################################
                                       Timers
      #############################################################################
    */

    // Timer for connection + slow refresh every 3 seconds (doors + cardholders)
    private DispatcherTimer? _connectionTimer;

    // Timer for selected door + hardware state (Runs every 1 second to keep door sensor, REX, and breakglass state responsive)... will this melt my VM??
    private DispatcherTimer? _selectedDoorTimer;

    // Timer for polling reader state more quickly (Reader LEDs can flash briefly, so readers are polled faster than the rest of the door hardware).
    private DispatcherTimer? _readerTimer;

    // Prevents selected-door polling from overlapping itself if a refresh takes longer than the timer interval.
    private bool _selectedDoorRefreshInProgress;

    // Prevents reader polling from overlapping itself if Softwire responds slowly.
    // Without this, old reader-state responses can race newer ones and cause
    // online/offline flicker.
    private bool _readerRefreshInProgress;

    // Tracks the most recent reader action so we can match it against Softwire's LastDecision in the refreshed door JSON.
    private string _pendingDecisionReaderPath = string.Empty;
    private DateTime? _pendingDecisionSentUtc;
    private bool _pendingDecisionIsInReader;

    // The cardholder involved in the pending reader action. Used for future easter egg sounds, such as Simpson granted/denied audio.
    private Cardholder? _pendingDecisionCardholder;

    // The specific door panel state that sent the pending reader action.
    // This is required for Two Door View so feedback appears under the correct reader.
    private DoorsViewModel? _pendingDecisionTargetDoors;

    // Suppresses reader LED alert sounds briefly after an access decision.
    //
    // Reader LEDs often change as part of normal granted/denied behaviour.
    // We do not want those LED changes to also play reader-alert sounds.
    // Reader-alert sounds are intended for non-access-decision events, such as door forced / door held open behaviour.
    private DoorsViewModel? _readerAlertSuppressedTargetDoors;
    private DateTime? _readerAlertSuppressedUntilUtc;


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

    // Controls whether the Connect menu option is enabled. Disabled once a successful connection is established
    [ObservableProperty]
    private bool canConnect = true;

    // Tracks which main simulator view is currently selected (Single Door is the default view).
    [ObservableProperty]
    private string currentViewMode = "SingleDoor";


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

        // Two Door View uses the existing shared door and cardholder ViewModels for now.
        // Later, this will be expanded so each side has its own selected door state.
        TwoDoor = new TwoDoorViewModel(Doors, Cardholders);
    }


    /*
     #############################################################################
                       Refresh helpers/methods and View updates
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

        // Keep the Two Door View's independent left/right door lists in sync
        // with the latest doors returned by Softwire.
        TwoDoor.LoadDoors(doors);

        return doors.Count;
    }

    // Records that a card or PIN was sent to a reader. The selected-door polling loop uses this to match the next Softwire LastDecision back to the reader that triggered it.
    public void RegisterPendingReaderDecision(string readerPath, bool isInReader, DoorsViewModel targetDoors, Cardholder? cardholder)
    {
        if (string.IsNullOrWhiteSpace(readerPath))
            return;

        _pendingDecisionReaderPath = readerPath;
        _pendingDecisionSentUtc = DateTime.UtcNow;
        _pendingDecisionIsInReader = isInReader;
        _pendingDecisionTargetDoors = targetDoors;
        _pendingDecisionCardholder = cardholder;
    }

    // Returns true when reader LED change sounds should be suppressed for the supplied door panel.
    //
    // We suppress if:
    // - that panel has a pending access decision, or
    // - that panel has just completed an access decision and LEDs may still be
    //   returning to normal.
    public bool ShouldSuppressReaderLedSound(DoorsViewModel targetDoors)
    {
        if (_pendingDecisionSentUtc != null &&
            _pendingDecisionTargetDoors == targetDoors)
        {
            return true;
        }

        if (_readerAlertSuppressedTargetDoors == targetDoors &&
            _readerAlertSuppressedUntilUtc != null &&
            DateTime.UtcNow < _readerAlertSuppressedUntilUtc.Value)
        {
            return true;
        }

        return false;
    }

    // Checks whether Softwire has reported a LastDecision after a reader action.
    //
    // In Two Door View, feedback must appear under the reader that sent the action.
    // Therefore we store the specific DoorsViewModel target when the swipe/PIN is sent,
    // and only apply the feedback when that same target is being refreshed.
    private async Task CheckPendingReaderDecisionAsync(DoorsViewModel targetDoors, SoftwireDoor updatedDoor)
    {
        if (_pendingDecisionSentUtc == null)
            return;

        if (_pendingDecisionTargetDoors != targetDoors)
            return;

        // If Softwire has not reported a decision yet, keep waiting.
        if (!updatedDoor.LastDecisionGranted && !updatedDoor.LastDecisionDenied)
            return;

        if (updatedDoor.LastDecisionGranted)
        {
            if (_pendingDecisionIsInReader)
                _ = targetDoors.ShowInReaderDecisionFeedbackAsync("Access granted", true);
            else
                _ = targetDoors.ShowOutReaderDecisionFeedbackAsync("Access granted", true);

            // Access decision audio happens here, not from reader LED changes.
            _soundService.PlayAccessGranted(_pendingDecisionCardholder);
        }
        else if (updatedDoor.LastDecisionDenied)
        {
            if (_pendingDecisionIsInReader)
                _ = targetDoors.ShowInReaderDecisionFeedbackAsync("Access denied", false);
            else
                _ = targetDoors.ShowOutReaderDecisionFeedbackAsync("Access denied", false);

            // Access denied gets its own warning pattern.
            _ = _soundService.PlayAccessDeniedAsync(_pendingDecisionCardholder);
        }

        // Suppress reader LED alert sounds briefly after the decision.
        // This prevents normal access LED changes from causing extra alert beeps.
        _readerAlertSuppressedTargetDoors = targetDoors;
        _readerAlertSuppressedUntilUtc = DateTime.UtcNow.AddSeconds(2);

        // Clear pending action so this decision is only handled once.
        _pendingDecisionReaderPath = string.Empty;
        _pendingDecisionSentUtc = null;
        _pendingDecisionTargetDoors = null;
        _pendingDecisionCardholder = null;
    }

    // Refreshes View menu checkmarks when the selected view mode changes.
    partial void OnCurrentViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSingleDoorViewSelected));
        OnPropertyChanged(nameof(IsTwoDoorViewSelected));
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

    // Refreshes one selected door state target.
    //
    // This method is the reusable version of the old single-door polling logic.
    // It can update:
    // - the normal Single Door View state
    // - the left door state in Two Door View
    // - the right door state in Two Door View
    //
    // It intentionally accepts a DoorsViewModel target so each door panel can
    // maintain its own selected door and live device state.
    private async Task RefreshSelectedDoorStateAsync(DoorsViewModel targetDoors)
    {
        if (!IsConnected || targetDoors.SelectedDoor == null)
            return;

        // For now, refresh all doors and find the selected one.
        // Later this could be replaced with a dedicated GetDoorByIdAsync call.
        var updatedDoors = await _softwireService.GetDoorsAsync();

        var updated = updatedDoors
            .FirstOrDefault(d => d.Id == targetDoors.SelectedDoor.Id);

        if (updated != null)
        {
            var isOpen = false;
            var isShunted = false;

            // Door sensor live state from Softwire.
            if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.DoorSensorDevicePath))
            {
                var sensorState = await _softwireService.GetInputStateAsync(
                    targetDoors.SelectedDoor.DoorSensorDevicePath);

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

            // Readers are polled separately in StartReaderMonitoring() using a faster timer.
            // DO NOT update reader state here, otherwise the reader LED/status can flicker.

            // Read In REX live state from Softwire.
            if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.RexSideInDevicePath))
            {
                var inRexState = await _softwireService.GetInputStateAsync(
                    targetDoors.SelectedDoor.RexSideInDevicePath);

                if (inRexState != null)
                {
                    var inRexIsShunted = inRexState.IsShunted;
                    var inRexIsActive = inRexIsShunted ? false : inRexState.Active;

                    targetDoors.UpdateInRexState(inRexIsActive, inRexIsShunted);
                }
            }

            // Read Out REX live state from Softwire.
            if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.RexSideOutDevicePath))
            {
                var outRexState = await _softwireService.GetInputStateAsync(
                    targetDoors.SelectedDoor.RexSideOutDevicePath);

                if (outRexState != null)
                {
                    var outRexIsShunted = outRexState.IsShunted;
                    var outRexIsActive = outRexIsShunted ? false : outRexState.Active;

                    targetDoors.UpdateOutRexState(outRexIsActive, outRexIsShunted);
                }
            }

            // Read No-side REX live state from Softwire.
            if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.RexNoSideDevicePath))
            {
                var noSideRexState = await _softwireService.GetInputStateAsync(
                    targetDoors.SelectedDoor.RexNoSideDevicePath);

                if (noSideRexState != null)
                {
                    var noSideRexIsShunted = noSideRexState.IsShunted;
                    var noSideRexIsActive = noSideRexIsShunted ? false : noSideRexState.Active;

                    targetDoors.UpdateNoSideRexState(noSideRexIsActive, noSideRexIsShunted);
                }
            }

            // Read Breakglass live state from Softwire.
            if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.BreakGlassDevicePath))
            {
                var breakGlassState = await _softwireService.GetInputStateAsync(
                    targetDoors.SelectedDoor.BreakGlassDevicePath);

                if (breakGlassState != null)
                {
                    var breakGlassIsShunted = breakGlassState.IsShunted;
                    var breakGlassIsActive = breakGlassIsShunted ? false : breakGlassState.Active;

                    targetDoors.UpdateBreakGlassState(breakGlassIsActive, breakGlassIsShunted);
                }
            }

            await CheckPendingReaderDecisionAsync(targetDoors, updated);

            targetDoors.UpdateSelectedDoorState(updated.DoorIsLocked, isOpen, isShunted);
        }
        else
        {
            targetDoors.SelectedDoor = null;
        }
    }

    // Starts a fast timer to refresh the selected door state every second
    private void StartSelectedDoorMonitoring()
    {
        _selectedDoorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _selectedDoorTimer.Tick += async (s, e) =>
        {
            if (!IsConnected)
                return;

            if (_selectedDoorRefreshInProgress)
                return;

            try
            {
                _selectedDoorRefreshInProgress = true;

                if (CurrentViewMode == "TwoDoor")
                {
                    await RefreshSelectedDoorStateAsync(TwoDoor.LeftDoorPanel.DoorState);
                    await RefreshSelectedDoorStateAsync(TwoDoor.RightDoorPanel.DoorState);
                }
                else
                {
                    await RefreshSelectedDoorStateAsync(Doors);
                }
            }
            finally
            {
                _selectedDoorRefreshInProgress = false;
            }
        };

        _selectedDoorTimer.Start();
    }


    /*
     #############################################################################
                      Reader Monitoring (very fast loop)
     #############################################################################
   */

    // Refreshes reader state for one selected door target.
    //
    // This is the reusable version of the original single-door reader polling.
    // It can update:
    // - Single Door View reader state
    // - Left door reader state in Two Door View
    // - Right door reader state in Two Door View
    //
    // Reader state is kept separate from the slower selected-door polling because
    // reader LEDs can change very briefly during access granted / denied events.
    private async Task RefreshReaderStateAsync(DoorsViewModel targetDoors)
    {
        if (!IsConnected || targetDoors.SelectedDoor == null)
            return;

        // Read In Reader live state from Softwire.
        if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.ReaderSideInDevicePath))
        {
            var inReaderState = await _softwireService.GetReaderStateAsync(
                targetDoors.SelectedDoor.ReaderSideInDevicePath);

            if (inReaderState != null)
            {
                targetDoors.UpdateInReaderState(
                    inReaderState.Online,
                    inReaderState.IsShunted,
                    inReaderState.LedColor);
            }
        }

        // Read Out Reader live state from Softwire.
        if (!string.IsNullOrWhiteSpace(targetDoors.SelectedDoor.ReaderSideOutDevicePath))
        {
            var outReaderState = await _softwireService.GetReaderStateAsync(
                targetDoors.SelectedDoor.ReaderSideOutDevicePath);

            if (outReaderState != null)
            {
                targetDoors.UpdateOutReaderState(
                    outReaderState.Online,
                    outReaderState.IsShunted,
                    outReaderState.LedColor);
            }
        }
    }

    // Polls reader state faster than the rest of the door hardware.
    //
    // This helps catch short reader LED changes such as granted/denied flashes.
    // In Two Door View, both door panels need their own reader polling.
    private void StartReaderMonitoring()
    {
        _readerTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };

        _readerTimer.Tick += async (s, e) =>
        {
            if (!IsConnected)
                return;

            if (_readerRefreshInProgress)
                return;

            try
            {
                _readerRefreshInProgress = true;

                if (CurrentViewMode == "TwoDoor")
                {
                    await RefreshReaderStateAsync(TwoDoor.LeftDoorPanel.DoorState);
                    await RefreshReaderStateAsync(TwoDoor.RightDoorPanel.DoorState);
                }
                else
                {
                    await RefreshReaderStateAsync(Doors);
                }
            }
            finally
            {
                _readerRefreshInProgress = false;
            }
        };

        _readerTimer.Start();
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

        // Start slow refresh loop: connection, doors, and cardholders.
        StartConnectionMonitoring();
        // Start fast refresh loop: selected door sensor, REX, and breakglass.
        StartSelectedDoorMonitoring();
        // Start very fast refresh loop: reader state and LED changes only.
        StartReaderMonitoring();
    }

    // Switches the main content area to Single Door View.
    [RelayCommand]
    private void ShowSingleDoorView()
    {
        CurrentViewMode = "SingleDoor";
    }

    // Switches the main content area to Two Door View.
    [RelayCommand]
    private void ShowTwoDoorView()
    {
        // Carry the current Single Door selection into the left side of Two Door View.
        // The right side intentionally starts empty so the trainer chooses the second door.
        TwoDoor.PrepareFromSingleDoorSelection(Doors.SelectedDoor);

        CurrentViewMode = "TwoDoor";
    }

}