using System.Windows.Controls;

namespace DoorSim.Views;

public partial class SingleDoorView : UserControl
{
    /*
      #############################################################################
                           Constructor and Initialisation
      #############################################################################
    */

    // SingleDoorView is now a lightweight host for DoorPanelView.
    //
    // The actual interactive door hardware UI now lives in:
    // - DoorPanelView.xaml
    // - DoorPanelView.xaml.cs
    //
    // This keeps the door panel reusable so TwoDoorView can eventually host
    // two independent DoorPanelView controls side by side.
    public SingleDoorView()
    {
        InitializeComponent();
    }
}