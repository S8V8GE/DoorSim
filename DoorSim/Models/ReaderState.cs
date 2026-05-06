namespace DoorSim.Models;

// Represents the live state of a Softwire reader device.
//
// Reader state is separate from door configuration:
//      - Door Roles tell us which reader belongs to the door
//      - The reader device endpoint tells us whether that reader is online, shunted, and what LED colour Softwire currently reports
public class ReaderState
{
    // True when Softwire reports the reader as online
    public bool Online { get; set; }

    // True when the reader is shunted/bypassed
    public bool IsShunted { get; set; }

    // Softwire-reported LED colour.
    //
    // Known values currently include "Red" and "Green".
    // DoorSim may temporarily override the displayed LED colour in the UI for interaction states such as drag-hover over a reader.
    public string LedColor { get; set; } = "Red";
}
