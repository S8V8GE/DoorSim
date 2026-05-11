using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace DoorSim.ViewModels;

// ViewModel for Auto Mode.
//
// Auto Mode will eventually run automated Softwire simulation scenarios such as:
// - normal access events
// - door forced events
// - door held open events
//
// This version provides the settings UI, running summary, and event log.
// Real Softwire simulation logic will be added later.
public partial class AutoModeViewModel : ObservableObject
{
    /*
      #############################################################################
                          Simulation Engine State
      #############################################################################
    */

    // Used to stop the running simulation loop safely.
    private CancellationTokenSource? _simulationCancellation;

    // Random number generator used for delays and event type selection.
    private readonly Random _random = new Random();


    /*
      #############################################################################
                      Simulation Dependencies / Callbacks
      #############################################################################
    */

    // Auto Mode does not own SoftwireService directly.
    //
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

    public string Subtitle => "Busy site simulation for training, demos, and stress testing.";


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
    private int minimumDelaySeconds = 3;

    [ObservableProperty]
    private int maximumDelaySeconds = 7;

    [ObservableProperty]
    private int numberOfEvents = 100;

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

    // User-facing validation message shown under the Auto Mode settings.
    // Empty string means the current settings are valid.
    public string ValidationMessage
    {
        get
        {
            if (NumberOfEvents < 1 || NumberOfEvents > 9999)
                return "Events must be a number between 1 and 9999.";

            if (MinimumDelaySeconds < 0 || MinimumDelaySeconds > 60)
                return "Minimum delay must be between 0 and 60 seconds.";

            if (MaximumDelaySeconds < 0 || MaximumDelaySeconds > 120)
                return "Maximum delay must be between 0 and 120 seconds.";

            if (MaximumDelaySeconds < MinimumDelaySeconds)
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
                                Running State
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
            MinimumDelaySeconds = 0;
            MaximumDelaySeconds = 0;
        }
        else if (value == "Relaxed")
        {
            MinimumDelaySeconds = 3;
            MaximumDelaySeconds = 7;
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

    partial void OnNumberOfEventsChanged(int value)
    {
        RefreshValidationState();
    }

    partial void OnMinimumDelaySecondsChanged(int value)
    {
        RefreshValidationState();
    }

    partial void OnMaximumDelaySecondsChanged(int value)
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
    //
    // MainViewModel owns the actual services and live data sources. Auto Mode only receives the specific actions it needs:
    //
    //      - load current doors,
    //      - read loaded cardholders,
    //      - read/set input state,
    //      - swipe card credentials,
    //      - send PIN values.
    //
    // This avoids making AutoModeViewModel responsible for Softwire login/session handling and keeps the design similar to the interlocking controls.
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

    [RelayCommand(CanExecute = nameof(CanStartSimulation))]
    private async Task StartSimulationAsync()
    {
        IsSimulationRunning = true;
        SimulationStatus = "Running";


        CompletedEvents = 0;
        FailedAttempts = 0;
        ExecutedNormalEvents = 0;
        ExecutedForcedEvents = 0;
        ExecutedHeldEvents = 0;

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
            message: $"Settings: {NumberOfEvents} events, {SelectedDelayMode} delay, {SelectedEventProfile}, PIN {GlobalPin}.");

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: "Simulation dependencies configured. Running fake simulation loop.");

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
            await RunFakeSimulationAsync(_simulationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            AddLog(
                level: "Warning",
                eventType: "-",
                doorName: "-",
                message: "Auto Mode stopped by user.");
        }
        finally
        {
            _simulationCancellation?.Dispose();
            _simulationCancellation = null;

            IsSimulationRunning = false;

            if (CompletedEvents >= NumberOfEvents)
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
                          Fake Simulation Engine
  #############################################################################
*/

    // Runs a fake timed simulation loop.
    //
    // This proves the Auto Mode engine before we connect it to real Softwire actions.
    // Later, ExecuteFakeEventAsync(...) will be replaced with real door/cardholder/input logic.
    private async Task RunFakeSimulationAsync(CancellationToken cancellationToken)
    {
        for (var eventNumber = 1; eventNumber <= NumberOfEvents; eventNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            await ExecuteFakeEventAsync(eventNumber, eventType, cancellationToken);
        }
    }

    // Executes one simulation event.
    //
    // Current implementation status:
    //      - Normal events are real Softwire REX events.
    //      - Forced events are real Softwire door sensor events.
    //      - Held events are still fake placeholders.
    //
    // We are intentionally replacing fake events one type at a time so Auto Mode
    // remains stable while the real simulation logic grows.
    private async Task ExecuteFakeEventAsync(int eventNumber, string eventType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (eventType == "Normal")
        {
            await ExecuteNormalRexEventAsync(eventNumber, cancellationToken);
            return;
        }

        if (eventType == "Forced")
        {
            await ExecuteForcedDoorEventAsync(eventNumber, cancellationToken);
            return;
        }

        // Held events are still fake for now. They will become real later because
        // they need reservation/cleanup logic to keep the door open long enough for
        // Softwire to generate a door-held-open event.
        await Task.Delay(150, cancellationToken);

        ExecutedHeldEvents++;
        CompletedEvents++;

        AddLog(
            level: "Success",
            eventType: eventType,
            doorName: "-",
            message: "Fake held event completed.",
            eventNumber: eventNumber);
    }

    // Executes a real normal access event using a REX input.
    //
    // This is the first real "Normal" event implementation. It deliberately uses REX
    // before reader/cardholder logic because it proves the normal door lifecycle
    // without access-decision/Card+PIN complexity.
    //
    // Sequence:
    //      1. Find a suitable locked door where REX can unlock the door.
    //      2. Activate one available REX input.
    //      3. Release the REX input.
    //      4. If the door has a sensor, open and close the door sensor.
    //      5. Log success/failure.
    //
    // Important cleanup rule:
    //      If Auto Mode activates a REX or opens a door sensor, it must make a best
    //      effort to restore them to Inactive even if the trainer presses Stop.
    private async Task ExecuteNormalRexEventAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null || _setInputStateAsync == null)
        {
            FailedAttempts++;
            CompletedEvents++;

            AddLog(
                level: "Error",
                eventType: "Normal",
                doorName: "-",
                message: "Normal REX event failed because Auto Mode dependencies are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var doors = await _getDoorsAsync();

        var selectedDoor = SelectNormalRexDoorCandidate(doors);

        if (selectedDoor == null)
        {
            FailedAttempts++;
            CompletedEvents++;

            AddLog(
                level: "Warning",
                eventType: "Normal",
                doorName: "-",
                message: "No suitable normal REX candidate found. Door must be locked, not in maintenance, 'Unlock On Rex' enabled, and have a REX input.",
                eventNumber: eventNumber);

            return;
        }

        var rexPath = SelectRexPath(selectedDoor);
        var rexDescription = GetRexDescription(selectedDoor, rexPath);

        if (string.IsNullOrWhiteSpace(rexPath))
        {
            FailedAttempts++;
            CompletedEvents++;

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
                CompletedEvents++;

                AddLog(
                    level: "Error",
                    eventType: "Normal",
                    doorName: selectedDoor.Name,
                    message: $"Failed to activate {rexDescription}.",
                    eventNumber: eventNumber);

                return;
            }

            rexWasActivated = true;

            // Give Softwire a brief moment to process the REX activation and unlock
            // the door. In testing, Softwire reacts quickly, so one second is enough.
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
                    CompletedEvents++;

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
            CompletedEvents++;
            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;
            CompletedEvents++;

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

    // Selects a random door suitable for a normal REX event.
    //
    // For this first Normal implementation, we only use REX. Reader/cardholder normal events will be added later.
    //
    // A normal REX candidate must:
    //      - be locked,
    //      - not be unlocked for maintenance,
    //      - allow REX to unlock the door,
    //      - have at least one configured REX input.
    private SoftwireDoor? SelectNormalRexDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var candidates = doors
            .Where(d => d.DoorIsLocked)
            .Where(d => !d.UnlockedForMaintenance)
            .Where(d => d.AutoUnlockOnRex)
            .Where(HasUsableRex)
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }

    // Returns true when the door has at least one REX input path Auto Mode can use.
    private static bool HasUsableRex(SoftwireDoor door)
    {
        return !string.IsNullOrWhiteSpace(door.RexSideInDevicePath) ||
               !string.IsNullOrWhiteSpace(door.RexSideOutDevicePath) ||
               !string.IsNullOrWhiteSpace(door.RexNoSideDevicePath);
    }

    // Selects one available REX path from the supplied door.
    //
    // If more than one REX exists, a random one is chosen so repeated Auto Mode runs
    // do not always exercise the same side.
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

    // Executes a real forced-door event against Softwire.
    //
    // A forced-door event simulates the door sensor opening while the door is still
    // locked. If the door is configured to enforce forced-open events, Softwire
    // should generate the corresponding door forced event.
    //
    // Important cleanup rule:
    //      If Auto Mode opens a door sensor, it must make a best effort to close it
    //      again, even if the trainer presses Stop while the event is running.
    //
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
            CompletedEvents++;

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
            CompletedEvents++;

            AddLog(
                level: "Warning",
                eventType: "Forced",
                doorName: "-",
                message: "No suitable forced-door candidate found. Door must be locked, not in maintenance, have a door sensor, and have forced-open enforcement enabled.",
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
                CompletedEvents++;

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
            // One second is enough to generate the forced-door condition without
            // slowing the simulation unnecessarily.
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
            CompletedEvents++;

            return;
        }

        if (eventWasCancelled)
        {
            FailedAttempts++;
            CompletedEvents++;

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

    // Selects a random door suitable for a forced-door event.
    //
    // A forced-door event should only use doors where opening the door sensor without
    // an unlock is meaningful. Therefore the door must:
    //      - be locked,
    //      - not be unlocked for maintenance,
    //      - have a door sensor,
    //      - have a valid door sensor device path,
    //      - have forced-open enforcement enabled.
    private SoftwireDoor? SelectForcedDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var candidates = doors
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

    // Returns a random delay between the configured minimum and maximum values.
    private int GetRandomDelaySeconds()
    {
        if (MaximumDelaySeconds <= MinimumDelaySeconds)
            return MinimumDelaySeconds;

        return _random.Next(MinimumDelaySeconds, MaximumDelaySeconds + 1);
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
                                  Log Helpers
      #############################################################################
    */

    private void AddLog(string level, string eventType, string doorName, string message, int? eventNumber = null)
    {
        LogEntries.Insert(0, new AutoSimulationLogEntry
        {
            Timestamp = DateTime.Now,
            EventNumber = eventNumber,
            TotalEvents = eventNumber == null ? null : NumberOfEvents,
            Level = level,
            EventType = eventType,
            DoorName = doorName,
            Message = message
        });
    }

    // Adds a visual separator row to the log.
    //
    // The log displays newest entries at the top using Insert(0).
    // Therefore this is called at the start of each event. As later log entries for
    // the same event are inserted above it, the separator naturally ends up between
    // this event and the previous event.
    private void AddEventSeparator(int eventNumber)
    {
        LogEntries.Insert(0, new AutoSimulationLogEntry
        {
            Timestamp = DateTime.Now,
            EventNumber = eventNumber,
            TotalEvents = NumberOfEvents,
            IsSeparator = true,
            Level = "",
            EventType = "",
            DoorName = "",
            Message = "────────────────────────────────────────────────────────"
        });
    }

}