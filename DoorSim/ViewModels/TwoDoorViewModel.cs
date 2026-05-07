using CommunityToolkit.Mvvm.ComponentModel;

namespace DoorSim.ViewModels;

// ViewModel for Two Door View.
//
// Responsibilities:
//      - Own independent left and right door panel selectors.
//      - Share the cardholder list across both door panels.
//      - Keep both selector lists refreshed from the latest Softwire door list.
//      - Prevent the same door being selected on both sides at once.
//      - Prepare Two Door View when switching from Single Door View.
//
// This ViewModel does not poll live hardware state directly. MainViewModel handles polling and updates each panel's DoorState.
public partial class TwoDoorViewModel : ObservableObject
{
    /*
      #############################################################################
                              Shared Source ViewModels
      #############################################################################
    */

    // Shared source door list.
    // MainViewModel refreshes this from Softwire. TwoDoorViewModel uses it as the master list when rebuilding the left/right selector lists.
    public DoorsViewModel Doors { get; }

    // Shared cardholder list.
    // The same cardholders can be dragged onto any reader in either door panel (left or right).
    public CardholdersViewModel Cardholders { get; }


    /*
      #############################################################################
                              Door Panel ViewModels
      #############################################################################
    */

    // Independent state for the left door panel.
    // Owns the left selector and the DoorsViewModel used by the left DoorPanelView.
    public DoorPanelViewModel LeftDoorPanel { get; }

    // Independent state for the right door panel.
    // Owns the right selector and the DoorsViewModel used by the right DoorPanelView.
    public DoorPanelViewModel RightDoorPanel { get; }

    // ViewModel for the interlocking controls area below the two door panels.
    public DoorInterlockingControlsViewModel Interlocking { get; } = new();


    /*
      #############################################################################
                              Refresh Guard State
      #############################################################################
    */

    // Prevents RefreshAvailableDoorSelections from recursively triggering itself.
    // Loading filtered door lists can reassign SelectedDoor to a refreshed object with the same Id.
    // That raises PropertyChanged, which would normally call RefreshAvailableDoorSelections again.
    // Without this guard, the right-hand panel can flicker as its selector and live panel state are repeatedly refreshed.
    private bool _isRefreshingAvailableDoorSelections;


    /*
      #############################################################################
                                    Constructor
      #############################################################################
    */

    // When either selector changes, rebuild both available-door lists so the opposite selector no longer offers the selected door.
    // The refresh guard prevents list-refresh assignments from recursively triggering another refresh.
    public TwoDoorViewModel(DoorsViewModel doors, CardholdersViewModel cardholders)
    {
        Doors = doors;
        Cardholders = cardholders;

        // Two Door View uses two independent door panel states. Each side selects and controls its own door.
        LeftDoorPanel = new DoorPanelViewModel("Left Door");
        RightDoorPanel = new DoorPanelViewModel("Right Door");

        // When either side changes selection, refresh the opposite dropdown so the same door cannot be selected twice.
        LeftDoorPanel.PropertyChanged += (s, e) =>
        {
            if (_isRefreshingAvailableDoorSelections)
                return;

            if (e.PropertyName == nameof(DoorPanelViewModel.SelectedDoor))
            {
                RefreshAvailableDoorSelections();
            }
        };

        RightDoorPanel.PropertyChanged += (s, e) =>
        {
            if (_isRefreshingAvailableDoorSelections)
                return;

            if (e.PropertyName == nameof(DoorPanelViewModel.SelectedDoor))
            {
                RefreshAvailableDoorSelections();
            }
        };

    }


    /*
      #############################################################################
                        Public Door Loading / View Preparation
      #############################################################################
    */

    // Refreshes Two Door View from the latest Softwire door list.
    // The shared Doors ViewModel stores the master list.
    // The left and right panel selector lists are then rebuilt from that master list, with filtering applied so the same door cannot be selected on both sides.
    public void LoadDoors(IEnumerable<DoorSim.Models.SoftwireDoor> doors)
    {
        Doors.LoadDoors(doors);

        RefreshAvailableDoorSelections();
    }

    // Prepares Two Door View when the user switches from Single Door View.
    //
    // Behaviour:
    //      - Left panel inherits the currently selected Single Door View door.
    //      - Right panel starts empty so the trainer deliberately chooses the second door.
    //
    // Note: This expects LoadDoors(...) to have populated the panel selector lists before the view is switched. If no matching left door exists, the left panel remains unselected.
    public void PrepareFromSingleDoorSelection(DoorSim.Models.SoftwireDoor? singleDoorSelection)
    {
        if (singleDoorSelection == null)
        {
            LeftDoorPanel.SelectedDoor = null;
            RightDoorPanel.SelectedDoor = null;
            return;
        }

        var matchingLeftDoor = LeftDoorPanel.Doors
            .FirstOrDefault(d => d.Id == singleDoorSelection.Id);

        LeftDoorPanel.SelectedDoor = matchingLeftDoor;

        // Always start the right panel empty when switching into Two Door View.
        RightDoorPanel.SelectedDoor = null;
    }


    /*
      #############################################################################
                            Door Selection Filtering
      #############################################################################
    */

    // Rebuilds the left and right selector lists while preventing duplicates.
    //
    // If the left panel has a selected door, that door is removed from the right selector list.
    // If the right panel has a selected door, that door is removed from the left selector list.
    //
    // This matters for interlocking tests because selecting the same door on both sides would not represent a valid two-door scenario.
    private void RefreshAvailableDoorSelections()
    {
        if (_isRefreshingAvailableDoorSelections)
            return;

        try
        {
            _isRefreshingAvailableDoorSelections = true;

            var allDoors = Doors.Doors.ToList();

            var leftSelectedId = LeftDoorPanel.SelectedDoor?.Id;
            var rightSelectedId = RightDoorPanel.SelectedDoor?.Id;

            var leftAvailableDoors = allDoors
                .Where(d => string.IsNullOrWhiteSpace(rightSelectedId) || d.Id != rightSelectedId)
                .ToList();

            var rightAvailableDoors = allDoors
                .Where(d => string.IsNullOrWhiteSpace(leftSelectedId) || d.Id != leftSelectedId)
                .ToList();

            // DoorPanelViewModel.LoadDoors(...) preserves the current selection by Id if possible, or clears it if the selected door is no longer available.
            LeftDoorPanel.LoadDoors(leftAvailableDoors);
            RightDoorPanel.LoadDoors(rightAvailableDoors);
        }
        finally
        {
            _isRefreshingAvailableDoorSelections = false;
        }
    }

}