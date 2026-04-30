namespace DoorSim.Models;

// Represents the live state of a Softwire reader device.
//
// Reader state is separate from door configuration:
// - Door Roles tell us which reader belongs to the door
// - The reader device endpoint tells us whether that reader is online, shunted, and what LED colour Softwire currently reports
public class ReaderState
{
    // True when Softwire reports the reader as online
    public bool Online { get; set; }

    // True when the reader is shunted/bypassed
    public bool IsShunted { get; set; }

    // Softwire-reported LED colour.
    // Current known examples: Red, Green (This app may override this later for temporary UI states such as drag-hover blue... and so on).
    public string LedColor { get; set; } = "Red";
}
