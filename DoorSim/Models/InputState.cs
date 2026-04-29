namespace DoorSim.Models;

// Represents the live state of a Softwire input device.
// Used for door sensors, REX buttons, and breakglass inputs.
public class InputState
{
    public bool Online { get; set; }
    public bool Active { get; set; }
    public bool IsShunted { get; set; }
}
