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
// allowing the implementation to be swapped (e.g., real vs mock service)
// for testing, without changing UI logic.

public interface ISoftwireService
{
    Task<bool> LoginAsync(string hostname, string username, string password);

    Task<bool> CheckConnectionAsync();

    Task<List<SoftwireDoor>> GetDoorsAsync();

    Task<bool> GetInputStateAsync(string devicePath);

    Task<bool> SetInputStateAsync(string inputPointer, string state);

}
