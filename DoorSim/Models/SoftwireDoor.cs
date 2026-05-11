namespace DoorSim.Models;

// Represents a door retrieved from Softwire.
//
// This model combines:
//      - static door identity,
//      - detected hardware roles,
//      - Softwire device paths,
//      - live hardware state used by the simulator UI,
//      - the most recent access decision reported by Softwire.
public class SoftwireDoor
{
    /*
      #############################################################################
                                  Door Identity
      #############################################################################
    */
    // Basic door identity returned by Softwire (Href used for API calls)
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Href { get; set; } = string.Empty;


    /*
      #############################################################################
                             Detected Hardware Roles
      #############################################################################
    */

    // Hardware roles detected from the Softwire door Roles array
    public bool HasDoorSensor { get; set; }
    public bool HasLock { get; set; }
    public bool HasReaderSideIn { get; set; }
    public bool HasReaderSideOut { get; set; }
    public bool HasRexSideIn { get; set; }
    public bool HasRexSideOut { get; set; }
    public bool HasRexNoSide { get; set; } 
    public bool HasBreakGlass { get; set; }


    /*
  #############################################################################
                       Door Behaviour Configuration
  #############################################################################
*/

    // Door timing and behaviour configuration parsed from the Softwire door JSON.
    //
    // These values are mainly used by Auto Mode to decide which doors are suitable for different simulated events:
    //
    //      - GrantTimeSeconds:
    //          How long a normal access grant lasts.
    //
    //      - ExtendedGrantTimeSeconds:
    //          How long an extended access grant lasts.
    //
    //      - DoorHeldTimeSeconds:
    //          How long the door must remain open before Softwire generates a door-held-open event.
    //
    //      - AutoUnlockOnRex:
    //          Whether activating a REX input can unlock the door.
    //
    //      - EnforceDoorForcedOpen:
    //          Whether opening the door without a valid unlock should generate
    //          a door-forced-open event.
    //
    //      - IgnoreHeldOpenWhenUnlocked:
    //          Whether Softwire ignores held-open logic while the door is unlocked.
    public int GrantTimeSeconds { get; set; }
    public int ExtendedGrantTimeSeconds { get; set; }
    public int DoorHeldTimeSeconds { get; set; }
    public bool AutoUnlockOnRex { get; set; }
    public bool EnforceDoorForcedOpen { get; set; }
    public bool IgnoreHeldOpenWhenUnlocked { get; set; }


    /*
      #############################################################################
                       In Reader Configuration and Live State
      #############################################################################
    */

    // In reader configuration and live state.
    // RequiresCardAndPin / PinTimeoutSeconds come from reader configuration.
    // Online / IsShunted / LedColor come from the reader device endpoint.
    public bool InReaderRequiresCardAndPin { get; set; }
    public int InReaderPinTimeoutSeconds { get; set; }
    public bool InReaderIsOnline { get; set; }
    public bool InReaderIsShunted { get; set; }
    public string InReaderLedColor { get; set; } = "Red";


    /*
      #############################################################################
                       Out Reader Configuration and Live State
      #############################################################################
    */

    // Out reader configuration and live state.
    // RequiresCardAndPin / PinTimeoutSeconds come from reader configuration.
    // Online / IsShunted / LedColor come from the reader device endpoint.
    public bool OutReaderRequiresCardAndPin { get; set; }
    public int OutReaderPinTimeoutSeconds { get; set; }
    public bool OutReaderIsOnline { get; set; }
    public bool OutReaderIsShunted { get; set; }
    public string OutReaderLedColor { get; set; } = "Red";


    /*
      #############################################################################
                           Last Access Decision
      #############################################################################
    */

    // Most recent access decision reported by Softwire for this door.
    // MainViewModel uses this after a pending card/PIN action to show temporary "Access granted" or "Access denied" feedback under the correct reader.
    public DateTime? LastDecisionTimeUtc { get; set; }
    // Reader path associated with the last decision, when Softwire provides it. Stored so decision matching can be tightened later if needed.
    public string LastDecisionReaderPath { get; set; } = string.Empty;
    public bool LastDecisionGranted { get; set; }
    public bool LastDecisionDenied { get; set; }


    /*
      #############################################################################
                           Door Lock and Sensor State
      #############################################################################
    */

    // Live door state (Sensor and Lock)
    public bool DoorSensorIsOpen { get; set; }
    public bool DoorSensorIsShunted { get; set; }
    public bool DoorIsLocked { get; set; }
    public bool UnlockedForMaintenance { get; set; }


    /*
      #############################################################################
                               REX Input State
      #############################################################################
    */

    // Live REX state
    public bool RexSideInIsActive { get; set; }
    public bool RexSideInIsShunted { get; set; }
    public bool RexSideOutIsActive { get; set; }
    public bool RexSideOutIsShunted { get; set; }
    public bool RexNoSideIsActive { get; set; }
    public bool RexNoSideIsShunted { get; set; }


    /*
      #############################################################################
                               Breakglass Input State
      #############################################################################
    */

    // Live Breakglass state
    public bool BreakGlassIsActive { get; set; }
    public bool BreakGlassIsShunted { get; set; }


    /*
      #############################################################################
                               Softwire Device Paths
      #############################################################################
    */

    // Softwire device paths used for live state queries and simulated input actions.
    // Example: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01

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


    /*
      #############################################################################
                               Display Helpers
      #############################################################################
    */

    // Controls how the door appears in ComboBoxes
    public override string ToString() 
    {
        return Name;
    }

}
