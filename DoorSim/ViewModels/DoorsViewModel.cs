using CommunityToolkit.Mvvm.ComponentModel;
using DoorSim.Models;
using System.Collections.ObjectModel;

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

    // Loads doors into the ViewModel and preserves selection if possible
    public void LoadDoors(IEnumerable<SoftwireDoor> loadedDoors)
    {
        // Store the previously selected door ID (if any)
        // This allows us to restore selection after a refresh
        var previousSelectedDoorId = SelectedDoor?.Id;

        // Replace the collection with a sorted list of doors
        Doors = new ObservableCollection<SoftwireDoor>(
            loadedDoors.OrderBy(d => d.Name));

        // Update flag indicating whether any doors exist
        HasDoors = Doors.Any();
        DoorCount = Doors.Count;

        // Attempt to restore previously selected door
        if (!string.IsNullOrWhiteSpace(previousSelectedDoorId))
        {
            SelectedDoor = Doors.FirstOrDefault(d => d.Id == previousSelectedDoorId);
        }
        else
        {
            // No previous selection → leave unselected
            SelectedDoor = null;
        }
    }

    // Automatically called when SelectedDoor changes
    partial void OnSelectedDoorChanged(SoftwireDoor? value)
    {
        // Update UI flag based on whether a door is selected
        HasSelectedDoor = value != null;
    }
}