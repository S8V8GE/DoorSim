using System.Windows.Controls;

namespace DoorSim.Views;

// Lightweight code-behind for the Single Door selector.
//
// Selection behaviour is handled by binding:
//      - ItemsSource -> DoorsViewModel.Doors
//      - SelectedItem -> DoorsViewModel.SelectedDoor
//
// This file currently only initialises the XAML.
public partial class DoorSelectorView : UserControl
{
    public DoorSelectorView()
    {
        InitializeComponent();
    }
}