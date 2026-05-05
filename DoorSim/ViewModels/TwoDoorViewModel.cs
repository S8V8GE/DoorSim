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

    /*
      #############################################################################
                                    Constructor
      #############################################################################
    */

    public TwoDoorViewModel(DoorsViewModel doors, CardholdersViewModel cardholders)
    {
        Doors = doors;
        Cardholders = cardholders;

        // Two Door View uses two independent door panel states.
        // Each side will eventually select and control its own door.
        LeftDoorPanel = new DoorPanelViewModel("Left Door");
        RightDoorPanel = new DoorPanelViewModel("Right Door");
    }

    /*
      #############################################################################
                                  Door loading
      #############################################################################
    */

    // Refreshes the available door lists for both door panels.
    //
    // For now, both panels receive the same list of doors.
    // Each panel keeps its own SelectedDoor, so changing the left selector
    // will not automatically change the right selector later.
    public void LoadDoors(IEnumerable<DoorSim.Models.SoftwireDoor> doors)
    {
        LeftDoorPanel.LoadDoors(doors);
        RightDoorPanel.LoadDoors(doors);
    }



}