namespace DoorSim.Models;

// Represents the live state of a Softwire input device.
//
// Used for:
//      - door sensors,
//      - REX inputs,
//      - breakglass/manual station inputs.
public class InputState
{
    // True when Softwire reports the input device as online.
    public bool Online { get; set; }

    // True when the input is active.
    // For example: door sensor open, REX pressed, or breakglass active.
    public bool Active { get; set; }

    // True when the input is shunted/bypassed.
    public bool IsShunted { get; set; }

}
