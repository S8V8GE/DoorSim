using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;

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
    /*
      #############################################################################
                   Observable Properties for Door List and Selection
      #############################################################################
    */

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


    /*
      #############################################################################
                                    Shared brushes
      #############################################################################
    */

    // Shared UI colours used for live hardware status
    private static readonly Brush GoodBrush = new SolidColorBrush(Color.FromRgb(40, 200, 120)); // green
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));  // red
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // default


    /*
      #############################################################################
                           Visibility properties for devices
      #############################################################################
    */

    public Visibility InReaderVisibility =>
        SelectedDoor?.HasReaderSideIn == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutReaderVisibility =>
        SelectedDoor?.HasReaderSideOut == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InRexVisibility =>
        SelectedDoor?.HasRexSideIn == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutRexVisibility =>
        SelectedDoor?.HasRexSideOut == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoSideRexVisibility =>
        SelectedDoor?.HasRexNoSide == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BreakGlassVisibility =>
    SelectedDoor?.HasBreakGlass == true ? Visibility.Visible : Visibility.Collapsed;


    /*
      #############################################################################
                           Layout slot properties for devices
      #############################################################################
    */

    // Left side layout:
    // Column 0 = outside position && Column 1 = closest to the door
    // If an In Reader exists, In REX stays outside. If no In Reader exists, In REX moves closest to the door.
    public int InRexColumn =>
        SelectedDoor?.HasReaderSideIn == true ? 0 : 1;

    // Right side layout:
    // Column 0 = closest to the door && Column 1 = outside position
    // If an Out Reader exists, Out REX stays outside. If no Out Reader exists, Out REX moves closest to the door.
    public int OutRexColumn =>
        SelectedDoor?.HasReaderSideOut == true ? 1 : 0;


    /*
      #############################################################################
                          Image path properties for devices
      #############################################################################
    */

    // TEMP
    public string ReaderImagePath => "/Images/Reader.png";

    // TEMP
    public string BreakGlassImagePath => "/Images/Breakglass_Normal.png";

    // DONE....;
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

    public string InRexImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return "/Images/REX_Normal.png";

            return SelectedDoor.RexSideInIsActive
                ? "/Images/REX_Active.png"
                : "/Images/REX_Normal.png";
        }
    }

    public string OutRexImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return "/Images/REX_Normal.png";

            return SelectedDoor.RexSideOutIsActive
                ? "/Images/REX_Active.png"
                : "/Images/REX_Normal.png";
        }
    }

    public string NoSideRexImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return "/Images/REX_Normal.png";

            return SelectedDoor.RexNoSideIsActive
                ? "/Images/REX_Active.png"
                : "/Images/REX_Normal.png";
        }
    }


    /*
      #############################################################################
                          Status text properties for devices
      #############################################################################
    */

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

            if (SelectedDoor.DoorSensorIsShunted)
                return "Shunted";

            return SelectedDoor.DoorSensorIsOpen ? "Open" : "Closed";
        }
    }

    public string InRexStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexSideIn)
                return "";

            if (SelectedDoor.RexSideInIsShunted)
                return "Shunted";

            return SelectedDoor.RexSideInIsActive ? "Active" : "Normal";
        }
    }

    public string OutRexStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexSideOut)
                return "";

            if (SelectedDoor.RexSideOutIsShunted)
                return "Shunted";

            return SelectedDoor.RexSideOutIsActive ? "Active" : "Normal";
        }
    }

    public string NoSideRexStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexNoSide)
                return "";

            if (SelectedDoor.RexNoSideIsShunted)
                return "Shunted";

            return SelectedDoor.RexNoSideIsActive ? "Active" : "Normal";
        }
    }


    /*
      #############################################################################
                       Status colour properties for devices
      #############################################################################
    */

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

            return SelectedDoor.DoorIsLocked ? GoodBrush : BadBrush;
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

            if (SelectedDoor.DoorSensorIsShunted)
                return WarningBrush;

            return SelectedDoor.DoorSensorIsOpen ? BadBrush : GoodBrush;
        }
    }

    public Brush InRexStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (SelectedDoor.RexSideInIsShunted)
                return WarningBrush;

            return SelectedDoor.RexSideInIsActive ? BadBrush : GoodBrush;
        }
    }

    public Brush OutRexStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (SelectedDoor.RexSideOutIsShunted)
                return WarningBrush;

            return SelectedDoor.RexSideOutIsActive ? BadBrush : GoodBrush;
        }
    }

    public Brush NoSideRexStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (SelectedDoor.RexNoSideIsShunted)
                return WarningBrush;

            return SelectedDoor.RexNoSideIsActive ? BadBrush : GoodBrush;
        }
    }


    /*
      #############################################################################
                         Tooltip properties for devices
      #############################################################################
    */

    public string DoorActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasDoorSensor)
                return "No door sensor configured";

            if (SelectedDoor.DoorSensorIsShunted)
                return "Door sensor is shunted";

            return SelectedDoor.DoorSensorIsOpen
                ? "Close door"
                : "Open door";
        }
    }

    public string InRexActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexSideIn)
                return "";

            if (SelectedDoor.RexSideInIsShunted)
                return "In REX is shunted";

            return SelectedDoor.RexSideInIsActive
                ? "Release In REX"
                : "Press In REX";
        }
    }

    public string OutRexActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexSideOut)
                return "";

            if (SelectedDoor.RexSideOutIsShunted)
                return "Out REX is shunted";

            return SelectedDoor.RexSideOutIsActive
                ? "Release Out REX"
                : "Press Out REX";
        }
    }

    public string NoSideRexActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexNoSide)
                return "";

            if (SelectedDoor.RexNoSideIsShunted)
                return "REX is shunted";

            return SelectedDoor.RexNoSideIsActive
                ? "Release REX"
                : "Press REX";
        }
    }


    /*
      #############################################################################
                         Load doors and preserve selection logic
      #############################################################################
    */

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
                // Preserve fast-polled live state so the image/status does not flicker
                refreshedSelectedDoor.DoorSensorIsOpen = previousSelectedDoor.DoorSensorIsOpen;
                refreshedSelectedDoor.DoorSensorIsShunted = previousSelectedDoor.DoorSensorIsShunted;

                // Preserve current display state until the 1-second poll updates it
                refreshedSelectedDoor.DoorIsLocked = previousSelectedDoor.DoorIsLocked;
                refreshedSelectedDoor.UnlockedForMaintenance = previousSelectedDoor.UnlockedForMaintenance;

                // Preserve In REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexSideInIsActive = previousSelectedDoor.RexSideInIsActive;
                refreshedSelectedDoor.RexSideInIsShunted = previousSelectedDoor.RexSideInIsShunted;

                // Preserve Out REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexSideOutIsActive = previousSelectedDoor.RexSideOutIsActive;
                refreshedSelectedDoor.RexSideOutIsShunted = previousSelectedDoor.RexSideOutIsShunted;

                // Preserve No-side REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexNoSideIsActive = previousSelectedDoor.RexNoSideIsActive;
                refreshedSelectedDoor.RexNoSideIsShunted = previousSelectedDoor.RexNoSideIsShunted;
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
        OnPropertyChanged(nameof(InReaderVisibility));
        OnPropertyChanged(nameof(OutReaderVisibility));
        OnPropertyChanged(nameof(InRexVisibility));
        OnPropertyChanged(nameof(OutRexVisibility));
        OnPropertyChanged(nameof(NoSideRexVisibility));
        OnPropertyChanged(nameof(ReaderImagePath));
        OnPropertyChanged(nameof(BreakGlassVisibility));
        OnPropertyChanged(nameof(BreakGlassImagePath));
        OnPropertyChanged(nameof(InRexColumn));
        OnPropertyChanged(nameof(OutRexColumn));
        OnPropertyChanged(nameof(InRexImagePath));
        OnPropertyChanged(nameof(InRexStatusText));
        OnPropertyChanged(nameof(InRexStatusColor));
        OnPropertyChanged(nameof(InRexActionTooltip));
        OnPropertyChanged(nameof(OutRexImagePath));
        OnPropertyChanged(nameof(OutRexStatusText));
        OnPropertyChanged(nameof(OutRexStatusColor));
        OnPropertyChanged(nameof(OutRexActionTooltip));
        OnPropertyChanged(nameof(NoSideRexImagePath));
        OnPropertyChanged(nameof(NoSideRexStatusText));
        OnPropertyChanged(nameof(NoSideRexStatusColor));
        OnPropertyChanged(nameof(NoSideRexActionTooltip));
    }


    /*
      #############################################################################
             Update methods for live state changes with optimised UI refresh
      #############################################################################
    */

    // Updates live state for the selected door and refreshes dependent UI properties
    public void UpdateSelectedDoorState(bool doorIsLocked, bool doorSensorIsOpen, bool doorSensorIsShunted)
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

        if (SelectedDoor.DoorSensorIsShunted != doorSensorIsShunted)
        {
            SelectedDoor.DoorSensorIsShunted = doorSensorIsShunted;
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

    // Updates live state for the In REX and refreshes dependent UI properties
    public void UpdateInRexState(bool isActive, bool isShunted)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.RexSideInIsActive != isActive)
        {
            SelectedDoor.RexSideInIsActive = isActive;
            changed = true;
        }

        if (SelectedDoor.RexSideInIsShunted != isShunted)
        {
            SelectedDoor.RexSideInIsShunted = isShunted;
            changed = true;
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(InRexImagePath));
        OnPropertyChanged(nameof(InRexStatusText));
        OnPropertyChanged(nameof(InRexStatusColor));
        OnPropertyChanged(nameof(InRexActionTooltip));
    }

    // Updates live state for the Out REX and refreshes dependent UI properties
    public void UpdateOutRexState(bool isActive, bool isShunted)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.RexSideOutIsActive != isActive)
        {
            SelectedDoor.RexSideOutIsActive = isActive;
            changed = true;
        }

        if (SelectedDoor.RexSideOutIsShunted != isShunted)
        {
            SelectedDoor.RexSideOutIsShunted = isShunted;
            changed = true;
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(OutRexImagePath));
        OnPropertyChanged(nameof(OutRexStatusText));
        OnPropertyChanged(nameof(OutRexStatusColor));
        OnPropertyChanged(nameof(OutRexActionTooltip));
    }

    // Updates live state for the No Side REX and refreshes dependent UI properties
    public void UpdateNoSideRexState(bool isActive, bool isShunted)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.RexNoSideIsActive != isActive)
        {
            SelectedDoor.RexNoSideIsActive = isActive;
            changed = true;
        }

        if (SelectedDoor.RexNoSideIsShunted != isShunted)
        {
            SelectedDoor.RexNoSideIsShunted = isShunted;
            changed = true;
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(NoSideRexImagePath));
        OnPropertyChanged(nameof(NoSideRexStatusText));
        OnPropertyChanged(nameof(NoSideRexStatusColor));
        OnPropertyChanged(nameof(NoSideRexActionTooltip));
    }
    
}