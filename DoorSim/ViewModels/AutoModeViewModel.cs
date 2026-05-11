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

    public string Subtitle =>
        "Busy site simulation for training, demos, and stress testing.";


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

    // Executes one fake event and updates the running summary.
    //
    // No Softwire commands are sent here yet.
    private async Task ExecuteFakeEventAsync(int eventNumber, string eventType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Tiny delay so the log feels alive and proves the UI remains responsive.
        await Task.Delay(150, cancellationToken);

        switch (eventType)
        {
            case "Normal":
                ExecutedNormalEvents++;
                break;

            case "Forced":
                ExecutedForcedEvents++;
                break;

            case "Held":
                ExecutedHeldEvents++;
                break;
        }

        CompletedEvents++;

        AddLog(
            level: "Success",
            eventType: eventType,
            doorName: "-",
            message: $"Fake {eventType.ToLower()} event completed.",
            eventNumber: eventNumber);
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
}