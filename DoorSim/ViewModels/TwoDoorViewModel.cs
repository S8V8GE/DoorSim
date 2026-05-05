using CommunityToolkit.Mvvm.ComponentModel;

namespace DoorSim.ViewModels;

// ViewModel for the Two Door View.
//
// This is intentionally a shell for now.
// It gives TwoDoorView its own clean place to manage:
// - Left door selection
// - Right door selection
// - Shared cardholders
// - Future interlocking controls
//
// For the first step, it simply holds references to the existing
// DoorsViewModel and CardholdersViewModel so the current UI keeps working.
public partial class TwoDoorViewModel : ObservableObject
{
    /*
      #############################################################################
                              Shared child view models
      #############################################################################
    */

    // Existing shared door state.
    // Temporary: both sides of TwoDoorView still use this until we introduce
    // independent left/right door panel state.
    public DoorsViewModel Doors { get; }

    // Existing shared cardholder list.
    // Cardholders are shared across both doors so credentials can be dragged
    // to any reader in the two-door layout.
    public CardholdersViewModel Cardholders { get; }

    // Independent left-side door panel state.
    public DoorPanelViewModel LeftDoorPanel { get; }

    // Independent right-side door panel state.
    public DoorPanelViewModel RightDoorPanel { get; }

    // Prevents RefreshAvailableDoorSelections from recursively triggering itself.
    //
    // Loading the filtered door lists can reassign SelectedDoor to a refreshed object with the same Id. That raises PropertyChanged, which would normally
    // call RefreshAvailableDoorSelections again. Without this guard, the RHS panel can flicker as its selector/panel state is repeatedly refreshed.
    private bool _isRefreshingAvailableDoorSelections;

    /*
      #############################################################################
                                    Constructor
      #############################################################################
    */

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
                                  Door loading
      #############################################################################
    */

    // Refreshes the available door lists for both door panels.
    //
    // Each side receives the same base list, but the final available selections are filtered so the same door cannot be selected on both sides.
    public void LoadDoors(IEnumerable<DoorSim.Models.SoftwireDoor> doors)
    {
        Doors.LoadDoors(doors);

        RefreshAvailableDoorSelections();
    }

    // Prepares Two Door View when the user switches from Single Door View.
    //
    // Behaviour:
    // - Left panel inherits the currently selected Single Door View door.
    // - Right panel starts empty so the trainer deliberately chooses the second door.
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

    // Removes already-selected doors from the opposite selector.
    //
    // This prevents the trainer selecting the same door on both sides of Two Door View, which would make interlocking tests meaningless.
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

            LeftDoorPanel.LoadDoors(leftAvailableDoors);
            RightDoorPanel.LoadDoors(rightAvailableDoors);
        }
        finally
        {
            _isRefreshingAvailableDoorSelections = false;
        }
    }

}