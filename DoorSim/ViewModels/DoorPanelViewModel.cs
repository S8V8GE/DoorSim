using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;

namespace DoorSim.ViewModels;

// ViewModel for one reusable door simulator panel.
//
// TwoDoorView will eventually use two instances:
// - Left door panel
// - Right door panel
//
// For now this class only owns independent door selection.
// Door interaction logic will be moved across gradually so we do not break
// the existing Single Door behaviour.
public partial class DoorPanelViewModel : ObservableObject
{
    /*
      #############################################################################
                                  Door selection
      #############################################################################
    */

    // Friendly label used by the UI, for example:
    // - Left Door
    // - Right Door
    public string PanelTitle { get; }

    // Doors available for this panel.
    //
    // This will be populated from the shared DoorsViewModel door list.
    public ObservableCollection<SoftwireDoor> Doors { get; } = new ObservableCollection<SoftwireDoor>();

    // The door selected for this specific panel.
    //
    // In Two Door View, the left and right panels will each have their own
    // SelectedDoor instead of sharing DoorsViewModel.SelectedDoor.
    [ObservableProperty]
    private SoftwireDoor? selectedDoor;

    // Full DoorsViewModel used by DoorPanelView.
    //
    // DoorPanelView already expects a DoorsViewModel because it needs all the
    // existing calculated properties, colours, commands, and feedback methods.
    // This lets each Two Door side have its own independent selected door state
    // without rewriting DoorPanelView yet.
    public DoorsViewModel DoorState { get; } = new DoorsViewModel();


    /*
      #############################################################################
                                  Constructor
      #############################################################################
    */

    public DoorPanelViewModel(string panelTitle)
    {
        PanelTitle = panelTitle;
    }

    // Keeps the full DoorState ViewModel in sync when this panel's selector changes.
    //
    // The selector binds to DoorPanelViewModel.SelectedDoor.
    // The actual DoorPanelView binds to DoorPanelViewModel.DoorState.
    // This bridge keeps both pointing at the same selected door.
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        DoorState.SelectedDoor = value;
    }


    /*
      #############################################################################
                                  Door loading
      #############################################################################
    */

    // Replaces the available door list for this panel while preserving the
    // currently selected door by Id whenever possible.
    //
    // Softwire returns fresh door objects on each refresh. If we simply clear
    // and reload the list, the ComboBox can lose its selected item because the
    // old selected object no longer exists in the new collection.
    //
    // To avoid that, we remember the selected door Id before refreshing,
    // reload the list, then reselect the matching refreshed door.
    public void LoadDoors(IEnumerable<SoftwireDoor> doors)
    {
        var selectedDoorId = SelectedDoor?.Id;

        var orderedDoors = doors
            .OrderBy(d => d.Name)
            .ToList();

        Doors.Clear();

        foreach (var door in orderedDoors)
        {
            Doors.Add(door);
        }

        // Keep the full door-state ViewModel loaded with the same refreshed doors.
        DoorState.LoadDoors(orderedDoors);

        if (!string.IsNullOrWhiteSpace(selectedDoorId))
        {
            var matchingDoor = Doors.FirstOrDefault(d => d.Id == selectedDoorId);

            if (matchingDoor != null)
            {
                SelectedDoor = matchingDoor;
                DoorState.SelectedDoor = matchingDoor;
                return;
            }
        }

        if (SelectedDoor == null && Doors.Count > 0)
        {
            SelectedDoor = Doors[0];
            DoorState.SelectedDoor = SelectedDoor;
        }
    }

}