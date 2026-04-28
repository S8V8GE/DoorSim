namespace DoorSim.Models;

// Represents a door retrieved from Softwire.
// This is the high-level door object used by the UI.
// It will expand as I start to map more Softwire role/device information... WIP - JS 28th May 2026
public class SoftwireDoor
{
    // Door Id and Name
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;

    // Door Hardware
    public bool HasDoorSensor { get; set; }
    public bool HasLock { get; set; }
    public bool HasReaderSideIn { get; set; }
    public bool HasReaderSideOut { get; set; }
    public bool HasRexSideIn { get; set; }
    public bool HasRexSideOut { get; set; }
    public bool HasRexNoSide { get; set; } 
    public bool HasBreakGlass { get; set; }

    // Door status
    public bool DoorSensorIsOpen { get; set; }
    public bool DoorIsLocked { get; set; }
    public bool UnlockedForMaintenance { get; set; }

    // Softwire input path for the door sensor.
    // Example: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    public string DoorSensorDevicePath { get; set; } = string.Empty;

    // Controls how the door appears in ComboBoxes
    public override string ToString() 
    {
        return Name;
    }

}
