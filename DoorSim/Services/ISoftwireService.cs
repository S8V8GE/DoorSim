using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DoorSim.Models;

namespace DoorSim.Services;

// Defines the "contract" for interacting with Softwire.
//
// This interface ensures that any Softwire service implementation
// provides the required functionality (e.g., login).
//
// The ViewModel depends on this interface rather than a concrete class,
// allowing the implementation to be swapped (e.g., real vs mock service) for testing, without changing UI logic.

public interface ISoftwireService
{

    // Authenticates with a Softwire instance.
    //
    // Returns:
    // - true  → login successful
    // - false → login failed
    //
    // On success, the implementation will maintain a session (typically via cookies)
    // so subsequent API calls are authenticated.
    Task<bool> LoginAsync(string hostname, string username, string password);


    // Verifies that the current session is still valid.
    //
    // Typically calls a lightweight endpoint (e.g. /Doors/) to confirm connectivity.
    //
    // Returns:
    // - true  → Softwire is reachable and session is valid
    // - false → connection failed or session expired
    Task<bool> CheckConnectionAsync();


    // Retrieves all doors from Softwire.
    //
    // This usually involves:
    // 1. Calling the door list endpoint (/Doors/)
    // 2. Fetching full details for each door via its Href
    //
    // Returns a list of SoftwireDoor models used by the UI.
    Task<List<SoftwireDoor>> GetDoorsAsync();


    // Retrieves the current state of a Softwire input.
    //
    // devicePath example:
    // /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    //
    // Returns an InputState object containing:
    // - Active   → input is active (e.g. REX pressed, door open)
    // - IsShunted → input is shunted/disabled
    //
    // Returns null if the state could not be retrieved.
    Task<InputState?> GetInputStateAsync(string devicePath);


    // Sets the state of a Softwire input.
    //
    // inputPointer example:
    // /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01 
    //
    // state values:
    // - "Active"
    // - "Inactive"
    // - (others may exist depending on Softwire)
    //
    // Used to simulate hardware actions such as:
    // - Door sensor open/close
    // - REX press/release
    //
    // Returns:
    // - true  → request succeeded
    // - false → request failed
    Task<bool> SetInputStateAsync(string inputPointer, string state); 

}
