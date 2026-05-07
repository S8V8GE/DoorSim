using DoorSim.Models;

namespace DoorSim.Services;

// Contract for all Softwire API interactions used by DoorSim.
//
// MainViewModel depends on this interface rather than SoftwireService directly, so the real HTTP implementation can later be replaced with a mock/fake implementation for testing or offline demonstrations.
//
// Implementations are responsible for:
//      - authentication/session handling,
//      - retrieving door and device state,
//      - simulating input changes,
//      - sending credential/PIN swipes to readers.
public interface ISoftwireService
{
    /*
      #############################################################################
                             Authentication & Connection
      #############################################################################
    */

    // Authenticates with a Softwire instance.
    //
    // On success, the implementation should preserve the authenticated session, usually through cookies, so later API calls are authorised.
    Task<bool> LoginAsync(string hostname, string username, string password);

    // Verifies that the current session is still valid.
    //
    // Typically calls a lightweight endpoint (e.g. /Doors/) to confirm connectivity.
    //
    // Returns:
    // - true  → Softwire is reachable and session is valid
    // - false → connection failed or session expired
    Task<bool> CheckConnectionAsync();


    /*
      #############################################################################
                                  Door Discovery
      #############################################################################
    */

    // Retrieves all Softwire doors used by DoorSim.
    //
    // Implementations usually:
    //      1. Call the door list endpoint.
    //      2. Fetch full details for each door.
    //      3. Map hardware roles/device paths into SoftwireDoor models.
    Task<List<SoftwireDoor>> GetDoorsAsync();


    /*
      #############################################################################
                               Device State Queries
      #############################################################################
    */

    // Retrieves all simulated Softwire inputs.
    //
    // Used by Door Interlocking Controls so the trainer can select override and lockdown inputs from the full simulated input list.
    Task<List<SimulatedInput>> GetSimulatedInputsAsync();

    // Retrieves the current state of a Softwire input.
    //
    // devicePath example:
    // /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    //
    // Returns an InputState object containing:
    // - Online   → device is online/reachable
    // - Active   → input is active (e.g. REX pressed, door open)
    // - IsShunted → input is shunted/disabled
    //
    // Returns null if the state could not be retrieved.
    Task<InputState?> GetInputStateAsync(string devicePath);

    // Retrieves the current state of a Softwire reader.
    //
    // readerPath example: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    //
    // Returns a ReaderState object containing:
    //      - Online   → reader is online/reachable
    //      - IsShunted → reader is shunted/disabled
    //      - LedColor → current LED color of the reader
    //
    // Returns null if the state could not be retrieved.
    Task<ReaderState?> GetReaderStateAsync(string readerPath);


    /*
      #############################################################################
                             Simulated Input Actions
      #############################################################################
    */

    // Sets the state of a Softwire input.
    //
    // inputPointer example: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01 
    //
    // state values:
    //      - "Active"   → input is active (e.g. REX pressed, door open)
    //      - "Inactive" → input is inactive
    //      - (others may exist depending on Softwire)
    //
    // Used to simulate hardware actions such as:
    //      - Door sensor open/close
    //      - REX press/release
    //      - breakglass activate/reset.
    //
    // Returns:
    //      - true  → request succeeded
    //      - false → request failed
    Task<bool> SetInputStateAsync(string inputPointer, string state);


    /*
      #############################################################################
                             Reader Swipe Actions
      #############################################################################
    */

    // Simulates a raw credential swipe on a Softwire reader.
    //
    // readerPointer example: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    //
    // bytes should be the raw hexadecimal credential value.
    // bitCount is the number of valid bits represented by that value.
    Task<bool> SwipeRawAsync(string readerPointer, string bytes, int bitCount);

    // Simulates a 26-bit Wiegand swipe on a Softwire reader.
    //
    // DoorSim uses this for PIN entry. Softwire expects PINs to be sent as Wiegand26 with:
    //      - Facility = 0
    //      - Card     = entered PIN
    //
    // readerPointer example: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    Task<bool> SwipeWiegand26Async(string readerPointer, int facility, int card);

}
