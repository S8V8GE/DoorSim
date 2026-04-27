using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DoorSim.Services;

public interface ISoftwireService
{
    Task<bool> LoginAsync(string hostname, string username, string password);
}
