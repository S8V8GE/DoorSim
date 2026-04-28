using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;

namespace DoorSim.ViewModels;

// ViewModel for door selection and single-door display.
//
// Responsible for:
// - Holding the list of doors retrieved from Softwire
// - Tracking which door is currently selected by the user
// - Preserving selection across refreshes (if the door still exists)
// - Exposing simple UI state (HasDoors / HasSelectedDoor)
//
// This ViewModel feeds:
// - DoorSelectorView (dropdown)
// - SingleDoorView (visual representation of the selected door)
public partial class DoorsViewModel : ObservableObject
{
    // Collection of all doors available from Softwire
    [ObservableProperty]
    private ObservableCollection<SoftwireDoor> doors = new ObservableCollection<SoftwireDoor>();

    // The currently selected door (bound to ComboBox)
    [ObservableProperty]
    private SoftwireDoor? selectedDoor;

    // True when at least one door exists
    [ObservableProperty]
    private bool hasDoors;

    // True when a door has been selected
    // Used to control visibility of the SingleDoorView
    [ObservableProperty]
    private bool hasSelectedDoor;

    // Count of doors, for display purposes
    [ObservableProperty]
    private int doorCount;

    // Colors... cause we like colors!
    private static readonly Brush GoodBrush = new SolidColorBrush(Color.FromRgb(40, 200, 120)); // green
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));  // red
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // default

    // Image shown for the selected door.
    // Later this will reflect the real door sensor state from Softwire.
    public string DoorImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            return SelectedDoor.DoorSensorIsOpen
                ? "/Images/Door_Open.png"
                : "/Images/Door_Closed.png";
        }
    }

    // Loads doors into the ViewModel and preserves selection if possible
    public void LoadDoors(IEnumerable<SoftwireDoor> loadedDoors)
    {
        var previousSelectedDoor = SelectedDoor;
        var previousSelectedDoorId = previousSelectedDoor?.Id;

        Doors = new ObservableCollection<SoftwireDoor>(
            loadedDoors.OrderBy(d => d.Name));

        HasDoors = Doors.Any();
        DoorCount = Doors.Count;

        if (!string.IsNullOrWhiteSpace(previousSelectedDoorId))
        {
            var refreshedSelectedDoor = Doors.FirstOrDefault(d => d.Id == previousSelectedDoorId);

            if (refreshedSelectedDoor != null && previousSelectedDoor != null)
            {
                // Preserve fast-polled live state so the image does not flicker
                refreshedSelectedDoor.DoorSensorIsOpen = previousSelectedDoor.DoorSensorIsOpen;

                // Preserve current display state until the 1-second poll updates it
                refreshedSelectedDoor.DoorIsLocked = previousSelectedDoor.DoorIsLocked;
                refreshedSelectedDoor.UnlockedForMaintenance = previousSelectedDoor.UnlockedForMaintenance;
            }

            SelectedDoor = refreshedSelectedDoor;
        }
        else
        {
            SelectedDoor = null;
        }

        OnPropertyChanged(nameof(DoorImagePath));
        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorSensorStatusText));
        OnPropertyChanged(nameof(DoorActionTooltip));
        OnPropertyChanged(nameof(DoorLockStatusColor));
        OnPropertyChanged(nameof(DoorSensorStatusColor));
    }

    // Automatically called when SelectedDoor changes
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        // Update UI flag based on whether a door is selected
        HasSelectedDoor = value != null;

        // Refresh anything that depends on the selected door
        OnPropertyChanged(nameof(DoorImagePath));
        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorSensorStatusText));
        OnPropertyChanged(nameof(DoorActionTooltip));
        OnPropertyChanged(nameof(DoorLockStatusColor));
        OnPropertyChanged(nameof(DoorSensorStatusColor));
    }

    // Updates live state for the selected door and refreshes dependent UI properties
    public void UpdateSelectedDoorState(bool doorIsLocked, bool doorSensorIsOpen)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.DoorIsLocked != doorIsLocked)
        {
            SelectedDoor.DoorIsLocked = doorIsLocked;
            changed = true;
        }

        if (SelectedDoor.DoorSensorIsOpen != doorSensorIsOpen)
        {
            SelectedDoor.DoorSensorIsOpen = doorSensorIsOpen;
            changed = true;
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(DoorImagePath));
        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorSensorStatusText));
        OnPropertyChanged(nameof(DoorActionTooltip));
        OnPropertyChanged(nameof(DoorLockStatusColor));
        OnPropertyChanged(nameof(DoorSensorStatusColor));
    }

    public string DoorLockStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasLock)
                return "No door lock configured";

            if (SelectedDoor.UnlockedForMaintenance)
                return "Maintenance mode";

            return SelectedDoor.DoorIsLocked ? "Locked" : "Unlocked";
        }
    }

    public string DoorSensorStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasDoorSensor)
                return "No door sensor configured";

            return SelectedDoor.DoorSensorIsOpen ? "Open" : "Closed";
        }
    }

    public string DoorActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasDoorSensor)
                return "No door sensor configured";

            return SelectedDoor.DoorSensorIsOpen
                ? "Close door"
                : "Open door";
        }
    }

    public Brush DoorLockStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (!SelectedDoor.HasLock)
                return WarningBrush;

            if (SelectedDoor.UnlockedForMaintenance)
                return WarningBrush;

            return SelectedDoor.DoorIsLocked ? BadBrush : GoodBrush;
        }
    }

    public Brush DoorSensorStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (!SelectedDoor.HasDoorSensor)
                return WarningBrush;

            return SelectedDoor.DoorSensorIsOpen ? BadBrush : GoodBrush;
        }
    }
}