using System.Windows.Controls;

namespace DoorSim.Views;

// Lightweight host for Single Door mode.
//
// The actual interactive hardware UI lives in DoorPanelView.
// SingleDoorView keeps MainWindow's Single Door layout clear and allows Single Door and Two Door modes to share the same reusable door panel.
public partial class SingleDoorView : UserControl
{

    // SingleDoorView is now a lightweight host for DoorPanelView.
    //
    // The actual interactive door hardware UI now lives in:
    //      - DoorPanelView.xaml
    //      - DoorPanelView.xaml.cs
    //
    // This keeps the door panel reusable so TwoDoorView can eventually host two independent DoorPanelView controls side by side.
    public SingleDoorView()
    {
        InitializeComponent();
    }
}