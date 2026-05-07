using System.Windows.Controls;

namespace DoorSim.Views;

// Lightweight code-behind for the Door Interlocking Controls panel.
//
// Behaviour is handled by DoorInterlockingControlsViewModel.
// This file currently only initialises the XAML.
public partial class DoorInterlockingControlsView : UserControl
{
    public DoorInterlockingControlsView()
    {
        InitializeComponent();
    }
}