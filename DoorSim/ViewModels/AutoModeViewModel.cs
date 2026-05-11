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

    // Tracks doors that Auto Mode must not use temporarily.
    //
    // This will be used by real Held events. When Auto Mode leaves a door open long
    // enough to generate a Door Held event, that door must not be selected again by
    // another Normal/Forced/Held event until cleanup has closed the sensor.
    //
    // Key   = Softwire door Id
    // Value = reservation details for logging/debugging
    private readonly Dictionary<string, AutoDoorReservation> _reservedDoors = new();

    // Tracks background cleanup tasks for Held events.
    //
    // Held events deliberately leave a door sensor open long enough for Softwire to
    // generate a door-held-open event. The main simulation loop continues meanwhile,
    // so cleanup happens in the background.
    private readonly List<Task> _heldCleanupTasks = new();


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

        // Start each run with a clean reservation list.
        // If a previous run was stopped or completed, we do not want stale reservations
        // preventing doors from being selected in the next run.
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

            // Held events may still have background cleanup tasks running after the last
            // requested event has been generated. Before the run fully ends, wait for those
            // cleanup tasks so Auto Mode does not leave simulated door sensors open.
            await WaitForHeldCleanupTasksAsync();

            _reservedDoors.Clear();
            _heldCleanupTasks.Clear();

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
                          Fake Simulation Engine
      #############################################################################
    */

    // Runs a fake timed simulation loop.
    //
    // This proves the Auto Mode engine before we connect it to real Softwire actions.
    // Later, ExecuteFakeEventAsync(...) will be replaced with real door/cardholder/input logic.
    private async Task RunFakeSimulationAsync(CancellationToken cancellationToken)
    {
        // CompletedEvents now means successfully executed events only.
        //
        // Failed/skipped attempts increment FailedAttempts but do not consume one of
        // the requested events. This mirrors the original PowerShell PoC behaviour:
        // if an event cannot be executed because the system changed or no suitable
        // door/cardholder is available, Auto Mode retries until the requested number
        // of real events has been generated.
        while (CompletedEvents < NumberOfEvents)
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
            await ExecuteNormalEventAsync(eventNumber, cancellationToken);
            return;
        }

        if (eventType == "Forced")
        {
            await ExecuteForcedDoorEventAsync(eventNumber, cancellationToken);
            return;
        }

        await ExecuteHeldRexEventAsync(eventNumber, cancellationToken);

    }

    // Executes a normal access event.
    //
    // Normal events can be generated by either:
    //      - a reader/cardholder action,
    //      - a REX input action.
    //
    // Method selection is intentionally weighted:
    //      - If a door has readers and usable REX inputs, prefer readers 80% of the time.
    //      - If only readers exist, use a reader.
    //      - If only usable REX inputs exist, use REX.
    //
    // Reader execution is still a placeholder in this step. The next step will add
    // actual cardholder selection, card swipe, Card + PIN support, and decision checking.
    private async Task ExecuteNormalEventAsync(int eventNumber, CancellationToken cancellationToken)
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
            var readerSelection = SelectReaderForNormalEvent(selectedDoor);

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

    // Executes a real normal access event using a reader and cardholder credential.
    //
    // Sequence:
    //      1. Select a suitable cardholder.
    //      2. Swipe the card credential at the selected reader.
    //      3. If the reader is Card + PIN, send the configured Global PIN.
    //      4. Poll Softwire briefly to see whether the door unlocks or reports denial.
    //      5. If the door unlocks and has a sensor, open and close the door sensor.
    //
    // Important Card + PIN rule:
    //      For Card + PIN readers, Auto Mode only selects cardholders where HasPin is true.
    //      This mirrors the PowerShell PoC behaviour and avoids deliberately selecting
    //      cardholders that cannot complete a Card + PIN transaction.
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
                var pinSucceeded = await _swipeWiegand26Async(
                    readerSelection.ReaderPath,
                    0,
                    int.Parse(GlobalPin));

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

    // Executes a real held-open event using REX.
    //
    // This first Held implementation deliberately uses REX only. Reader-held events
    // will be added later once the held-open reservation/cleanup behaviour is proven.
    //
    // Sequence:
    //      1. Select a suitable held-capable door.
    //      2. If every suitable held door is reserved, wait briefly for one to become free.
    //      3. Reserve the selected door.
    //      4. Activate and release REX.
    //      5. Open the door sensor and leave it open.
    //      6. Start background cleanup to close the sensor after DoorHeldTime + buffer.
    //
    // Important:
    //      The event is counted as executed once the door sensor has been opened and
    //      cleanup has been scheduled. The door remains reserved until cleanup closes
    //      the sensor or discovers the door was deleted.
    private async Task ExecuteHeldRexEventAsync(int eventNumber, CancellationToken cancellationToken)
    {
        if (_getDoorsAsync == null || _setInputStateAsync == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Error",
                eventType: "Held",
                doorName: "-",
                message: "Held REX event failed because Auto Mode dependencies are not configured.",
                eventNumber: eventNumber);

            return;
        }

        var selectedDoor = await WaitForHeldRexDoorCandidateAsync(eventNumber, cancellationToken);

        if (selectedDoor == null)
        {
            FailedAttempts++;

            AddLog(
                level: "Warning",
                eventType: "Held",
                doorName: "-",
                message: "No suitable held REX door became available. Door must be locked, not in maintenance, have a door sensor, have 'Door Held' configured, and support AutoUnlockOnRex.",
                eventNumber: eventNumber);

            return;
        }

        var rexPath = SelectRexPath(selectedDoor);
        var rexDescription = GetRexDescription(selectedDoor, rexPath);

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

            var cleanupDelaySeconds = GetHeldCleanupDelaySeconds(selectedDoor);

            AddLog(
                level: "Info",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: $"Door will remain open for approximately {cleanupDelaySeconds} second(s) before cleanup.",
                eventNumber: eventNumber);

            var cleanupTask = CleanupHeldDoorLaterAsync(
                doorId: selectedDoor.Id,
                doorName: selectedDoor.Name,
                doorSensorPath: selectedDoor.DoorSensorDevicePath,
                delaySeconds: cleanupDelaySeconds,
                cancellationToken: cancellationToken);

            _heldCleanupTasks.Add(cleanupTask);

            ExecutedHeldEvents++;
            CompletedEvents++;

            AddLog(
                level: "Success",
                eventType: "Held",
                doorName: selectedDoor.Name,
                message: "Held-open event generated. Door reserved until cleanup closes the sensor.",
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
            // If cancellation/failure happened before background cleanup was scheduled,
            // clean up immediately here.
            if (cleanupFailed || eventWasCancelled)
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

    // Selects a random door suitable for a normal event.
    //
    // A normal event can use either:
    //      - a reader,
    //      - a REX input, but only when AutoUnlockOnRex is enabled.
    //
    // The door must be locked and not in maintenance mode so the simulation represents
    // a meaningful access attempt rather than interacting with an already-unlocked door.
    private SoftwireDoor? SelectNormalDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var doorList = doors.ToList();

        // If a reserved door was deleted while Auto Mode is running, release the
        // reservation and move on cleanly. This prevents a stale reservation from
        // blocking future event selection.
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

    // Returns true when the door has at least one reader path Auto Mode can use.
    private static bool HasUsableReader(SoftwireDoor door)
    {
        return !string.IsNullOrWhiteSpace(door.ReaderSideInDevicePath) ||
               !string.IsNullOrWhiteSpace(door.ReaderSideOutDevicePath);
    }

    // Returns true when the door has at least one usable REX input and the door is
    // configured to unlock on REX.
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

    // Selects one reader from the supplied door.
    //
    // If both In and Out readers exist, selection is 50/50. This gives Auto Mode a
    // balanced mix of entry and exit reads during long simulations.
    private AutoReaderSelection? SelectReaderForNormalEvent(SoftwireDoor door)
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

    // Selects a random cardholder suitable for the selected reader.
    //
    // Card-only reader:
    //      Any cardholder with a usable card credential can be used.
    //
    // Card + PIN reader:
    //      Only cardholders with a usable card credential AND HasPin = true are used.
    //      The actual PIN sent is the Auto Mode Global PIN, so the training system
    //      should be configured with matching cardholder PINs.
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
    //
    // Reader decisions and door unlock state are reported on the door object.
    // Rather than relying on the manual UI feedback system, Auto Mode refreshes the
    // door directly and uses the latest decision/lock state for logging and behaviour.
    //
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
        var doorList = doors.ToList();

        // Reserved doors are deliberately being held by Auto Mode, usually because a
        // Held event has opened the sensor and is waiting for Softwire to generate a
        // Door Held event. Do not use those doors for forced events.
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

    // Waits for a held-capable REX door to become available.
    //
    // If all suitable Held doors are currently reserved, Auto Mode waits briefly
    // rather than immediately failing. This matters when multiple Held events occur
    // close together and every held-capable door is already waiting for cleanup.
    private async Task<SoftwireDoor?> WaitForHeldRexDoorCandidateAsync(int eventNumber, CancellationToken cancellationToken)
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

            var candidate = SelectHeldRexDoorCandidate(doorList);

            if (candidate != null)
                return candidate;

            var hasHeldCapableDoors = doorList.Any(IsHeldRexCapableDoor);
            var allHeldCapableDoorsReserved =
                hasHeldCapableDoors &&
                doorList
                    .Where(IsHeldRexCapableDoor)
                    .All(d => IsDoorReserved(d.Id));

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

    // Selects a random door suitable for a held-open REX event.
    private SoftwireDoor? SelectHeldRexDoorCandidate(IEnumerable<SoftwireDoor> doors)
    {
        var candidates = doors
            .Where(IsHeldRexCapableDoor)
            .Where(d => !IsDoorReserved(d.Id))
            .ToList();

        if (!candidates.Any())
            return null;

        return candidates[_random.Next(candidates.Count)];
    }

    // Returns true when a door can generate a held-open event using REX.
    private static bool IsHeldRexCapableDoor(SoftwireDoor door)
    {
        return door.DoorIsLocked &&
               !door.UnlockedForMaintenance &&
               door.HasDoorSensor &&
               !string.IsNullOrWhiteSpace(door.DoorSensorDevicePath) &&
               door.DoorHeldTimeSeconds > 0 &&
               !door.IgnoreHeldOpenWhenUnlocked &&
               HasUsableAutoUnlockRex(door);
    }

    // Returns the delay before Auto Mode should close a held-open door.
    private static int GetHeldCleanupDelaySeconds(SoftwireDoor door)
    {
        const int heldEventBufferSeconds = 5;

        return Math.Max(door.DoorHeldTimeSeconds + heldEventBufferSeconds, 1);
    }

    // Closes a held-open door sensor after the configured held-open delay.
    //
    // This runs in the background so Auto Mode can continue generating other events
    // while the selected door remains open long enough for Softwire to raise the
    // held-open event.
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
                AddLog(
                    level: "Info",
                    eventType: "Held",
                    doorName: doorName,
                    message: "Simulation stopped. Closing held-open door sensor.");

                await _setInputStateAsync(doorSensorPath, "Inactive");
            }

            ReleaseDoorReservation(
                doorId,
                "Held-open reservation released after simulation stop.",
                level: "Warning");
        }
    }

    // Waits for all currently scheduled Held cleanup tasks to finish.
    //
    // We remove completed tasks first so repeated calls do not keep old completed
    // tasks around forever.
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
            // Swallow here so one cleanup issue does not crash the application shell.
        }
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

    // Small internal model used by Auto Mode reader selection.
    //
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

    // Small internal model used to track doors that Auto Mode has temporarily
    // reserved.
    //
    // Held events will use this so a door can remain open long enough for Softwire
    // to generate the door-held-open event without another Auto Mode event selecting
    // the same door and interrupting the scenario.
    private class AutoDoorReservation
    {
        public string DoorId { get; set; } = string.Empty;
        public string DoorName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public DateTime ReservedAtUtc { get; set; }
    }

}