using System.Windows.Controls;

namespace DoorSim.Views;

// Lightweight code-behind for Two Door View.
//
// All behaviour is currently handled by:
//      - TwoDoorViewModel
//      - DoorPanelView / DoorPanelViewModel
//      - MainViewModel polling
//
// This file only initialises the XAML.
public partial class TwoDoorView : UserControl
{
    public TwoDoorView()
    {
        InitializeComponent();
    }
}

