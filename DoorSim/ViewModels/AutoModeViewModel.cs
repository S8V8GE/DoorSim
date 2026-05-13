using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace DoorSim.ViewModels;

// ViewModel for Auto Mode.
// ------------------------
// Auto Mode generates automated access-control activity against a connected Softwire simulation environment.
//
// It can currently generate:
//      - normal access events using readers/cardholders or REX inputs,
//      - door forced events,
//      - door held-open events using readers/cardholders or REX inputs.
//
// Auto Mode is designed for training, demo, and stress-test scenarios.
// It keeps its own event log, running counters, retry guard, and held-door reservation tracking so automated events do not interfere with each other.
// It is NOT intelligent and does not try to "learn" the environment or optimise its selections.
// It simply generates random events based on the selected profile and retries when it encounters an invalid configuration for the current event.
public partial class AutoModeViewModel : ObservableObject
{
    // Raised when Auto Mode detects that Softwire/API communication has failed.
    //
    // MainViewModel listens for this so it can perform the same safe disconnect behaviour used by Manual Mode:
    //      - stop polling,
    //      - mark the app disconnected,
    //      - re-enable Connect,
    //      - return the trainer to a safe reconnect state.
    public event Action<string>? ConnectionLost;


    /*
      #############################################################################
                          Simulation constants and state
      #############################################################################
    */

    // Used to stop the running simulation loop safely.
    private CancellationTokenSource? _simulationCancellation;

    // Random number generator used for delays and event type selection.
    private readonly Random _random = new Random();

    // Maximum number of failed/retried attempts allowed in a row.
    // This prevents Auto Mode from running forever if the environment cannot produce valid events, for example:
    //      - no suitable doors exist,
    //      - all suitable doors are in maintenance,
    //      - no suitable cardholders exist,
    //      - Card + PIN readers exist but no cardholders have PINs,
    //      - held-open capable doors are not configured.
    private const int MaxConsecutiveFailedAttempts = 50;

    // Counts failed/retried attempts since the last successfully executed event.
    // This is deliberately consecutive rather than total. A long simulation may have occasional retries and still be healthy.
    // Fifty failures in a row means Auto Mode is almost certainly stuck and should stop itself safely.
    private int _consecutiveFailedAttempts;

    // Tracks doors temporarily reserved by Auto Mode.
    // Held-open events deliberately leave a door sensor open while Softwire waits to generate the Door Held event.
    // During that time, the same door must not be used by another Normal, Forced, or Held event.
    //      - Key   = Softwire door Id
    //      - Value = reservation details for logging/debugging
    private readonly Dictionary<string, AutoDoorReservation> _reservedDoors = new();

    // Tracks background cleanup tasks for Held events.
    // Held events deliberately leave a door sensor open long enough for Softwire to generate a door-held-open event.
    // The main simulation loop continues meanwhile, so cleanup happens in the background.
    private readonly List<Task> _heldCleanupTasks = new();


    /*
      #############################################################################
                      Simulation Dependencies / Callbacks
      #############################################################################
    */

    // Auto Mode does not own SoftwireService directly.
    // MainViewModel owns the application services and passes Auto Mode a small set of safe callbacks.
    // This keeps AutoModeViewModel focused on simulation logic rather than login/session ownership.
    private Func<Task<List<SoftwireDoor>>>? _getDoorsAsync;
    private Func<IReadOnlyList<Cardholder>>? _getCardholders;
    private Func<string, Task<InputState?>>? _getInputStateAsync;
    private Func<string, string, Task<bool>>? _setInputStateAsync;
    private Func<string, string, int, Task<bool>>? _swipeRawAsync;
    private Func<string, int, int, Task<bool>>? _swipeWiegand26Async;

    // True when MainViewModel has supplied all callbacks required for real simulation.
    private bool HasSimulationDependencies =>
        _getDoorsAsync != null &&
        _getCardholders != null &&
        _getInputStateAsync != null &&
        _setInputStateAsync != null &&
        _swipeRawAsync != null &&
        _swipeWiegand26Async != null;


    /*
      #############################################################################
                                  Page Text
      #############################################################################
    */

    public string Title => "Auto Mode";

    public string Subtitle => "Automatic busy site simulation for training, demos, and stress testing.";


    /*
      #############################################################################
                                Simulation Settings
      #############################################################################
    */

    public ObservableCollection<string> DelayModes { get; } = new()
    {
        "Extreme",
        "Relaxed",
        "Custom"
    };

    [ObservableProperty]
    private string selectedDelayMode = "Relaxed";

    [ObservableProperty]
    private string minimumDelaySecondsText = "3";

    [ObservableProperty]
    private string maximumDelaySecondsText = "7";

    [ObservableProperty]
    private string numberOfEventsText = "100";

    // Parsed numeric value used by the simulation engine.
    // Safe to use only when validation has passed.
    private int NumberOfEventsValue => int.Parse(NumberOfEventsText);

    // Parsed minimum delay used by the simulation engine.
    // Safe to use only when validation has passed.
    private int MinimumDelaySecondsValue => int.Parse(MinimumDelaySecondsText);

    // Parsed maximum delay used by the simulation engine.
    // Safe to use only when validation has passed.
    private int MaximumDelaySecondsValue => int.Parse(MaximumDelaySecondsText);

    public ObservableCollection<string> EventProfiles { get; } = new()
    {
        "Normal operation",
        "Low anomalies",
        "Typical environment",
        "Elevated anomalies",
        "Fault / misuse"
    };

    [ObservableProperty]
    private string selectedEventProfile = "Typical environment";

    [ObservableProperty]
    private string globalPin = "1234";

    // Custom delay fields are editable only when Custom mode is selected and the simulation is stopped.
    public bool CanEditCustomDelay => IsCustomDelayMode && CanEditSettings;

    // User-facing validation message shown under the Auto Mode settings. Empty string means the current settings are valid.
    public string ValidationMessage
    {
        get
        {
            if (!int.TryParse(NumberOfEventsText, out var parsedNumberOfEvents))
                return "Events must be a number between 1 and 9999.";

            if (parsedNumberOfEvents < 1 || parsedNumberOfEvents > 9999)
                return "Events must be a number between 1 and 9999.";

            if (!int.TryParse(MinimumDelaySecondsText, out var parsedMinimumDelay))
                return "Minimum delay must be a number between 0 and 60 seconds.";

            if (parsedMinimumDelay < 0 || parsedMinimumDelay > 60)
                return "Minimum delay must be between 0 and 60 seconds.";

            if (!int.TryParse(MaximumDelaySecondsText, out var parsedMaximumDelay))
                return "Maximum delay must be a number between 0 and 120 seconds.";

            if (parsedMaximumDelay < 0 || parsedMaximumDelay > 120)
                return "Maximum delay must be between 0 and 120 seconds.";

            if (parsedMaximumDelay < parsedMinimumDelay)
                return "Maximum delay must be equal to or greater than minimum delay.";

            if (string.IsNullOrWhiteSpace(GlobalPin))
                return "Global PIN is required.";

            if (!Regex.IsMatch(GlobalPin, @"^\d{4,5}$"))
                return "Global PIN must be 4 or 5 digits.";

            return "";
        }
    }

    // Convenience property in case we later want to hide/show warning UI.
    public bool HasValidationMessage => !string.IsNullOrWhiteSpace(ValidationMessage);


    /*
      #############################################################################
                              Event Profile Percentages
      #############################################################################
    */

    // These percentages are used by GetRandomEventType(). They are statistical weights, not guarantees.
    // A short run of 10 events may not match the exact displayed percentages.

    // Percentage chance that the next generated event will be a normal access event.
    public int NormalEventPercentage
    {
        get
        {
            return SelectedEventProfile switch
            {
                "Normal operation" => 100,
                "Low anomalies" => 90,
                "Typical environment" => 80,
                "Elevated anomalies" => 70,
                "Fault / misuse" => 50,
                _ => 80
            };
        }
    }

    // Percentage chance that the next generated event will be a forced-door event.
    public int ForcedEventPercentage
    {
        get
        {
            return SelectedEventProfile switch
            {
                "Normal operation" => 0,
                "Low anomalies" => 5,
                "Typical environment" => 10,
                "Elevated anomalies" => 20,
                "Fault / misuse" => 25,
                _ => 10
            };
        }
    }

    // Percentage chance that the next generated event will be a held-open event.
    public int HeldEventPercentage
    {
        get
        {
            return SelectedEventProfile switch
            {
                "Normal operation" => 0,
                "Low anomalies" => 5,
                "Typical environment" => 10,
                "Elevated anomalies" => 10,
                "Fault / misuse" => 25,
                _ => 10
            };
        }
    }


    /*
      #############################################################################
                            Running State / counters / log
      #############################################################################
    */

    [ObservableProperty]
    private bool isSimulationRunning;

    [ObservableProperty]
    private string simulationStatus = "Idle";

    [ObservableProperty]
    private int completedEvents;

    [ObservableProperty]
    private int failedAttempts;

