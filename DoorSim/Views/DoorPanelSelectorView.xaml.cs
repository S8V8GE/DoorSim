using System.Windows.Controls;

namespace DoorSim.Views;

// Lightweight code-behind for a Two Door panel selector.
//
// Selection behaviour is handled by binding:
//      - ItemsSource -> DoorPanelViewModel.Doors
//      - SelectedItem -> DoorPanelViewModel.SelectedDoor
//
// This file currently only initialises the XAML.
public partial class DoorPanelSelectorView : UserControl
{
    public DoorPanelSelectorView()
    {
        InitializeComponent();
    }
}
