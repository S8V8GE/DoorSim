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
                                    Commands
      #############################################################################
    */

    [RelayCommand(CanExecute = nameof(CanStartSimulation))]
    private void StartSimulation()
    {
        IsSimulationRunning = true;
        SimulationStatus = "Running";

        CompletedEvents = 0;
        FailedAttempts = 0;
        ExecutedNormalEvents = 0;
        ExecutedForcedEvents = 0;
        ExecutedHeldEvents = 0;

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: "Auto Mode started. Real simulation logic will be added next.");

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: $"Settings: {NumberOfEvents} events, {SelectedDelayMode} delay, {SelectedEventProfile}, PIN {GlobalPin}.");
    }

    private bool CanStartSimulation()
    {
        return !IsSimulationRunning && !HasValidationMessage;
    }

    [RelayCommand(CanExecute = nameof(CanStopSimulation))]
    private void StopSimulation()
    {
        IsSimulationRunning = false;
        SimulationStatus = "Stopped";

        AddLog(
            level: "Warning",
            eventType: "-",
            doorName: "-",
            message: "Auto Mode stopped by user.");
    }

    private bool CanStopSimulation()
    {
        return IsSimulationRunning;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();

        AddLog(
            level: "Info",
            eventType: "-",
            doorName: "-",
            message: "Event log cleared.");
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