    [ObservableProperty]
    private int executedNormalEvents;

    [ObservableProperty]
    private int executedForcedEvents;

    [ObservableProperty]
    private int executedHeldEvents;

    public ObservableCollection<AutoSimulationLogEntry> LogEntries { get; } = new();


    /*
      #############################################################################
                              Generated Property Hooks
      #############################################################################
    */

    partial void OnSelectedDelayModeChanged(string value)
    {
        if (value == "Extreme")
        {
            MinimumDelaySecondsText = "0";
            MaximumDelaySecondsText = "0";
        }
        else if (value == "Relaxed")
        {
            MinimumDelaySecondsText = "3";
            MaximumDelaySecondsText = "7";
        }

        OnPropertyChanged(nameof(IsCustomDelayMode));
        OnPropertyChanged(nameof(CanEditCustomDelay));

        RefreshValidationState();
    }

    partial void OnSelectedEventProfileChanged(string value)
    {
        OnPropertyChanged(nameof(NormalEventPercentage));
        OnPropertyChanged(nameof(ForcedEventPercentage));
        OnPropertyChanged(nameof(HeldEventPercentage));
    }

    partial void OnIsSimulationRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSettings));
        OnPropertyChanged(nameof(CanEditCustomDelay));

        StartSimulationCommand.NotifyCanExecuteChanged();
        StopSimulationCommand.NotifyCanExecuteChanged();
    }

    public bool IsCustomDelayMode => SelectedDelayMode == "Custom";

    // Settings can only be edited while the simulation is stopped.
    public bool CanEditSettings => !IsSimulationRunning;

    partial void OnNumberOfEventsTextChanged(string value)
    {
        RefreshValidationState();
    }

    partial void OnMinimumDelaySecondsTextChanged(string value)
    {
        RefreshValidationState();
    }

    partial void OnMaximumDelaySecondsTextChanged(string value)
    {
        RefreshValidationState();
    }

    partial void OnGlobalPinChanged(string value)
    {
        RefreshValidationState();
    }

    // Refreshes validation-related UI whenever a setting changes.
    private void RefreshValidationState()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationMessage));

        StartSimulationCommand.NotifyCanExecuteChanged();
    }


    /*
      #############################################################################
                          Dependency Configuration
      #############################################################################
    */

    // Configures the callbacks Auto Mode needs in order to run real simulations.
    // MainViewModel owns the actual services and live data sources. Auto Mode only receives the specific actions it needs:
    //
    //      - load current doors,
    //      - read loaded cardholders,
    //      - read/set input state,
    //      - swipe card credentials,
    //      - send PIN values.
    //
    // This avoids making AutoModeViewModel responsible for Softwire login/session handling and keeps the design similar to manual mode/the interlocking controls.
    public void ConfigureSimulationDependencies(
        Func<Task<List<SoftwireDoor>>> getDoorsAsync,
        Func<IReadOnlyList<Cardholder>> getCardholders,
        Func<string, Task<InputState?>> getInputStateAsync,
        Func<string, string, Task<bool>> setInputStateAsync,
        Func<string, string, int, Task<bool>> swipeRawAsync,
        Func<string, int, int, Task<bool>> swipeWiegand26Async)
    {
        _getDoorsAsync = getDoorsAsync;
        _getCardholders = getCardholders;
        _getInputStateAsync = getInputStateAsync;
        _setInputStateAsync = setInputStateAsync;
        _swipeRawAsync = swipeRawAsync;
        _swipeWiegand26Async = swipeWiegand26Async;
    }


    /*
      #############################################################################
                                    Commands
      #############################################################################
    */

    // Handles unexpected Softwire/API failures during Auto Mode.
    //
    // This is different from a normal user Stop:
    //      - Stop is expected and uses OperationCanceledException.
    //      - Softwire/API failure means one of the callbacks threw unexpectedly.
    //
    // Cleanup is deliberately best-effort only. If Softwire is down, cleanup commands may also fail because Softwire cannot receive them.
    // In that situation the priority is to stop Auto Mode and return the application to a safe reconnect state instead of crashing.
    private void HandleAutoModeConnectionLost(Exception ex)
    {
        SimulationStatus = "Stopped";

        AddLog(
            level: "Error",
            eventType: "-",
            doorName: "-",
            message: $"Auto Mode stopped because Softwire became unavailable. {ex.Message}");

        _simulationCancellation?.Cancel();

        ConnectionLost?.Invoke("Softwire became unavailable during Auto Mode.");
    }

    [RelayCommand(CanExecute = nameof(CanStartSimulation))]
    private async Task StartSimulationAsync()
    {
        IsSimulationRunning = true;
        SimulationStatus = "Running";

        // Reset all run counters before starting a new simulation.
        // CompletedEvents counts real generated events only; FailedAttempts counts retries/skips.
        CompletedEvents = 0;
        FailedAttempts = 0;
        ExecutedNormalEvents = 0;
        ExecutedForcedEvents = 0;
        ExecutedHeldEvents = 0;
        _consecutiveFailedAttempts = 0;

        // Start each run with a clean reservation list.
        // If a previous run was stopped or completed, we do not want stale reservations preventing doors from being selected in the next run.
        _reservedDoors.Clear();

        _simulationCancellation = new CancellationTokenSource();

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: "Auto Mode started.");

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: $"Settings: {NumberOfEventsValue} events, {SelectedDelayMode} delay, {SelectedEventProfile}, PIN {GlobalPin}.");

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: "Simulation dependencies configured. Running simulation loop.");

        if (!HasSimulationDependencies)
        {
            SimulationStatus = "Configuration error";

            AddLog(
                level: "Error",
                eventType: "-",
                doorName: "-",
                message: "Auto Mode cannot start because simulation dependencies are not configured.");

            IsSimulationRunning = false;
            return;
        }

        try
        {
            await RunSimulationAsync(_simulationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AddLog(
                level: "Warning",
                eventType: "-",
                doorName: "-",
                message: "Auto Mode stopped by user.");
        }
        catch (Exception ex)
        {
            // Any unexpected exception here is treated as a Softwire/API failure.
            //
            // Examples:
            //      - Softwire service stopped during Auto Mode,
            //      - HTTP call failed,
            //      - input/read/swipe callback threw while the simulation was running.
            HandleAutoModeConnectionLost(ex);
        }
        finally
        {
            _simulationCancellation?.Dispose();
            _simulationCancellation = null;

            // Held events may still have background cleanup tasks running after the last requested event has been generated.
            // Before the run fully ends, wait for those cleanup tasks so Auto Mode does not leave simulated door sensors open.
            await WaitForHeldCleanupTasksAsync();

            _reservedDoors.Clear();
            _heldCleanupTasks.Clear();

            IsSimulationRunning = false;

            if (CompletedEvents >= NumberOfEventsValue)
            {
                SimulationStatus = "Completed";

                AddLog(
                    level: "Success",
                    eventType: "-",
                    doorName: "-",
                    message: "Auto Mode completed all requested events.");
            }
            else if (SimulationStatus != "Stopped")
            {
                SimulationStatus = "Stopped";
            }
        }
    }

    private bool CanStartSimulation()
    {
        return !IsSimulationRunning && !HasValidationMessage;
    }

    [RelayCommand(CanExecute = nameof(CanStopSimulation))]
    private void StopSimulation()
    {
        SimulationStatus = "Stopping...";

        _simulationCancellation?.Cancel();
    }

    private bool CanStopSimulation()
    {
        return IsSimulationRunning;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();

        if (!IsSimulationRunning)
        {
            AddLog(
                level: "Info",
                eventType: "-",
                doorName: "-",
                message: "Event log cleared.");
        }
    }


    /*
      #############################################################################
                        Simulation Loop and Event Routing
      #############################################################################
    */

    // Runs the Auto Mode simulation loop.
    // CompletedEvents represents successfully generated events only.
    // Failed/skipped attempts increment FailedAttempts but do not consume one of the requested events.
    // This means "Requested 100" means Auto Mode will try to generate 100 real events, not 100 attempts.
    // A consecutive-failure guard prevents Auto Mode from retrying forever if the environment cannot currently produce valid events.
    private async Task RunSimulationAsync(CancellationToken cancellationToken)
    {
        while (CompletedEvents < NumberOfEventsValue)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var eventNumber = CompletedEvents + 1;

            AddEventSeparator(eventNumber);

            var waitSeconds = GetRandomDelaySeconds();

            AddLog(
                level: "Info",
                eventType: "-",
                doorName: "-",
                message: waitSeconds == 0
                    ? "No delay before next event."
                    : $"Waiting {waitSeconds} second(s) before next event.",
                eventNumber: eventNumber);

            if (waitSeconds > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var eventType = GetRandomEventType();

            AddLog(
                level: "Info",
                eventType: eventType,
                doorName: "-",
                message: $"Event type selected: {eventType}.",
                eventNumber: eventNumber);

            // Track counts before the event runs.
            // CompletedEvents should only increase when a real event was generated.
            // FailedAttempts should increase when the event could not be generated and Auto Mode needs to retry.
            var completedBeforeEvent = CompletedEvents;
            var failedBeforeEvent = FailedAttempts;

            await ExecuteSimulationEventAsync(eventNumber, eventType, cancellationToken);

            // If a real event was generated, reset the consecutive failure guard.
            if (CompletedEvents > completedBeforeEvent)
            {
                _consecutiveFailedAttempts = 0;
            }
            // If the event did not complete but did register a failed attempt, count it towards the runaway guard.
            else if (FailedAttempts > failedBeforeEvent)
            {
                _consecutiveFailedAttempts++;
            }
            // Defensive fallback.
            // If neither counter changed, something returned without clearly succeeding or failing. Treat it as a retry so Auto Mode cannot loop forever silently.
            else
            {
                FailedAttempts++;
                _consecutiveFailedAttempts++;

                AddLog(
                    level: "Warning",
                    eventType: eventType,
                    doorName: "-",
                    message: "Event attempt ended without success or failure being recorded. Counting as a retry.",
                    eventNumber: eventNumber);
            }

            if (_consecutiveFailedAttempts >= MaxConsecutiveFailedAttempts)
            {
                SimulationStatus = "Stopped";

                AddLog(
                    level: "Error",
                    eventType: "-",
                    doorName: "-",
                    message: $"Auto Mode stopped automatically after {MaxConsecutiveFailedAttempts} consecutive failed attempts. Check door configuration, door settings/properties, reader modes, cardholders, credentials, held-open settings, and Softwire connectivity.");

                break;
            }
        }
    }

    // Routes one generated event type to the correct simulation method.
    // The event type is selected by GetRandomEventType() using the configured event profile percentages.
    // Each execution method is responsible for deciding whether it produced a real event or recorded a failed/retry attempt.
    private async Task ExecuteSimulationEventAsync(int eventNumber, string eventType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (eventType == "Normal")
        {
            await ExecuteNormalAccessEventAsync(eventNumber, cancellationToken);
            return;
        }

        if (eventType == "Forced")
        {
            await ExecuteForcedDoorEventAsync(eventNumber, cancellationToken);
            return;
        }

        await ExecuteHeldOpenEventAsync(eventNumber, cancellationToken);

    }

    // Returns a random delay between the configured minimum and maximum values.
    private int GetRandomDelaySeconds()
    {
        if (MaximumDelaySecondsValue <= MinimumDelaySecondsValue)
            return MinimumDelaySecondsValue;

        return _random.Next(MinimumDelaySecondsValue, MaximumDelaySecondsValue + 1);
    }

    // Randomly chooses Normal / Forced / Held based on the selected event profile.
    private string GetRandomEventType()
    {
        var roll = _random.Next(1, 101);

        if (roll <= NormalEventPercentage)
            return "Normal";

        if (roll <= NormalEventPercentage + ForcedEventPercentage)
            return "Forced";

        return "Held";
    }


    /*
      #############################################################################
                                 Normal Access Events
      #############################################################################
    */

    // Executes a normal access event. Normal events can be generated by either:
    //      - a reader/cardholder action,
    //      - a REX input action.
    // If a door supports both methods, readers are preferred because they exercise cardholders, credentials, Card + PIN, access rules, and granted/denied logic.
    private async Task ExecuteNormalAccessEventAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null || _setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Normal",
                doorName: "-",
                message: "Normal event failed because Auto Mode dependencies are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var doors = await _getDoorsAsync();

        var selectedDoor = SelectNormalDoorCandidate(doors);

        if (selectedDoor == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: "-",
                message: "No suitable normal-event candidate found. Door must be locked, not in maintenance, and have a reader or usable REX.",
                eventNumber: eventNumber);

            return;
        }

        var method = SelectNormalEventMethod(selectedDoor);

        AddLog(
            level: "Info",
            eventType: "Normal",
            doorName: selectedDoor.Name,
            message: $"Door selected. Method selected: {method}.",
            eventNumber: eventNumber);

        if (method == "REX")
        {
            var rexPath = SelectRexPath(selectedDoor);
            var rexDescription = GetRexDescription(selectedDoor, rexPath);

            await ExecuteNormalRexEventAsync(
                eventNumber,
                selectedDoor,
                rexPath,
                rexDescription,
                cancellationToken);

            return;
        }

        if (method == "Reader")
        {
            var readerSelection = SelectReaderForDoor(selectedDoor);

            if (readerSelection == null)
            {
                FailedAttempts++;

                AddLog(
                    level: "Warning",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Reader was selected, but no valid reader path could be chosen.",
                    eventNumber: eventNumber);

                return;
            }

            await ExecuteNormalReaderEventAsync(
                eventNumber,
                selectedDoor,
                readerSelection,
                cancellationToken);

            return;
        }

        FailedAttempts++;

        AddLog(
            level: "Error",
            eventType: "Normal",
            doorName: selectedDoor.Name,
            message: "Normal event failed because no supported method could be selected.",
            eventNumber: eventNumber);
    }

    // Executes a normal access event using a REX input.
    // The REX is activated and released, then the door sensor is opened and closed where available.
    // Any input opened by Auto Mode is cleaned up if the simulation is stopped during the event.
    private async Task ExecuteNormalRexEventAsync(int eventNumber, SoftwireDoor selectedDoor, string rexPath, string rexDescription, CancellationToken cancellationToken)
    {
        if (_setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal REX event failed because the input-state callback is not configured.",
                eventNumber: eventNumber);

            return;
        }

        if (string.IsNullOrWhiteSpace(rexPath))
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal REX event failed because no valid REX path could be selected.",
                eventNumber: eventNumber);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var rexWasActivated = false;
        var doorSensorWasOpened = false;
        var eventWasCancelled = false;
        var cleanupFailed = false;

        try
        {
            AddLog(
                level: "Info",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: $"Activating {rexDescription}.",
                eventNumber: eventNumber);

            var rexActivated = await _setInputStateAsync(rexPath, "Active");

            if (!rexActivated)
            {
                FailedAttempts++;

                AddLog(
                    level: "Error",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: $"Failed to activate {rexDescription}.",
                    eventNumber: eventNumber);

                return;
            }

            rexWasActivated = true;

            // Give Softwire a brief moment to process the REX activation and unlock the door.
            // In testing, Softwire reacts quickly, so one second is enough.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            AddLog(
                level: "Info",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: $"Releasing {rexDescription}.",
                eventNumber: eventNumber);

            var rexReleased = await _setInputStateAsync(rexPath, "Inactive");

            if (!rexReleased)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: $"{rexDescription} was activated, but DoorSim failed to release it. Manual cleanup may be required.",
                    eventNumber: eventNumber);

                return;
            }

            rexWasActivated = false;

            // If the door has a sensor, simulate a normal user opening and closing
            // the door after the REX unlock.
            if (selectedDoor.HasDoorSensor &&
                !string.IsNullOrWhiteSpace(selectedDoor.DoorSensorDevicePath))
            {
                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Opening door sensor after REX unlock.",
                    eventNumber: eventNumber);

                var sensorOpened = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Active");

                if (!sensorOpened)
                {
                    FailedAttempts++;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: "REX was processed, but DoorSim failed to open the door sensor.",
                        eventNumber: eventNumber);

                    return;
                }

                doorSensorWasOpened = true;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Closing door sensor.",
                    eventNumber: eventNumber);

                var sensorClosed = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Inactive");

                if (!sensorClosed)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: "Door sensor was opened, but DoorSim failed to close it. Manual cleanup may be required.",
                        eventNumber: eventNumber);

                    return;
                }

                doorSensorWasOpened = false;
            }
            else
            {
                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "REX event completed without door sensor movement because this door has no configured door sensor.",
                    eventNumber: eventNumber);
            }
        }
        catch (OperationCanceledException)
        {
            eventWasCancelled = true;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal REX event was stopped while running. Cleaning up simulated inputs.",
                eventNumber: eventNumber);
        }
        finally
        {
            // Best-effort door sensor cleanup.
            if (doorSensorWasOpened)
            {
                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Cleaning up door sensor.",
                    eventNumber: eventNumber);

                var sensorClosed = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Inactive");

                if (!sensorClosed)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: "DoorSim failed to clean up the door sensor. Manual cleanup may be required.",
                        eventNumber: eventNumber);
                }
            }

            // Best-effort REX cleanup.
            if (rexWasActivated)
            {
                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: $"Cleaning up {rexDescription}.",
                    eventNumber: eventNumber);

                var rexReleased = await _setInputStateAsync(rexPath, "Inactive");

                if (!rexReleased)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: $"DoorSim failed to clean up {rexDescription}. Manual cleanup may be required.",
                        eventNumber: eventNumber);
                }
            }
        }

        if (cleanupFailed)
        {
            FailedAttempts++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal REX event stopped and cleaned up.",
                eventNumber: eventNumber);

            return;
        }

        ExecutedNormalEvents++;
        CompletedEvents++;

        AddLog(
            level: "Success",
            eventType: "Normal",
            doorName: selectedDoor.Name,
            message: "Normal REX event completed.",
            eventNumber: eventNumber);
    }

    // Executes a real normal access event using a reader and cardholder credential. Sequence:
    //      1. Select a suitable cardholder.
    //      2. Swipe the card credential at the selected reader.
    //      3. If the reader is Card + PIN, send the configured Global PIN.
    //      4. Poll Softwire briefly to see whether the door unlocks or reports denial.
    //      5. If the door unlocks and has a sensor, open and close the door sensor.
    // Important Card + PIN rule:
    //      For Card + PIN readers, Auto Mode only selects cardholders where HasPin is true.
    //      This avoids deliberately selecting cardholders that cannot complete a Card + PIN transaction.
    private async Task ExecuteNormalReaderEventAsync(int eventNumber, SoftwireDoor selectedDoor, AutoReaderSelection readerSelection, CancellationToken cancellationToken)
    {
        if (_getCardholders == null ||
            _getDoorsAsync == null ||
            _setInputStateAsync == null ||
            _swipeRawAsync == null ||
            _swipeWiegand26Async == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal reader event failed because one or more simulation callbacks are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var cardholder = SelectCardholderForReader(readerSelection);

        if (cardholder == null)
        {
            FailedAttempts++;

            var reason = readerSelection.RequiresCardAndPin
                ? "No suitable cardholder found with both a card credential and PIN."
                : "No suitable cardholder found with a card credential.";

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: reason,
                eventNumber: eventNumber);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var doorSensorWasOpened = false;
        var eventWasCancelled = false;
        var cleanupFailed = false;

        try
        {
            AddLog(
                level: "Info",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: $"Reader selected: {readerSelection.Description} ({readerSelection.ReaderMode}).",
                eventNumber: eventNumber);

            AddLog(
                level: "Info",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: $"Cardholder selected: {cardholder.CardholderName}.",
                eventNumber: eventNumber);

            AddLog(
                level: "Info",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Swiping card credential.",
                eventNumber: eventNumber);

            var cardSwipeSucceeded = await _swipeRawAsync(
                readerSelection.ReaderPath,
                cardholder.TrimmedCredential,
                cardholder.BitCount);

            if (!cardSwipeSucceeded)
            {
                FailedAttempts++;

                AddLog(
                    level: "Error",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Card swipe command failed.",
                    eventNumber: eventNumber);

                return;
            }

            if (readerSelection.RequiresCardAndPin)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Reader requires Card + PIN. Sending Global PIN.",
                    eventNumber: eventNumber);

                // Softwire expects PIN input through SwipeWiegand26.
                // Facility code remains 0; the PIN is sent as the card value.
                var pinSucceeded = await _swipeWiegand26Async(readerSelection.ReaderPath, 0, int.Parse(GlobalPin));

                if (!pinSucceeded)
                {
                    FailedAttempts++;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: "PIN command failed.",
                        eventNumber: eventNumber);

                    return;
                }
            }

            var decision = await WaitForReaderDecisionAsync(
                selectedDoor.Id,
                cancellationToken);

            if (decision == null)
            {
                FailedAttempts++;

                AddLog(
                    level: "Warning",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "No access decision or unlock was observed before timeout.",
                    eventNumber: eventNumber);

                return;
            }

            if (decision.LastDecisionDenied)
            {
                CompletedEvents++;
                ExecutedNormalEvents++;

                AddLog(
                    level: "Warning",
                    eventType: "Normal",
                    doorName: decision.Name,
                    message: "Access denied. Door will not be opened.",
                    eventNumber: eventNumber);

                return;
            }

            if (decision.LastDecisionGranted && decision.DoorIsLocked)
            {
                CompletedEvents++;
                ExecutedNormalEvents++;

                AddLog(
                    level: "Warning",
                    eventType: "Normal",
                    doorName: decision.Name,
                    message: "Credential accepted, but the door remained locked. Door will not be opened.",
                    eventNumber: eventNumber);

                return;
            }

            if (!decision.DoorIsLocked)
            {
                AddLog(
                    level: "Success",
                    eventType: "Normal",
                    doorName: decision.Name,
                    message: "Access granted and door unlocked.",
                    eventNumber: eventNumber);

                if (decision.HasDoorSensor &&
                    !string.IsNullOrWhiteSpace(decision.DoorSensorDevicePath))
                {
                    AddLog(
                        level: "Info",
                        eventType: "Normal",
                        doorName: decision.Name,
                        message: "Opening door sensor after reader unlock.",
                        eventNumber: eventNumber);

                    var sensorOpened = await _setInputStateAsync(
                        decision.DoorSensorDevicePath,
                        "Active");

                    if (!sensorOpened)
                    {
                        FailedAttempts++;

                        AddLog(
                            level: "Error",
                            eventType: "Normal",
                            doorName: decision.Name,
                            message: "Access was granted, but DoorSim failed to open the door sensor.",
                            eventNumber: eventNumber);

                        return;
                    }

                    doorSensorWasOpened = true;

                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    AddLog(
                        level: "Info",
                        eventType: "Normal",
                        doorName: decision.Name,
                        message: "Closing door sensor.",
                        eventNumber: eventNumber);

                    var sensorClosed = await _setInputStateAsync(
                        decision.DoorSensorDevicePath,
                        "Inactive");

                    if (!sensorClosed)
                    {
                        cleanupFailed = true;

                        AddLog(
                            level: "Error",
                            eventType: "Normal",
                            doorName: decision.Name,
                            message: "Door sensor was opened, but DoorSim failed to close it. Manual cleanup may be required.",
                            eventNumber: eventNumber);

                        return;
                    }

                    doorSensorWasOpened = false;
                }
                else
                {
                    AddLog(
                        level: "Info",
                        eventType: "Normal",
                        doorName: decision.Name,
                        message: "Access granted, but no door sensor is configured to open/close.",
                        eventNumber: eventNumber);
                }

                CompletedEvents++;
                ExecutedNormalEvents++;

                AddLog(
                    level: "Success",
                    eventType: "Normal",
                    doorName: decision.Name,
                    message: "Normal reader event completed.",
                    eventNumber: eventNumber);

                return;
            }

            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Reader action completed, but DoorSim could not determine a clear granted/denied outcome.",
                eventNumber: eventNumber);
        }
        catch (OperationCanceledException)
        {
            eventWasCancelled = true;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal reader event was stopped while running. Cleaning up simulated inputs.",
                eventNumber: eventNumber);
        }
        finally
        {
            if (doorSensorWasOpened)
            {
                AddLog(
                    level: "Info",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: "Cleaning up door sensor.",
                    eventNumber: eventNumber);

                var sensorClosed = await _setInputStateAsync(
                    selectedDoor.DoorSensorDevicePath,
                    "Inactive");

                if (!sensorClosed)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Normal",
                        doorName: selectedDoor.Name,
                        message: "DoorSim failed to clean up the door sensor. Manual cleanup may be required.",
                        eventNumber: eventNumber);
                }
            }
        }

        if (cleanupFailed)
        {
            FailedAttempts++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: selectedDoor.Name,
                message: "Normal reader event stopped and cleaned up.",
                eventNumber: eventNumber);

            return;
        }
    }


    /*
      #############################################################################
                                 Held-Open Events
      #############################################################################
    */

    // Executes a held-open event. Held events can be generated by either:
    //      - a reader/cardholder action,
    //      - a REX input action.
    // Method selection mirrors Normal events:
    //      - If a door has readers and usable REX inputs, prefer readers 80% of the time.
    //      - If only readers exist, use a reader.
    //      - If only usable REX inputs exist, use REX.
    // Unlike Normal events, a Held event only counts as completed when the door sensor is opened and a delayed cleanup task has been scheduled.
    private async Task ExecuteHeldOpenEventAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null || _setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Held",
                doorName: "-",
                message: "Held event failed because Auto Mode dependencies are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var selectedDoor = await WaitForHeldDoorCandidateAsync(eventNumber, cancellationToken);

        if (selectedDoor == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Held",
                doorName: "-",
                message: "No suitable held-open door became available. Door must be locked, not in maintenance, have a door sensor, have 'Door Held' configured, and support reader or 'Unlock On Rex'.",
                eventNumber: eventNumber);

            return;
        }

        var method = SelectHeldEventMethod(selectedDoor);

        AddLog(
            level: "Info",
            eventType: "Held",
            doorName: selectedDoor.Name,
            message: $"Door selected. Method selected: {method}.",
            eventNumber: eventNumber);

        if (method == "REX")
        {
            var rexPath = SelectRexPath(selectedDoor);
            var rexDescription = GetRexDescription(selectedDoor, rexPath);

            await ExecuteHeldRexEventAsync(
                eventNumber,
                selectedDoor,
                rexPath,
                rexDescription,
                cancellationToken);

            return;
        }

        if (method == "Reader")
        {
            var readerSelection = SelectReaderForDoor(selectedDoor);

            if (readerSelection == null)
            {
                FailedAttempts++;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: "Reader was selected for held-open event, but no valid reader path could be chosen.",
                    eventNumber: eventNumber);

                return;
            }

            await ExecuteHeldReaderEventAsync(
                eventNumber,
                selectedDoor,
                readerSelection,
                cancellationToken);

            return;
        }

        FailedAttempts++;

        AddLog(
            level: "Error",
            eventType: "Held",
            doorName: selectedDoor.Name,
            message: "Held event failed because no supported method could be selected.",
            eventNumber: eventNumber);
    }

    // Executes a held-open event using REX.
    // The caller has already selected a suitable held-capable door and REX path.
    // This method reserves the door, unlocks it with REX, opens the door sensor, and schedules delayed cleanup.
    //      1. Reserve the selected door.
    //      2. Activate and release REX.
    //      3. Open the door sensor and leave it open.
    //      4. Start background cleanup to close the sensor after DoorHeldTime + buffer.
    // Important:
    //      The event is counted as executed once the door sensor has been opened and cleanup has been scheduled.
    //      The door remains reserved until cleanup closes the sensor or discovers the door was deleted.
    private async Task ExecuteHeldRexEventAsync(int eventNumber, SoftwireDoor selectedDoor, string rexPath, string rexDescription, CancellationToken cancellationToken)
    {
        if (_setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held REX event failed because the input-state callback is not configured.",
                eventNumber: eventNumber);

            return;
        }

        if (string.IsNullOrWhiteSpace(rexPath))
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held REX event failed because no valid REX path could be selected.",
                eventNumber: eventNumber);

            return;
        }

        var doorSensorWasOpened = false;
        var rexWasActivated = false;
        var eventWasCancelled = false;
        var cleanupFailed = false;
        var cleanupScheduled = false;

        ReserveDoor(selectedDoor, "Held-open event in progress");

        try
        {
            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: $"Activating {rexDescription}.",
                eventNumber: eventNumber);

            var rexActivated = await _setInputStateAsync(rexPath, "Active");

            if (!rexActivated)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: $"Failed to activate {rexDescription}.",
                    eventNumber: eventNumber);

                return;
            }

            rexWasActivated = true;

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: $"Releasing {rexDescription}.",
                eventNumber: eventNumber);

            var rexReleased = await _setInputStateAsync(rexPath, "Inactive");

            if (!rexReleased)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: $"{rexDescription} was activated, but DoorSim failed to release it. Manual cleanup may be required.",
                    eventNumber: eventNumber);

                return;
            }

            rexWasActivated = false;

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Opening door sensor and leaving it open.",
                eventNumber: eventNumber);

            var sensorOpened = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Active");

            if (!sensorOpened)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: "DoorSim failed to open the door sensor for the held-open event.",
                    eventNumber: eventNumber);

                return;
            }

            doorSensorWasOpened = true;

            ScheduleHeldDoorCleanup(
                selectedDoor.Id,
                selectedDoor.Name,
                selectedDoor.DoorSensorDevicePath,
                GetHeldCleanupDelaySeconds(selectedDoor),
                cancellationToken);

            cleanupScheduled = true;

            ExecutedHeldEvents++;
            CompletedEvents++;

            AddLog(
                level: "Success",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held-open event generated. Door reserved until cleanup closes the door sensor.",
                eventNumber: eventNumber);
        }
        catch (OperationCanceledException)
        {
            eventWasCancelled = true;

            AddLog(
                level: "Warning",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held REX event was stopped while running. Cleaning up simulated inputs.",
                eventNumber: eventNumber);
        }
        finally
        {
            // If the background cleanup was scheduled, it now owns the open door
            // sensor and reservation. Do not close/release them here.
            if (!cleanupScheduled && (cleanupFailed || eventWasCancelled))
            {
                if (doorSensorWasOpened)
                {
                    AddLog(
                        level: "Info",
                        eventType: "Held",
                        doorName: selectedDoor.Name,
                        message: "Closing door sensor during held-event cleanup.",
                        eventNumber: eventNumber);

                    await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Inactive");
                }

                if (rexWasActivated)
                {
                    AddLog(
                        level: "Info",
                        eventType: "Held",
                        doorName: selectedDoor.Name,
                        message: $"Cleaning up {rexDescription}.",
                        eventNumber: eventNumber);

                    await _setInputStateAsync(rexPath, "Inactive");
                }

                ReleaseDoorReservation(
                    selectedDoor.Id,
                    "Held-open reservation released after failed/stopped setup.");
            }
        }

        if (cleanupFailed)
        {
            FailedAttempts++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;
            return;
        }
    }

    // Executes a held-open event using a reader and cardholder credential.
    // This follows the same reader/Card + PIN rules as a normal reader event, but instead of closing the door sensor after one second, it leaves the sensor open long enough for Softwire to generate a Door Held event.
    //      1. Reserve the selected door.
    //      2. Select a suitable cardholder.
    //      3. Swipe the card credential.
    //      4. If the reader is Card + PIN, send the configured Global PIN.
    //      5. Wait for the door to unlock.
    //      6. Open the door sensor and leave it open.
    //      7. Start background cleanup to close the sensor after DoorHeldTime + buffer.
    // Important Card + PIN rule: For Card + PIN readers, Auto Mode only selects cardholders where HasPin is true.
    private async Task ExecuteHeldReaderEventAsync(int eventNumber, SoftwireDoor selectedDoor, AutoReaderSelection readerSelection, CancellationToken cancellationToken)
    {
        if (_getCardholders == null ||
            _getDoorsAsync == null ||
            _setInputStateAsync == null ||
            _swipeRawAsync == null ||
            _swipeWiegand26Async == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held reader event failed because one or more simulation callbacks are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var cardholder = SelectCardholderForReader(readerSelection);

        if (cardholder == null)
        {
            FailedAttempts++;

            var reason = readerSelection.RequiresCardAndPin
                ? "No suitable cardholder found with both a card credential and PIN for held reader event."
                : "No suitable cardholder found with a card credential for held reader event.";

            AddLog(
                level: "Warning",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: reason,
                eventNumber: eventNumber);

            return;
        }

        var doorSensorWasOpened = false;
        var eventWasCancelled = false;
        var cleanupFailed = false;
        var cleanupScheduled = false;
        var openedDoorId = selectedDoor.Id;
        var openedDoorName = selectedDoor.Name;
        var openedDoorSensorPath = selectedDoor.DoorSensorDevicePath;

        ReserveDoor(selectedDoor, "Held-open event in progress");

        try
        {
            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: $"Reader selected: {readerSelection.Description} ({readerSelection.ReaderMode}).",
                eventNumber: eventNumber);

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: $"Cardholder selected: {cardholder.CardholderName}.",
                eventNumber: eventNumber);

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Swiping card credential for held-open event.",
                eventNumber: eventNumber);

            var cardSwipeSucceeded = await _swipeRawAsync(
                readerSelection.ReaderPath,
                cardholder.TrimmedCredential,
                cardholder.BitCount);

            if (!cardSwipeSucceeded)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: "Card swipe command failed for held-open event.",
                    eventNumber: eventNumber);

                return;
            }

            if (readerSelection.RequiresCardAndPin)
            {
                cancellationToken.ThrowIfCancellationRequested();

                AddLog(
                    level: "Info",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: "Reader requires Card + PIN. Sending Global PIN.",
                    eventNumber: eventNumber);

                var pinSucceeded = await _swipeWiegand26Async(
                    readerSelection.ReaderPath,
                    0,
                    int.Parse(GlobalPin));

                if (!pinSucceeded)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Held",
                        doorName: selectedDoor.Name,
                        message: "PIN command failed for held-open event.",
                        eventNumber: eventNumber);

                    return;
                }
            }

            var decision = await WaitForReaderDecisionAsync(
                selectedDoor.Id,
                cancellationToken);

            if (decision == null)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: selectedDoor.Name,
                    message: "No access decision or unlock was observed before timeout. Held-open event cannot be generated.",
                    eventNumber: eventNumber);

                return;
            }

            if (decision.LastDecisionDenied)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: decision.Name,
                    message: "Access denied. Held-open event cannot be generated.",
                    eventNumber: eventNumber);

                return;
            }

            if (decision.LastDecisionGranted && decision.DoorIsLocked)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: decision.Name,
                    message: "Credential accepted, but the door remained locked. Held-open event cannot be generated.",
                    eventNumber: eventNumber);

                return;
            }

            if (decision.DoorIsLocked)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: decision.Name,
                    message: "Reader action completed, but the door did not unlock. Held-open event cannot be generated.",
                    eventNumber: eventNumber);

                return;
            }

            if (!decision.HasDoorSensor ||
                string.IsNullOrWhiteSpace(decision.DoorSensorDevicePath))
            {
                cleanupFailed = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: decision.Name,
                    message: "Door unlocked, but no door sensor is configured. Held-open event cannot be generated.",
                    eventNumber: eventNumber);

                return;
            }

            openedDoorId = decision.Id;
            openedDoorName = decision.Name;
            openedDoorSensorPath = decision.DoorSensorDevicePath;

            AddLog(
                level: "Success",
                eventType: "Held",
                doorName: openedDoorName,
                message: "Access granted and door unlocked.",
                eventNumber: eventNumber);

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: openedDoorName,
                message: "Opening door sensor and leaving it open.",
                eventNumber: eventNumber);

            var sensorOpened = await _setInputStateAsync(
                openedDoorSensorPath,
                "Active");

            if (!sensorOpened)
            {
                cleanupFailed = true;

                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: openedDoorName,
                    message: "DoorSim failed to open the door sensor for the held-open event.",
                    eventNumber: eventNumber);

                return;
            }

            doorSensorWasOpened = true;

            ScheduleHeldDoorCleanup(
                openedDoorId,
                openedDoorName,
                openedDoorSensorPath,
                GetHeldCleanupDelaySeconds(decision),
                cancellationToken);

            cleanupScheduled = true;

            ExecutedHeldEvents++;
            CompletedEvents++;

            AddLog(
                level: "Success",
                eventType: "Held",
                doorName: openedDoorName,
                message: "Held-open reader event generated. Door reserved until cleanup closes the door sensor.",
                eventNumber: eventNumber);
        }
        catch (OperationCanceledException)
        {
            eventWasCancelled = true;

            AddLog(
                level: "Warning",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held reader event was stopped while running. Cleaning up simulated inputs.",
                eventNumber: eventNumber);
        }
        finally
        {
            // If cleanup was scheduled, it owns the open sensor/reservation.
            // If setup failed or was cancelled before scheduling cleanup, restore
            // anything Auto Mode touched and release the reservation.
            if (!cleanupScheduled && (cleanupFailed || eventWasCancelled))
            {
                if (doorSensorWasOpened)
                {
                    AddLog(
                        level: "Info",
                        eventType: "Held",
                        doorName: openedDoorName,
                        message: "Closing door sensor during held-reader cleanup.",
                        eventNumber: eventNumber);

                    await _setInputStateAsync(openedDoorSensorPath, "Inactive");
                }

                ReleaseDoorReservation(
                    selectedDoor.Id,
                    "Held-open reservation released after failed/stopped reader setup.");
            }
        }

        if (cleanupFailed)
        {
            FailedAttempts++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;
            return;
        }
    }


    /*
      #############################################################################
                              Forced-Door Events
      #############################################################################
    */

    // A forced-door event simulates the door sensor opening while the door is still locked.
    // If the door is configured to enforce forced-open events, Softwire should generate the corresponding door forced event.
    // Important cleanup rule:
    //      If Auto Mode opens a door sensor, it must make a best effort to close it again, even if the trainer presses Stop while the event is running.
    // Sequence:
    //      1. Find a suitable locked door with a door sensor.
    //      2. Set the door sensor Active.
    //      3. Wait briefly so Softwire registers the event.
    //      4. Always attempt to set the door sensor Inactive again.
    //      5. Log success/failure.
    private async Task ExecuteForcedDoorEventAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null || _setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Forced",
                doorName: "-",
                message: "Forced event failed because Auto Mode dependencies are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var doors = await _getDoorsAsync();

        var selectedDoor = SelectForcedDoorCandidate(doors);

        if (selectedDoor == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Forced",
                doorName: "-",
                message: "No suitable forced-door candidate found. Door must be locked, not in maintenance, have a door sensor, and have door forced enabled.",
                eventNumber: eventNumber);

            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var doorSensorWasOpened = false;
        var eventWasCancelled = false;
        var cleanupFailed = false;

        try
        {
            AddLog(
                level: "Info",
                eventType: "Forced",
                doorName: selectedDoor.Name,
                message: "Opening door sensor without a valid unlock.",
                eventNumber: eventNumber);

            var opened = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Active");

            if (!opened)
            {
                FailedAttempts++;

                AddLog(
                    level: "Error",
                    eventType: "Forced",
                    doorName: selectedDoor.Name,
                    message: "Failed to set door sensor Active.",
                    eventNumber: eventNumber);

                return;
            }

            doorSensorWasOpened = true;

            // Softwire sees the input state change immediately in testing.
            // One second is enough to generate the forced-door condition without slowing the simulation unnecessarily.
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            eventWasCancelled = true;

            AddLog(
                level: "Warning",
                eventType: "Forced",
                doorName: selectedDoor.Name,
                message: "Forced event was stopped while running. Cleaning up door sensor.",
                eventNumber: eventNumber);
        }
        finally
        {
            if (doorSensorWasOpened)
            {
                AddLog(
                    level: "Info",
                    eventType: "Forced",
                    doorName: selectedDoor.Name,
                    message: "Closing door sensor.",
                    eventNumber: eventNumber);

                var closed = await _setInputStateAsync(selectedDoor.DoorSensorDevicePath, "Inactive");

                if (!closed)
                {
                    cleanupFailed = true;

                    AddLog(
                        level: "Error",
                        eventType: "Forced",
                        doorName: selectedDoor.Name,
                        message: "Door sensor was opened, but DoorSim failed to set it Inactive again. Manual cleanup may be required.",
                        eventNumber: eventNumber);
                }
            }
        }

        if (cleanupFailed)
        {
            FailedAttempts++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Forced",
                doorName: selectedDoor.Name,
                message: "Forced-door event stopped and cleaned up.",
                eventNumber: eventNumber);

            return;
        }

        ExecutedForcedEvents++;
        CompletedEvents++;

        AddLog(
            level: "Success",
            eventType: "Forced",
            doorName: selectedDoor.Name,
            message: "Forced-door event completed. Door sensor opened and closed.",
            eventNumber: eventNumber);
    }


    /*
      #############################################################################
                          Door Reservation Helpers
      #############################################################################
    */

    // Reserves a door so Auto Mode will not select it for another event.
    //
    // This is mainly for Held events. A held-open door needs time to remain open so
    // Softwire can generate the door-held-open event. During that time, Auto Mode
    // must not accidentally pick the same door for a Normal or Forced event and
    // close/reopen the sensor.
    private void ReserveDoor(SoftwireDoor door, string reason)
    {
        if (string.IsNullOrWhiteSpace(door.Id))
            return;

        _reservedDoors[door.Id] = new AutoDoorReservation
        {
            DoorId = door.Id,
            DoorName = door.Name,
            Reason = reason,
            ReservedAtUtc = DateTime.UtcNow
        };

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: door.Name,
            message: $"Door reserved: {reason}.");
    }

    // Releases a previously reserved door.
    //
    // It is safe to call this even if the door is not currently reserved.
    // The log level can be changed by the caller so normal cleanup can be Info,
    // while cleanup after Stop/cancellation can be Warning.
    private void ReleaseDoorReservation(string doorId, string message, string level = "Info")
    {
        if (string.IsNullOrWhiteSpace(doorId))
            return;

        if (!_reservedDoors.TryGetValue(doorId, out var reservation))
            return;

        _reservedDoors.Remove(doorId);

        AddLog(
            level: level,
            eventType: "-",
            doorName: reservation.DoorName,
            message: message);
    }

    // Returns true when the supplied door is currently reserved by Auto Mode.
    private bool IsDoorReserved(string doorId)
    {
        if (string.IsNullOrWhiteSpace(doorId))
            return false;

        return _reservedDoors.ContainsKey(doorId);
    }

    // Releases reservations for doors that no longer exist in the latest Softwire
    // door list.
    //
    // This is important during training because someone may delete or reconfigure a
    // door while Auto Mode is running. If a reserved door disappears, we simply log
    // the situation, release the reservation, and keep the simulation moving.
    private void ReleaseReservationsForDeletedDoors(IEnumerable<SoftwireDoor> currentDoors)
    {
        if (!_reservedDoors.Any())
            return;

        var currentDoorIds = currentDoors
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .Select(d => d.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var deletedReservations = _reservedDoors.Values
            .Where(r => !currentDoorIds.Contains(r.DoorId))
            .ToList();

        foreach (var reservation in deletedReservations)
        {
            _reservedDoors.Remove(reservation.DoorId);

            AddLog(
                level: "Warning",
                eventType: "-",
                doorName: reservation.DoorName,
                message: "Reserved door no longer exists in Softwire. Releasing reservation and continuing.");
        }
    }


    /*
      #############################################################################
                          Candidate and Method Selection Helpers
      #############################################################################
    */

    // --> Normal candidates / method selection:
    //     -------------------------------------

    // Selects a random door suitable for a normal event. A normal event can use either:
    //      - a reader,
    //      - a REX input, but only when AutoUnlockOnRex is enabled.
    // The door must be locked and not in maintenance mode so the simulation represents a meaningful access attempt rather than interacting with an already-unlocked door.
    private SoftwireDoor? SelectNormalDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var doorList = doors.ToList();

        // If a reserved door was deleted while Auto Mode is running, release the reservation and move on cleanly.
        // This prevents a stale reservation from blocking future event selection.
        ReleaseReservationsForDeletedDoors(doorList);

        var candidates = doorList
            .Where(d => !IsDoorReserved(d.Id))
            .Where(d => d.DoorIsLocked)
            .Where(d => !d.UnlockedForMaintenance)
            .Where(d => HasUsableReader(d) || HasUsableAutoUnlockRex(d))
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }

    // Chooses whether a normal event should use Reader or REX.
    //
    // If both methods are available, readers are preferred because they create richer
    // access-control activity: cardholders, credentials, Card + PIN, access granted,
    // access denied, anti-passback, schedules, and other access-rule behaviour.
    private string SelectNormalEventMethod(SoftwireDoor door)
    {
        var hasReader = HasUsableReader(door);
        var hasRex = HasUsableAutoUnlockRex(door);

        if (hasReader && !hasRex)
            return "Reader";

        if (!hasReader && hasRex)
            return "REX";

        if (hasReader && hasRex)
            return _random.Next(1, 101) <= 80
                ? "Reader"
                : "REX";

        return "None";
    }


    // --> Forced candidates:
    //     ------------------

    // Selects a random door suitable for a forced-door event.
    // A forced-door event should only use doors where opening the door sensor without an unlock is meaningful. Therefore the door must:
    //      - be locked,
    //      - not be unlocked for maintenance,
    //      - have a door sensor,
    //      - have a valid door sensor device path,
    //      - have forced-open enforcement enabled.
    private SoftwireDoor? SelectForcedDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var doorList = doors.ToList();

        // Reserved doors are deliberately being held by Auto Mode, usually because a Held event has opened the sensor and is waiting for Softwire to generate a Door Held event.
        // Do not use those doors for forced events.
        ReleaseReservationsForDeletedDoors(doorList);

        var candidates = doorList
            .Where(d => !IsDoorReserved(d.Id))
            .Where(d => d.DoorIsLocked)
            .Where(d => !d.UnlockedForMaintenance)
            .Where(d => d.HasDoorSensor)
            .Where(d => !string.IsNullOrWhiteSpace(d.DoorSensorDevicePath))
            .Where(d => d.EnforceDoorForcedOpen)
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }


    // --> Held candidates / method selection:
    //     -----------------------------------

    // Waits for a held-capable door to become available.
    // If all suitable Held doors are currently reserved, Auto Mode waits briefly rather than immediately failing.
    // This matters when multiple Held events occur close together and every held-capable door is already waiting for cleanup.
    private async Task<SoftwireDoor?> WaitForHeldDoorCandidateAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null)
            return null;

        var timeoutAtUtc = DateTime.UtcNow.AddSeconds(30);
        var hasLoggedWaiting = false;

        while (DateTime.UtcNow < timeoutAtUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var doors = await _getDoorsAsync();
            var doorList = doors.ToList();

            ReleaseReservationsForDeletedDoors(doorList);

            var candidate = SelectHeldDoorCandidate(doorList);

            if (candidate != null)
                return candidate;

            var hasHeldCapableDoors = doorList.Any(IsHeldCapableDoor);
            var allHeldCapableDoorsReserved =
                hasHeldCapableDoors &&
                doorList
                    .Where(IsHeldCapableDoor)
                    .All(d => IsDoorReserved(d.Id));

            // If suitable held doors exist but are unsuitable for reasons other than reservation such as settings/hardware, do not wait.
            // Waiting only helps when every otherwise valid held door is temporarily reserved.
            if (!allHeldCapableDoorsReserved)
                return null;

            if (!hasLoggedWaiting)
            {
                hasLoggedWaiting = true;

                AddLog(
                    level: "Warning",
                    eventType: "Held",
                    doorName: "-",
                    message: "All held-capable doors are currently reserved. Waiting for a door to become available.",
                    eventNumber: eventNumber);
            }

            await Task.Delay(1000, cancellationToken);
        }

        return null;
    }

    // Selects a random door suitable for a held-open event.
    private SoftwireDoor? SelectHeldDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var candidates = doors
            .Where(IsHeldCapableDoor)
            .Where(d => !IsDoorReserved(d.Id))
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }

    // Chooses whether a held-open event should use Reader or REX.
    // Reader is preferred where possible because it uses cardholders, credentials, Card + PIN, access rules, and denied/granted behaviour.
    private string SelectHeldEventMethod(SoftwireDoor door)
    {
        var hasReader = IsHeldReaderCapableDoor(door);
        var hasRex = IsHeldRexCapableDoor(door);

        if (hasReader && !hasRex)
            return "Reader";

        if (!hasReader && hasRex)
            return "REX";

        if (hasReader && hasRex)
            return _random.Next(1, 101) <= 80
                ? "Reader"
                : "REX";

        return "None";
    }

    // Returns true when a door can generate a held-open event by either reader or REX.
    private static bool IsHeldCapableDoor(SoftwireDoor door)
    {
        return IsHeldReaderCapableDoor(door) || IsHeldRexCapableDoor(door);
    }

    // Returns true when a door can generate a held-open event using a reader.
    private static bool IsHeldReaderCapableDoor(SoftwireDoor door)
    {
        return IsBaseHeldCapableDoor(door) &&
               HasUsableReader(door);
    }

    // Returns true when a door can generate a held-open event using REX.
    private static bool IsHeldRexCapableDoor(SoftwireDoor door)
    {
        return IsBaseHeldCapableDoor(door) &&
               HasUsableAutoUnlockRex(door);
    }

    // Shared held-open requirements.
    // IgnoreHeldOpenWhenUnlocked must be false (this is configured in Config Tool, door --> properties --> Door held).
    // If Softwire is configured to ignore held-open behaviour while unlocked, Auto Mode should not select that door for a held-open scenario because the expected Door Held event may not be generated.
    private static bool IsBaseHeldCapableDoor(SoftwireDoor door)
    {
        return door.DoorIsLocked &&
               !door.UnlockedForMaintenance &&
               door.HasDoorSensor &&
               !string.IsNullOrWhiteSpace(door.DoorSensorDevicePath) &&
               door.DoorHeldTimeSeconds > 0 &&
               !door.IgnoreHeldOpenWhenUnlocked;
    }


    // --> Shared device capability checks:
    //     --------------------------------

    // Returns true when the door has at least one reader path Auto Mode can use.
    private static bool HasUsableReader(SoftwireDoor door)
    {
        return !string.IsNullOrWhiteSpace(door.ReaderSideInDevicePath) ||
               !string.IsNullOrWhiteSpace(door.ReaderSideOutDevicePath);
    }

    // Returns true when the door has at least one usable REX input and the door is configured to unlock on REX.
    private static bool HasUsableAutoUnlockRex(SoftwireDoor door)
    {
        return door.AutoUnlockOnRex && HasUsableRex(door);
    }

    // Returns true when the door has at least one REX input path Auto Mode can use.
    private static bool HasUsableRex(SoftwireDoor door)
    {
        return !string.IsNullOrWhiteSpace(door.RexSideInDevicePath) ||
               !string.IsNullOrWhiteSpace(door.RexSideOutDevicePath) ||
               !string.IsNullOrWhiteSpace(door.RexNoSideDevicePath);
    }

    // Selects one available REX path from the supplied door.
    // If more than one REX exists, a random one is chosen so repeated Auto Mode runs do not always exercise the same side.
    private string SelectRexPath(SoftwireDoor door)
    {
        var rexPaths = new List<string>();

        if (!string.IsNullOrWhiteSpace(door.RexSideInDevicePath))
            rexPaths.Add(door.RexSideInDevicePath);

        if (!string.IsNullOrWhiteSpace(door.RexSideOutDevicePath))
            rexPaths.Add(door.RexSideOutDevicePath);

        if (!string.IsNullOrWhiteSpace(door.RexNoSideDevicePath))
            rexPaths.Add(door.RexNoSideDevicePath);

        if (!rexPaths.Any())
            return string.Empty;

        return rexPaths[_random.Next(rexPaths.Count)];
    }

    // Returns friendly log text for whichever REX path was selected.
    private static string GetRexDescription(SoftwireDoor door, string rexPath)
    {
        if (string.Equals(rexPath, door.RexSideInDevicePath, StringComparison.OrdinalIgnoreCase))
            return "In REX";

        if (string.Equals(rexPath, door.RexSideOutDevicePath, StringComparison.OrdinalIgnoreCase))
            return "Out REX";

        if (string.Equals(rexPath, door.RexNoSideDevicePath, StringComparison.OrdinalIgnoreCase))
            return "No-side REX";

        return "REX";
    }

    // Selects one reader from the supplied door.
    // If both In and Out readers exist, selection is 50/50. This gives Auto Mode a balanced mix of entry and exit reads during long simulations.
    private AutoReaderSelection? SelectReaderForDoor(SoftwireDoor door)
    {
        var readers = new List<AutoReaderSelection>();

        if (!string.IsNullOrWhiteSpace(door.ReaderSideInDevicePath))
        {
            readers.Add(new AutoReaderSelection
            {
                ReaderPath = door.ReaderSideInDevicePath,
                Side = "In",
                Description = "In reader",
                RequiresCardAndPin = door.InReaderRequiresCardAndPin
            });
        }

        if (!string.IsNullOrWhiteSpace(door.ReaderSideOutDevicePath))
        {
            readers.Add(new AutoReaderSelection
            {
                ReaderPath = door.ReaderSideOutDevicePath,
                Side = "Out",
                Description = "Out reader",
                RequiresCardAndPin = door.OutReaderRequiresCardAndPin
            });
        }

        if (!readers.Any())
            return null;

        return readers[_random.Next(readers.Count)];
    }


    /*
      #############################################################################
                          Reader and Cardholder Helpers
      #############################################################################
    */

    // Selects a random cardholder suitable for the selected reader.
    // Card-only reader:
    //      - Any cardholder with a usable card credential can be used.
    // Card + PIN reader:
    //      - Only cardholders with a usable card credential AND HasPin = true are used.
    //      - The actual PIN sent is the Auto Mode Global PIN, so the training system should be configured with matching cardholder PINs.
    private Cardholder? SelectCardholderForReader(AutoReaderSelection readerSelection)
    {
        if (_getCardholders == null)
            return null;

        var candidates = _getCardholders()
            .Where(c => !string.IsNullOrWhiteSpace(c.TrimmedCredential))
            .Where(c => c.BitCount > 0)
            .Where(c => !readerSelection.RequiresCardAndPin || c.HasPin)
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }

    // Waits briefly for Softwire to report a result after a reader action.
    // Reader decisions and door unlock state are reported on the door object.
    // Rather than relying on the manual UI feedback system, Auto Mode refreshes the door directly and uses the latest decision/lock state for logging and behaviour.
    // Returns:
    //      - refreshed door when granted/denied/unlocked state is observed,
    //      - null if no useful result appears before timeout.
    private async Task<SoftwireDoor?> WaitForReaderDecisionAsync(string doorId, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null)
            return null;

        var timeoutAtUtc = DateTime.UtcNow.AddSeconds(4);

        while (DateTime.UtcNow < timeoutAtUtc)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var doors = await _getDoorsAsync();

            var refreshedDoor = doors.FirstOrDefault(d => d.Id == doorId);

            if (refreshedDoor == null)
                return null;

            if (refreshedDoor.LastDecisionGranted ||
                refreshedDoor.LastDecisionDenied ||
                !refreshedDoor.DoorIsLocked)
            {
                return refreshedDoor;
            }

            await Task.Delay(250, cancellationToken);
        }

        return null;
    }


    /*
      #############################################################################
                            Held-Open Cleanup Helpers
      #############################################################################
    */

    // Returns the delay before Auto Mode should close a held-open door.
    private static int GetHeldCleanupDelaySeconds(SoftwireDoor door)
    {
        const int heldEventBufferSeconds = 5;

        return Math.Max(door.DoorHeldTimeSeconds + heldEventBufferSeconds, 1);
    }

    // Schedules background cleanup for a held-open door.
    // The cleanup task closes the door sensor after DoorHeldTime + buffer and then releases the reservation.
    // The delay is based on the door's configured DoorHeldTimeSeconds plus a small safety buffer (5secs), so Softwire has time to generate the Door Held event before DoorSim closes the sensor.
    private void ScheduleHeldDoorCleanup(string doorId, string doorName, string doorSensorPath, int delaySeconds, CancellationToken cancellationToken)
    {
        AddLog(
            level: "Info",
            eventType: "Held",
            doorName: doorName,
            message: $"Door will remain open for approximately {delaySeconds} second(s) before cleanup.");

        var cleanupTask = CleanupHeldDoorLaterAsync(
            doorId: doorId,
            doorName: doorName,
            doorSensorPath: doorSensorPath,
            delaySeconds: delaySeconds,
            cancellationToken: cancellationToken);

        _heldCleanupTasks.Add(cleanupTask);
    }

    // Closes a held-open door sensor after the configured held-open delay.
    // This runs in the background so Auto Mode can continue generating other events while the selected door remains open long enough for Softwire to raise the held-open event.
    private async Task CleanupHeldDoorLaterAsync(string doorId, string doorName, string doorSensorPath, int delaySeconds, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);

            if (_getDoorsAsync == null || _setInputStateAsync == null)
                return;

            var doors = await _getDoorsAsync();

            var refreshedDoor = doors.FirstOrDefault(d => d.Id == doorId);

            if (refreshedDoor == null)
            {
                ReleaseDoorReservation(
                    doorId,
                    "Held-open door no longer exists in Softwire. Reservation released.",
                    level: "Warning");

                return;
            }

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: doorName,
                message: "Held-open timer elapsed. Closing door sensor.");

            var closed = await _setInputStateAsync(doorSensorPath, "Inactive");

            if (!closed)
            {
                AddLog(
                    level: "Error",
                    eventType: "Held",
                    doorName: doorName,
                    message: "DoorSim failed to close the held-open door sensor. Manual cleanup may be required.");

                return;
            }

            ReleaseDoorReservation(
                doorId,
                "Held-open cleanup complete. Door reservation released.");
        }
        catch (OperationCanceledException)
        {
            if (_setInputStateAsync != null)
            {
                try
                {
                    AddLog(
                        level: "Info",
                        eventType: "Held",
                        doorName: doorName,
                        message: "Simulation stopped. Attempting to close held-open door sensor.");

                    var closed = await _setInputStateAsync(doorSensorPath, "Inactive");

                    if (!closed)
                    {
                        AddLog(
                            level: "Error",
                            eventType: "Held",
                            doorName: doorName,
                            message: "DoorSim could not close the held-open door sensor during stop. Manual cleanup may be required.");
                    }
                }
                catch (Exception ex)
                {
                    AddLog(
                        level: "Error",
                        eventType: "Held",
                        doorName: doorName,
                        message: $"Held-open cleanup could not contact Softwire. Manual cleanup may be required. {ex.Message}");
                }
            }

            ReleaseDoorReservation(
                doorId,
                "Held-open reservation released after simulation stop.",
                level: "Warning");
        }
    }

    // Waits for all currently scheduled Held cleanup tasks to finish.
    // We remove completed tasks first so repeated calls do not keep old completed tasks around forever.
    private async Task WaitForHeldCleanupTasksAsync()
    {
        _heldCleanupTasks.RemoveAll(t => t.IsCompleted);

        if (!_heldCleanupTasks.Any())
            return;

        AddLog(
            level: "Warning",
            eventType: "-",
            doorName: "-",
            message: "Waiting for held-open cleanup tasks to finish.");

        try
        {
            await Task.WhenAll(_heldCleanupTasks);
        }
        catch
        {
            // Individual cleanup tasks already log their own cleanup/failure details.
            // Swallow here so one cleanup issue does not crash the application shell!!!
        }
    }


    /*
      #############################################################################
                                  Log Helpers
      #############################################################################
    */

    // Adds a row to the Auto Mode event log.
    // New entries are inserted at the top so the most recent activity is immediately visible without scrolling.
    // EventNumber is optional because some log messages describe the overall simulation rather than a specific event attempt.
    private void AddLog(string level, string eventType, string doorName, string message, int? eventNumber = null)
    {
        LogEntries.Insert(0, new AutoSimulationLogEntry
        {
            Timestamp = DateTime.Now,
            EventNumber = eventNumber,
            TotalEvents = eventNumber == null ? null : NumberOfEventsValue,
            Level = level,
            EventType = eventType,
            DoorName = doorName,
            Message = message
        });
    }

    // Adds a visual separator row to the log.
    // The log displays newest entries at the top using Insert(0).
    // Therefore this is called at the start of each event. As later log entries for the same event are inserted above it,
    // the separator naturally ends up between this event and the previous event.
    private void AddEventSeparator(int eventNumber)
    {
        LogEntries.Insert(0, new AutoSimulationLogEntry
        {
            Timestamp = DateTime.Now,
            EventNumber = eventNumber,
            TotalEvents = NumberOfEventsValue,
            IsSeparator = true,
            Level = "",
            EventType = "",
            DoorName = "",
            Message = "────────────────────────────────────────────────────────"
        });
    }


    /*
      #############################################################################
                            Private Helper Classes
      #############################################################################
    */

    // Small internal model used by Auto Mode reader selection.
    // Keeping this as a tiny private class avoids passing around loose strings for:
    //      - reader path,
    //      - reader side,
    //      - reader mode,
    //      - whether Card + PIN is required.
    private class AutoReaderSelection
    {
        public string ReaderPath { get; set; } = string.Empty;
        public string Side { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool RequiresCardAndPin { get; set; }

        public string ReaderMode => RequiresCardAndPin
            ? "Card + PIN"
            : "Card only";
    }

    // Small internal model used to track doors temporarily reserved by Auto Mode.
    // Held-open events use this so a door can remain open long enough for Softwire to generate the Door Held event
    // without another Auto Mode event selecting the same door and interrupting the scenario.
    private class AutoDoorReservation
    {
        public string DoorId { get; set; } = string.Empty;
        public string DoorName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime ReservedAtUtc { get; set; }
    }

}