using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows;

namespace DoorSim.ViewModels;

// ViewModel for one interactive door panel.
//
// Responsibilities:
//      - Hold the available Softwire doors and current SelectedDoor.
//      - Expose calculated UI state for the door, readers, REX inputs, and breakglass.
//      - Preserve live UI state across slow door-list refreshes.
//      - Provide update methods used by polling and UI interactions.
//      - Show temporary access decision feedback under readers and REX devices.
//
// Used by:
//      - Single Door View via MainViewModel.Doors.
//      - Two Door View via LeftDoorPanel.DoorState and RightDoorPanel.DoorState.
public partial class DoorsViewModel : ObservableObject
{
    /*
      #############################################################################
                          Door List and Selection State
      #############################################################################
    */

    // Collection of all doors available to this panel (from Softwire)
    //      - In Single Door View this is the full Softwire door list.
    //      - In Two Door View this may be a filtered list to prevent selecting the same door on both sides.
    [ObservableProperty]
    private ObservableCollection<SoftwireDoor> doors = new ObservableCollection<SoftwireDoor>();

    // The currently selected door (bound to ComboBox)
    [ObservableProperty]
    private SoftwireDoor? selectedDoor;

    // True when at least one door exists
    [ObservableProperty]
    private bool hasDoors;

    // True when a door has been selected. Used by the view to switch between the empty-state message and the live door hardware panel.
    [ObservableProperty]
    private bool hasSelectedDoor;

    // Count of doors, for display purposes
    [ObservableProperty]
    private int doorCount;

    // Message shown inside DoorPanelView when no door is currently selected.
    [ObservableProperty]
    private string emptyDoorPanelMessage = "Please select a door";


    /*
      #############################################################################
                          Temporary Interaction State
      #############################################################################
    */

    // Short-lived UI states caused by direct user interaction. These override normal reader status briefly, for example:
    //      - cardholder being dragged over a reader,
    //      - PIN just sent to a reader.

    // True only while a cardholder is being dragged over the In Reader
    [ObservableProperty]
    private bool isCardholderOverInReader;

    // True while a cardholder is being dragged over the Out Reader
    [ObservableProperty]
    private bool isCardholderOverOutReader;

    // True briefly after a PIN is sent through the In Reader
    [ObservableProperty]
    private bool inReaderPinSent;

    // True briefly after a PIN is sent through the Out Reader
    [ObservableProperty]
    private bool outReaderPinSent;


    /*
      #############################################################################
                          Temporary Access Feedback State
      #############################################################################
    */

    // Temporary feedback shown under devices after an access action.
    // These values intentionally override normal live status text for a short time, for example "Access granted", "Access denied", or "PIN sent".

    // Reader access decision feedback. These values are shown briefly after Softwire reports an access decision.
    [ObservableProperty]
    private string inReaderDecisionText = string.Empty;

    [ObservableProperty]
    private string outReaderDecisionText = string.Empty;

    // Reader access decision result. These control the feedback colour without relying on text comparison.
    [ObservableProperty]
    private bool inReaderDecisionIsGranted;

    [ObservableProperty]
    private bool inReaderDecisionIsDenied;

    [ObservableProperty]
    private bool outReaderDecisionIsGranted;

    [ObservableProperty]
    private bool outReaderDecisionIsDenied;

    // REX access decision feedback. Shown briefly after the REX is pressed.
    [ObservableProperty]
    private string inRexDecisionText = string.Empty;

    [ObservableProperty]
    private string outRexDecisionText = string.Empty;

    [ObservableProperty]
    private string noSideRexDecisionText = string.Empty;


    /*
      #############################################################################
                           Event for reader LED changes
      #############################################################################
    */

    // Raised when a reader LED leaves its normal idle state. DoorPanelView listens to this so it can play reader-alert audio for events such as door forced / held open.
    // Access granted/denied audio is not handled here. That is routed through MainViewModel because it needs the pending reader/cardholder context.
    public event Action? ReaderLedChanged;


    /*
      #############################################################################
                                   Shared UI brushes
      #############################################################################
    */

