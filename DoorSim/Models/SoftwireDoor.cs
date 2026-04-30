using System;

namespace DoorSim.Models;

// Represents a door retrieved from Softwire.
// This is the high-level door object used by the UI.
// It will expand as I start to map more Softwire role/device information... WIP - JS 30th May 2026
public class SoftwireDoor
{
    // Basic door identity returned by Softwire (Href used for API calls)
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;

    // Hardware roles detected from the Softwire door Roles array
    public bool HasDoorSensor { get; set; }
    public bool HasLock { get; set; }
    public bool HasReaderSideIn { get; set; }
    public bool HasReaderSideOut { get; set; }
    public bool HasRexSideIn { get; set; }
    public bool HasRexSideOut { get; set; }
    public bool HasRexNoSide { get; set; } 
    public bool HasBreakGlass { get; set; }

    // Reader configuration (True when the reader mode is Card + PIN, False otherwise) and live state
    // Note: for LED colour, Softwire reports the colour (known values: Green and Red) but the UI may temporarily override this later for drag-hover, access granted/denied, etc.
    public bool InReaderRequiresCardAndPin { get; set; }
    public int InReaderPinTimeoutSeconds { get; set; }
    public bool InReaderIsOnline { get; set; }
    public bool InReaderIsShunted { get; set; }
    public string InReaderLedColor { get; set; } = "Red";

    public bool OutReaderRequiresCardAndPin { get; set; }
    public int OutReaderPinTimeoutSeconds { get; set; }
    public bool OutReaderIsOnline { get; set; }
    public bool OutReaderIsShunted { get; set; }
    public string OutReaderLedColor { get; set; } = "Red";

    // Last access decision reported by Softwire for this door.
    //
    // Used to show short reader feedback such as:
    // - Access granted
    // - Access denied
    //
    // LastDecision belongs to the door, but it can include the reader path, allowing the UI to show the result under the correct reader.
    public DateTime? LastDecisionTimeUtc { get; set; }
    public string LastDecisionReaderPath { get; set; } = string.Empty;
    public bool LastDecisionGranted { get; set; }
    public bool LastDecisionDenied { get; set; }

    // Live door state (Sensor and Lock)
    public bool DoorSensorIsOpen { get; set; }
    public bool DoorSensorIsShunted { get; set; }
    public bool DoorIsLocked { get; set; }
    public bool UnlockedForMaintenance { get; set; }

    // Live REX state
    public bool RexSideInIsActive { get; set; }
    public bool RexSideInIsShunted { get; set; }
    public bool RexSideOutIsActive { get; set; }
    public bool RexSideOutIsShunted { get; set; }
    public bool RexNoSideIsActive { get; set; }
    public bool RexNoSideIsShunted { get; set; }

    // Live Breakglass state
    public bool BreakGlassIsActive { get; set; }
    public bool BreakGlassIsShunted { get; set; }

    // Softwire device paths used to query or change hardware state (Example: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01)

    // Door Sensor
    public string DoorSensorDevicePath { get; set; } = string.Empty;
    
    // Readers
    public string ReaderSideInDevicePath { get; set; } = string.Empty;
    public string ReaderSideOutDevicePath { get; set; } = string.Empty;

    // REX
    public string RexSideInDevicePath { get; set; } = string.Empty;
    public string RexSideOutDevicePath { get; set; } = string.Empty;
    public string RexNoSideDevicePath { get; set; } = string.Empty;

    // Breakglass
    public string BreakGlassDevicePath { get; set; } = string.Empty;

    // Controls how the door appears in ComboBoxes
    public override string ToString() 
    {
        return Name;
    }
}
