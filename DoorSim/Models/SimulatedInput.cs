namespace DoorSim.Models;

// Represents one simulated Softwire input that can be selected for interlocking.
//
// Examples:
//      - door sensor input
//      - REX input
//      - breakglass/manual station input
//      - spare simulated input
//
// The same physical/simulated input may appear as door hardware and may also be used as an interlocking override or lockdown input.
public class SimulatedInput
{
    // Stable unique identifier for selection/filtering. For now this can be the Softwire device path.
    public string Id { get; set; } = string.Empty;

    // Friendly display name if one is available from Softwire.
    public string Name { get; set; } = string.Empty;

    // Full Softwire input device path.
    //
    // Example: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    public string DevicePath { get; set; } = string.Empty;

    // Last known visual/simulated active state.
    //
    // Important: When an input is shunted, Softwire may not report Active correctly. DoorSim may preserve this locally so the UI remains interactive.
    public bool IsActive { get; set; }

    // True when Softwire reports the input as shunted/bypassed.
    public bool IsShunted { get; set; }

    // Text shown in searchable selectors.
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
                return DevicePath;

            return $"{Name} ({DevicePath})";
        }
    }

    public override string ToString()
    {
        return DisplayName;
    }
}