    // Shared UI colours used by calculated status/LED properties (for live hardware status) - cause we love colours don't we Barry!
    private static readonly Brush GoodBrush = new SolidColorBrush(Color.FromRgb(40, 200, 120)); // green
    private static readonly Brush BadBrush = new SolidColorBrush(Color.FromRgb(220, 80, 80));  // red
    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(255, 165, 0)); // orange
    private static readonly Brush NeutralBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // default
    private static readonly Brush DragOverBrush = new SolidColorBrush(Color.FromRgb(0, 122, 204)); // blue


    /*
      #############################################################################
                                  Device Visibility
      #############################################################################
    */

    // Readers
    public Visibility InReaderVisibility =>
        SelectedDoor?.HasReaderSideIn == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutReaderVisibility =>
        SelectedDoor?.HasReaderSideOut == true ? Visibility.Visible : Visibility.Collapsed;
    
    // REX
    public Visibility InRexVisibility =>
        SelectedDoor?.HasRexSideIn == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutRexVisibility =>
        SelectedDoor?.HasRexSideOut == true ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoSideRexVisibility =>
        SelectedDoor?.HasRexNoSide == true ? Visibility.Visible : Visibility.Collapsed;

    // Breakglass / Manual station
    public Visibility BreakGlassVisibility =>
        SelectedDoor?.HasBreakGlass == true ? Visibility.Visible : Visibility.Collapsed;


    /*
      #############################################################################
                                Device Layout Slots
      #############################################################################
    */

    // Left side device layout:
    //      - Column 0 = outside position.
    //      - Column 1 = closest to the door.
    //      - If an In Reader exists, the In REX stays outside.
    //      - If no In Reader exists, the In REX moves into the near-door slot.
    public int InRexColumn =>
        SelectedDoor?.HasReaderSideIn == true ? 0 : 1;


    // Right side device layout:
    //      - Column 0 = closest to the door.
    //      - Column 1 = outside position.
    //      - If an Out Reader exists, the Out REX stays outside.
    //      - If no Out Reader exists, the Out REX moves into the near-door slot.
    public int OutRexColumn =>
        SelectedDoor?.HasReaderSideOut == true ? 1 : 0;


    /*
      #############################################################################
                                 Device Image Paths
      #############################################################################
    */

    // Door
    public string DoorImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return null;

            return SelectedDoor.DoorSensorIsOpen
                ? "/Images/Door_Open.png"
                : "/Images/Door_Closed.png";
        }
    }

    // Readers
    // NOTE: Reader image is static. Reader state is shown by the LED overlay and text, not by changing the base reader image.
    public string ReaderImagePath => "/Images/Reader.png";

    // REX's
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

    // Breakglass / Manual station
    public string BreakGlassImagePath
    {
        get
        {
            if (SelectedDoor == null)
                return "/Images/Breakglass_Normal.png";

            return SelectedDoor.BreakGlassIsActive
                ? "/Images/Breakglass_Active.png"
                : "/Images/Breakglass_Normal.png";
        }
    }


    /*
      #############################################################################
                                 Device Status Text
      #############################################################################
    */

    // Status text properties are priority-based!
    // Temporary feedback such as "Access granted", "Access denied", "PIN sent", or "Card present" appears before normal live states such as Online/Offline.

    // Lock
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

            if (SelectedDoor.BreakGlassIsActive)
            {
                var lockState = SelectedDoor.DoorIsLocked ? "Locked" : "Unlocked";
                return $"No power ({lockState})";
            }

            return SelectedDoor.DoorIsLocked ? "Locked" : "Unlocked";
        }
    }

    // Door Sensor
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

    // Readers
    public string InReaderStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasReaderSideIn)
                return "";

            if (!string.IsNullOrWhiteSpace(InReaderDecisionText))
                return InReaderDecisionText;

            if (InReaderPinSent)
                return "PIN sent";

            if (IsCardholderOverInReader)
                return "Card present";

            if (SelectedDoor.InReaderIsShunted)
                return "Shunted";

            return SelectedDoor.InReaderIsOnline ? "Online" : "Offline";
        }
    }

    public string OutReaderStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasReaderSideOut)
                return "";

            if (!string.IsNullOrWhiteSpace(OutReaderDecisionText))
                return OutReaderDecisionText;

            if (OutReaderPinSent)
                return "PIN sent";

            if (IsCardholderOverOutReader)
                return "Card present";

            if (SelectedDoor.OutReaderIsShunted)
                return "Shunted";

            return SelectedDoor.OutReaderIsOnline ? "Online" : "Offline";
        }
    }

    // REX's
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

            if (!string.IsNullOrWhiteSpace(InRexDecisionText))
                return InRexDecisionText;

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

            if (!string.IsNullOrWhiteSpace(OutRexDecisionText))
                return OutRexDecisionText;

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

            if (!string.IsNullOrWhiteSpace(NoSideRexDecisionText))
                return NoSideRexDecisionText;

            return SelectedDoor.RexNoSideIsActive ? "Active" : "Normal";
        }
    }

    // Breakglass / Manual station
    public string BreakGlassStatusText
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasBreakGlass)
                return "";

            if (SelectedDoor.BreakGlassIsShunted)
                return "Shunted";

            return SelectedDoor.BreakGlassIsActive ? "Active" : "Normal";
        }
    }


    /*
      #############################################################################
                       Device Status Colours and LED Brushes
      #############################################################################
    */

    // These properties mirror the status-text priority rules so the colour matches whichever state the learner currently sees.

    // Lock
    public Brush DoorLockStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (!SelectedDoor.HasLock)
                return WarningBrush;

            if (SelectedDoor.BreakGlassIsActive)
                return WarningBrush;

            if (SelectedDoor.UnlockedForMaintenance)
                return WarningBrush;

            return SelectedDoor.DoorIsLocked ? BadBrush : GoodBrush;
        }
    }

    // Door Sensor
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

    // Readers
    public Brush InReaderStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (InReaderDecisionIsGranted)
                return GoodBrush;

            if (InReaderDecisionIsDenied)
                return BadBrush;

            if (InReaderPinSent)
                return DragOverBrush;

            if (IsCardholderOverInReader)
                return DragOverBrush;

            if (SelectedDoor.InReaderIsShunted)
                return WarningBrush;

            return SelectedDoor.InReaderIsOnline ? GoodBrush : NeutralBrush;
        }
    }

    public Brush InReaderLedBrush
    {
        get
        {
            if (IsCardholderOverInReader)
                return DragOverBrush;

            if (SelectedDoor == null)
                return BadBrush;

            if (SelectedDoor.InReaderIsShunted)
                return WarningBrush;

            if (!SelectedDoor.InReaderIsOnline)
                return NeutralBrush;

            return SelectedDoor.InReaderLedColor == "Green"
                ? GoodBrush
                : BadBrush;
        }
    }

    public Brush OutReaderStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (OutReaderDecisionIsGranted)
                return GoodBrush;

            if (OutReaderDecisionIsDenied)
                return BadBrush;

            if (OutReaderPinSent)
                return DragOverBrush;

            if (IsCardholderOverOutReader)
                return DragOverBrush;

            if (SelectedDoor.OutReaderIsShunted)
                return WarningBrush;

            return SelectedDoor.OutReaderIsOnline ? GoodBrush : NeutralBrush;
        }
    }

    public Brush OutReaderLedBrush
    {
        get
        {
            if (IsCardholderOverOutReader)
                return DragOverBrush;

            if (SelectedDoor == null)
                return BadBrush;

            if (SelectedDoor.OutReaderIsShunted)
                return WarningBrush;

            if (!SelectedDoor.OutReaderIsOnline)
                return NeutralBrush;

            return SelectedDoor.OutReaderLedColor == "Green"
                ? GoodBrush
                : BadBrush;
        }
    }

    // REX's
    public Brush InRexStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (SelectedDoor.RexSideInIsShunted)
                return WarningBrush;

            if (InRexDecisionText == "Access granted")
                return GoodBrush;

            if (InRexDecisionText == "Access denied")
                return BadBrush;

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

            if (OutRexDecisionText == "Access granted")
                return GoodBrush;

            if (OutRexDecisionText == "Access denied")
                return BadBrush;

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

            if (NoSideRexDecisionText == "Access granted")
                return GoodBrush;

            if (NoSideRexDecisionText == "Access denied")
                return BadBrush;

            return SelectedDoor.RexNoSideIsActive ? BadBrush : GoodBrush;
        }
    }

    // Breakglass / Manual station
    public Brush BreakGlassStatusColor
    {
        get
        {
            if (SelectedDoor == null)
                return NeutralBrush;

            if (SelectedDoor.BreakGlassIsShunted)
                return WarningBrush;

            return SelectedDoor.BreakGlassIsActive ? BadBrush : GoodBrush;
        }
    }


    /*
      #############################################################################
                                Device Tooltips
      #############################################################################
    */

    // Door
    public string DoorActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasDoorSensor)
                return "No door sensor configured";

            if (SelectedDoor.DoorSensorIsShunted)
            {
                return SelectedDoor.DoorSensorIsOpen
                    ? "Door sensor is shunted - close door"
                    : "Door sensor is shunted - open door";
            }

            return SelectedDoor.DoorSensorIsOpen
                ? "Close door"
                : "Open door";
        }
    }

    // Readers
    public string InReaderActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasReaderSideIn)
                return "";

            if (SelectedDoor.InReaderIsShunted)
                return "In reader is shunted";

            if (!SelectedDoor.InReaderIsOnline)
                return "In reader is offline";

            return "Drag credential here. Right-click for PIN or auto-enrol.";
        }
    }

    public string OutReaderActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasReaderSideOut)
                return "";

            if (SelectedDoor.OutReaderIsShunted)
                return "Out reader is shunted";

            if (!SelectedDoor.OutReaderIsOnline)
                return "Out reader is offline";

            return "Drag credential here. Right-click for PIN or auto-enrol.";
        }
    }

    // REX's
    public string InRexActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasRexSideIn)
                return "";

            if (SelectedDoor.RexSideInIsShunted)
            {
                return SelectedDoor.RexSideInIsActive
                    ? "In REX is shunted - Release In REX"
                    : "In REX is shunted - Press In REX";
            }

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
            {
                return SelectedDoor.RexSideOutIsActive
                    ? "Out REX is shunted - Release Out REX"
                    : "Out REX is shunted - Press Out REX";
            }

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
            {
                return SelectedDoor.RexNoSideIsActive
                    ? "REX is shunted - Release REX"
                    : "REX is shunted - Press REX";
            }

            return SelectedDoor.RexNoSideIsActive
                ? "Release REX"
                : "Press REX";
        }
    }

    // Breakglass / Manual station
    public string BreakGlassActionTooltip
    {
        get
        {
            if (SelectedDoor == null)
                return "";

            if (!SelectedDoor.HasBreakGlass)
                return "";

            if (SelectedDoor.BreakGlassIsShunted)
            {
                return SelectedDoor.BreakGlassIsActive
                    ? "Breakglass is shunted - Reset breakglass"
                    : "Breakglass is shunted - Activate breakglass";
            }

            return SelectedDoor.BreakGlassIsActive
                ? "Reset breakglass"
                : "Activate breakglass";
        }
    }


    /*
      #############################################################################
                         Door Loading and Selection Refresh
      #############################################################################
    */

    // Loads/refeshes the available doors for this panel while preserving the selected door by Id where possible.
    //
    // The slow connection refresh replaces the door list every few seconds.
    // To avoid UI flicker, live state that is normally polled separately (reader online/shunted/LED, REX active/shunted, door sensor, breakglass) is copied from the previous selected door object into the refreshed object.
    //
    // Door lock state is intentionally NOT preserved because it comes from the refreshed Softwire door JSON and should reflect the latest 'controller' state.
    public void LoadDoors(IEnumerable<SoftwireDoor> loadedDoors)
    {
        var previousSelectedDoor = SelectedDoor;
        var previousSelectedDoorId = previousSelectedDoor?.Id;

        Doors = new ObservableCollection<SoftwireDoor>(
           loadedDoors.OrderBy(d => d.Name));

        HasDoors = Doors.Any();
        DoorCount = Doors.Count;

        EmptyDoorPanelMessage = DoorCount == 0
            ? "Connected to Softwire, but no doors are configured. Please create a door in Config Tool using Softwire simulated hardware."
            : "Please select a door";

        if (!string.IsNullOrWhiteSpace(previousSelectedDoorId))
        {
            var refreshedSelectedDoor = Doors.FirstOrDefault(d => d.Id == previousSelectedDoorId);

            if (refreshedSelectedDoor != null && previousSelectedDoor != null)
            {
                // Preserve Door Sensor live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.DoorSensorIsOpen = previousSelectedDoor.DoorSensorIsOpen;
                refreshedSelectedDoor.DoorSensorIsShunted = previousSelectedDoor.DoorSensorIsShunted;

                // Do NOT preserve DoorIsLocked or UnlockedForMaintenance here.
                // They come from the refreshed door JSON and must reflect the current Softwire state.

                // Preserve In Reader live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.InReaderIsOnline = previousSelectedDoor.InReaderIsOnline;
                refreshedSelectedDoor.InReaderIsShunted = previousSelectedDoor.InReaderIsShunted;
                refreshedSelectedDoor.InReaderLedColor = previousSelectedDoor.InReaderLedColor;

                // Preserve Out Reader live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.OutReaderIsOnline = previousSelectedDoor.OutReaderIsOnline;
                refreshedSelectedDoor.OutReaderIsShunted = previousSelectedDoor.OutReaderIsShunted;
                refreshedSelectedDoor.OutReaderLedColor = previousSelectedDoor.OutReaderLedColor;

                // Preserve In REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexSideInIsActive = previousSelectedDoor.RexSideInIsActive;
                refreshedSelectedDoor.RexSideInIsShunted = previousSelectedDoor.RexSideInIsShunted;

                // Preserve Out REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexSideOutIsActive = previousSelectedDoor.RexSideOutIsActive;
                refreshedSelectedDoor.RexSideOutIsShunted = previousSelectedDoor.RexSideOutIsShunted;

                // Preserve No-side REX live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.RexNoSideIsActive = previousSelectedDoor.RexNoSideIsActive;
                refreshedSelectedDoor.RexNoSideIsShunted = previousSelectedDoor.RexNoSideIsShunted;

                // Preserve Breakglass live state so it does not flicker during the 3-second door list refresh
                refreshedSelectedDoor.BreakGlassIsActive = previousSelectedDoor.BreakGlassIsActive;
                refreshedSelectedDoor.BreakGlassIsShunted = previousSelectedDoor.BreakGlassIsShunted;

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


    /*
      #############################################################################
                          Generated Property Change Hooks
      #############################################################################
    */

    // Door:
    // -----
    // Automatically called when SelectedDoor changes
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        // Update UI flag based on whether a door is selected
        HasSelectedDoor = value != null;

        // Refresh anything that depends on the selected door:

        // DOOR
        OnPropertyChanged(nameof(DoorImagePath));
        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorLockStatusColor));
        OnPropertyChanged(nameof(DoorSensorStatusText));
        OnPropertyChanged(nameof(DoorSensorStatusColor));
        OnPropertyChanged(nameof(DoorActionTooltip));

        // READERS
        OnPropertyChanged(nameof(ReaderImagePath));

        OnPropertyChanged(nameof(InReaderVisibility));
        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
        OnPropertyChanged(nameof(InReaderLedBrush));
        OnPropertyChanged(nameof(InReaderActionTooltip));

        OnPropertyChanged(nameof(OutReaderVisibility));
        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
        OnPropertyChanged(nameof(OutReaderLedBrush));
        OnPropertyChanged(nameof(OutReaderActionTooltip));

        // REX's
        OnPropertyChanged(nameof(InRexColumn));
        OnPropertyChanged(nameof(InRexImagePath));
        OnPropertyChanged(nameof(InRexVisibility));
        OnPropertyChanged(nameof(InRexStatusText));
        OnPropertyChanged(nameof(InRexStatusColor));
        OnPropertyChanged(nameof(InRexActionTooltip));

        OnPropertyChanged(nameof(OutRexColumn));
        OnPropertyChanged(nameof(OutRexImagePath));
        OnPropertyChanged(nameof(OutRexVisibility));
        OnPropertyChanged(nameof(OutRexStatusText));
        OnPropertyChanged(nameof(OutRexStatusColor));
        OnPropertyChanged(nameof(OutRexActionTooltip));

        OnPropertyChanged(nameof(NoSideRexImagePath));
        OnPropertyChanged(nameof(NoSideRexVisibility));
        OnPropertyChanged(nameof(NoSideRexStatusText));
        OnPropertyChanged(nameof(NoSideRexStatusColor));
        OnPropertyChanged(nameof(NoSideRexActionTooltip));

        // Breakglass / Manual station
        OnPropertyChanged(nameof(BreakGlassImagePath));
        OnPropertyChanged(nameof(BreakGlassVisibility));
        OnPropertyChanged(nameof(BreakGlassStatusText));
        OnPropertyChanged(nameof(BreakGlassStatusColor));
        OnPropertyChanged(nameof(BreakGlassActionTooltip));

    }

    // Readers:
    // --------
    // Refreshes the In and Out Reader LED and status text when drag-over state changes
    partial void OnIsCardholderOverInReaderChanged(bool value)
    {
        OnPropertyChanged(nameof(InReaderLedBrush));
        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
    }
    partial void OnIsCardholderOverOutReaderChanged(bool value)
    {
        OnPropertyChanged(nameof(OutReaderLedBrush));
        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
    }

    // Refreshes the In and Out Reader colour when temporary decision result changes.
    partial void OnInReaderDecisionIsGrantedChanged(bool value)
    {
        OnPropertyChanged(nameof(InReaderStatusColor));
    }
    partial void OnInReaderDecisionIsDeniedChanged(bool value)
    {
        OnPropertyChanged(nameof(InReaderStatusColor));
    }
    partial void OnOutReaderDecisionIsGrantedChanged(bool value)
    {
        OnPropertyChanged(nameof(OutReaderStatusColor));
    }
    partial void OnOutReaderDecisionIsDeniedChanged(bool value)
    {
        OnPropertyChanged(nameof(OutReaderStatusColor));
    }

    // Refreshes the In and Out Reader status when temporary PIN-sent state changes
    partial void OnInReaderPinSentChanged(bool value)
    {
        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
    }
    partial void OnOutReaderPinSentChanged(bool value)
    {
        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
    }

    // Refreshes the In and Out Reader status when temporary decision feedback changes.
    partial void OnInReaderDecisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
    }
    partial void OnOutReaderDecisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
    }

    // REX's:
    // ------
    // Refreshes the In, Out, and No Side REX's status when temporary decision feedback changes.
    partial void OnInRexDecisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(InRexStatusText));
        OnPropertyChanged(nameof(InRexStatusColor));
    }
    partial void OnOutRexDecisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(OutRexStatusText));
        OnPropertyChanged(nameof(OutRexStatusColor));
    }
    partial void OnNoSideRexDecisionTextChanged(string value)
    {
        OnPropertyChanged(nameof(NoSideRexStatusText));
        OnPropertyChanged(nameof(NoSideRexStatusColor));
    }


    /*
      #############################################################################
             Update methods for live state changes with optimised UI refresh
      #############################################################################
    */

    // These methods are called by MainViewModel polling or DoorPanelView optimistic UI updates.
    // Each method updates only the relevant SoftwireDoor fields, then raises property notifications for dependent calculated UI properties.

    // Misc:
    // -----
    // Copies live/polled state from another SoftwireDoor into the currently selected door, then refreshes all calculated UI properties.
    // Used when switching from Two Door View back to Single Door View.
    // Without this, Single Door View can briefly show stale calculated UI state until the next polling tick refreshes the bindings.
    public void ApplyLiveStateFromDoor(SoftwireDoor source)
    {
        if (SelectedDoor == null)
            return;

        if (SelectedDoor.Id != source.Id)
            return;

        // Door lock / sensor live state
        SelectedDoor.DoorIsLocked = source.DoorIsLocked;
        SelectedDoor.UnlockedForMaintenance = source.UnlockedForMaintenance;
        SelectedDoor.DoorSensorIsOpen = source.DoorSensorIsOpen;
        SelectedDoor.DoorSensorIsShunted = source.DoorSensorIsShunted;

        // In reader live state
        SelectedDoor.InReaderIsOnline = source.InReaderIsOnline;
        SelectedDoor.InReaderIsShunted = source.InReaderIsShunted;
        SelectedDoor.InReaderLedColor = source.InReaderLedColor;

        // Out reader live state
        SelectedDoor.OutReaderIsOnline = source.OutReaderIsOnline;
        SelectedDoor.OutReaderIsShunted = source.OutReaderIsShunted;
        SelectedDoor.OutReaderLedColor = source.OutReaderLedColor;

        // In REX live state
        SelectedDoor.RexSideInIsActive = source.RexSideInIsActive;
        SelectedDoor.RexSideInIsShunted = source.RexSideInIsShunted;

        // Out REX live state
        SelectedDoor.RexSideOutIsActive = source.RexSideOutIsActive;
        SelectedDoor.RexSideOutIsShunted = source.RexSideOutIsShunted;

        // No-side REX live state
        SelectedDoor.RexNoSideIsActive = source.RexNoSideIsActive;
        SelectedDoor.RexNoSideIsShunted = source.RexNoSideIsShunted;

        // Breakglass live state
        SelectedDoor.BreakGlassIsActive = source.BreakGlassIsActive;
        SelectedDoor.BreakGlassIsShunted = source.BreakGlassIsShunted;

        RefreshSelectedDoorDisplayProperties();
    }

    // Refreshes calculated UI properties that depend on SelectedDoor.
    // These properties are not stored values; they are calculated from the selected SoftwireDoor.
    // Therefore, when we copy live state into the selected door object, we must notify the UI that these calculated bindings changed.
    private void RefreshSelectedDoorDisplayProperties()
    {
        // Door
        OnPropertyChanged(nameof(DoorImagePath));
        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorLockStatusColor));
        OnPropertyChanged(nameof(DoorSensorStatusText));
        OnPropertyChanged(nameof(DoorSensorStatusColor));
        OnPropertyChanged(nameof(DoorActionTooltip));

        // Readers
        OnPropertyChanged(nameof(InReaderVisibility));
        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
        OnPropertyChanged(nameof(InReaderLedBrush));
        OnPropertyChanged(nameof(InReaderActionTooltip));

        OnPropertyChanged(nameof(OutReaderVisibility));
        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
        OnPropertyChanged(nameof(OutReaderLedBrush));
        OnPropertyChanged(nameof(OutReaderActionTooltip));

        // REX
        OnPropertyChanged(nameof(InRexColumn));
        OnPropertyChanged(nameof(InRexImagePath));
        OnPropertyChanged(nameof(InRexVisibility));
        OnPropertyChanged(nameof(InRexStatusText));
        OnPropertyChanged(nameof(InRexStatusColor));
        OnPropertyChanged(nameof(InRexActionTooltip));

        OnPropertyChanged(nameof(OutRexColumn));
        OnPropertyChanged(nameof(OutRexImagePath));
        OnPropertyChanged(nameof(OutRexVisibility));
        OnPropertyChanged(nameof(OutRexStatusText));
        OnPropertyChanged(nameof(OutRexStatusColor));
        OnPropertyChanged(nameof(OutRexActionTooltip));

        OnPropertyChanged(nameof(NoSideRexImagePath));
        OnPropertyChanged(nameof(NoSideRexVisibility));
        OnPropertyChanged(nameof(NoSideRexStatusText));
        OnPropertyChanged(nameof(NoSideRexStatusColor));
        OnPropertyChanged(nameof(NoSideRexActionTooltip));

        // Breakglass / Manual station
        OnPropertyChanged(nameof(BreakGlassImagePath));
        OnPropertyChanged(nameof(BreakGlassVisibility));
        OnPropertyChanged(nameof(BreakGlassStatusText));
        OnPropertyChanged(nameof(BreakGlassStatusColor));
        OnPropertyChanged(nameof(BreakGlassActionTooltip));
    }

    // Door:
    // -----
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

    // Readers:
    // --------
    // Updates live state for the In and Out Readers and refreshes dependent UI properties
    public void UpdateInReaderState(bool isOnline, bool isShunted, string ledColor)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.InReaderIsOnline != isOnline)
        {
            SelectedDoor.InReaderIsOnline = isOnline;
            changed = true;
        }

        if (SelectedDoor.InReaderIsShunted != isShunted)
        {
            SelectedDoor.InReaderIsShunted = isShunted;
            changed = true;
        }

        if (SelectedDoor.InReaderLedColor != ledColor)
        {
            var previousLedColor = SelectedDoor.InReaderLedColor;

            SelectedDoor.InReaderLedColor = ledColor;
            changed = true;

            // Only raise a sound event when the reader leaves its normal red/idle state.
            // This avoids a second beep when the LED returns from green back to red.
            if (previousLedColor == "Red" && ledColor != "Red")
            {
                ReaderLedChanged?.Invoke();
            }
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(InReaderStatusText));
        OnPropertyChanged(nameof(InReaderStatusColor));
        OnPropertyChanged(nameof(InReaderLedBrush));
        OnPropertyChanged(nameof(InReaderActionTooltip));
    }
    public void UpdateOutReaderState(bool isOnline, bool isShunted, string ledColor)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.OutReaderIsOnline != isOnline)
        {
            SelectedDoor.OutReaderIsOnline = isOnline;
            changed = true;
        }

        if (SelectedDoor.OutReaderIsShunted != isShunted)
        {
            SelectedDoor.OutReaderIsShunted = isShunted;
            changed = true;
        }

        if (SelectedDoor.OutReaderLedColor != ledColor)
        {
            var previousLedColor = SelectedDoor.OutReaderLedColor;

            SelectedDoor.OutReaderLedColor = ledColor;
            changed = true;

            // Only raise a sound event when the reader leaves its normal red/idle state.
            // This avoids a second beep when the LED returns from green back to red.
            if (previousLedColor == "Red" && ledColor != "Red")
            {
                ReaderLedChanged?.Invoke();
            }
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(OutReaderStatusText));
        OnPropertyChanged(nameof(OutReaderStatusColor));
        OnPropertyChanged(nameof(OutReaderLedBrush));
        OnPropertyChanged(nameof(OutReaderActionTooltip));
    }

    // REX's:
    // ------
    // Updates live state for the In, Out, and No Side REX's and refreshes dependent UI properties
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

    // Breakglass / Manual station:
    // ----------------------------
    // Updates live state for Breakglass and refreshes dependent UI properties
    public void UpdateBreakGlassState(bool isActive, bool isShunted)
    {
        if (SelectedDoor == null)
            return;

        var changed = false;

        if (SelectedDoor.BreakGlassIsActive != isActive)
        {
            SelectedDoor.BreakGlassIsActive = isActive;
            changed = true;
        }

        if (SelectedDoor.BreakGlassIsShunted != isShunted)
        {
            SelectedDoor.BreakGlassIsShunted = isShunted;
            changed = true;
        }

        if (!changed)
            return;

        OnPropertyChanged(nameof(BreakGlassImagePath));
        OnPropertyChanged(nameof(BreakGlassStatusText));
        OnPropertyChanged(nameof(BreakGlassStatusColor));
        OnPropertyChanged(nameof(BreakGlassActionTooltip));

        OnPropertyChanged(nameof(DoorLockStatusText));
        OnPropertyChanged(nameof(DoorLockStatusColor));
    }


    /*
      #############################################################################
                          Temporary Feedback Methods
      #############################################################################
    */

    // Shows short-lived feedback under devices after an access action.
    // The "only clear if unchanged" check prevents an older delay from clearing newer feedback that arrived before the previous display period ended.

    // Readers:
    // --------
    // Shows temporary access decision feedback under the In and Out Readers.
    public async Task ShowInReaderDecisionFeedbackAsync(string decisionText, bool isGranted)
    {
        InReaderDecisionText = decisionText;
        InReaderDecisionIsGranted = isGranted;
        InReaderDecisionIsDenied = !isGranted;

        await Task.Delay(2000);

        // Only clear if nothing newer has replaced it.
        if (InReaderDecisionText == decisionText)
        {
            InReaderDecisionText = string.Empty;
            InReaderDecisionIsGranted = false;
            InReaderDecisionIsDenied = false;
        }
    }
    public async Task ShowOutReaderDecisionFeedbackAsync(string decisionText, bool isGranted)
    {
        OutReaderDecisionText = decisionText;
        OutReaderDecisionIsGranted = isGranted;
        OutReaderDecisionIsDenied = !isGranted;

        await Task.Delay(2000);

        // Only clear if nothing newer has replaced it.
        if (OutReaderDecisionText == decisionText)
        {
            OutReaderDecisionText = string.Empty;
            OutReaderDecisionIsGranted = false;
            OutReaderDecisionIsDenied = false;
        }
    }

    // REX's:
    // ------
    // Shows temporary access decision feedback under the REX's.
    public async Task ShowInRexDecisionFeedbackAsync(string decisionText)
    {
        InRexDecisionText = decisionText;

        await Task.Delay(2000);

        // Only clear if nothing newer has replaced it.
        if (InRexDecisionText == decisionText)
        {
            InRexDecisionText = string.Empty;
        }
    }
    public async Task ShowOutRexDecisionFeedbackAsync(string decisionText)
    {
        OutRexDecisionText = decisionText;

        await Task.Delay(2000);

        // Only clear if nothing newer has replaced it.
        if (OutRexDecisionText == decisionText)
        {
            OutRexDecisionText = string.Empty;
        }
    }
    public async Task ShowNoSideRexDecisionFeedbackAsync(string decisionText)
    {
        NoSideRexDecisionText = decisionText;

        await Task.Delay(2000);

        // Only clear if nothing newer has replaced it.
        if (NoSideRexDecisionText == decisionText)
        {
            NoSideRexDecisionText = string.Empty;
        }
    }

}