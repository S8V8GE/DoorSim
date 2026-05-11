using CommunityToolkit.Mvvm.ComponentModel;

namespace DoorSim.ViewModels;

// ViewModel for Auto Mode.
//
// Auto Mode will eventually run automated Softwire simulation scenarios such as:
// - normal access events
// - door forced events
// - door held open events
//
// For now this is only a shell so the Mode menu and Auto Mode page can be wired safely.
public partial class AutoModeViewModel : ObservableObject
{
    /*
      #############################################################################
                                  Page Text
      #############################################################################
    */

    public string Title => "Auto Mode";

    public string Subtitle =>
        "Busy site simulation for training, demos, and stress testing.";

    public string PlaceholderMessage =>
        "Auto Mode is ready to be built. Simulation settings and event logging will be added next.";
}