# DoorSim

**Softwire Door Simulation & Testing Tool**

![C#](https://img.shields.io/badge/C%23-WPF-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-purple)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![Status](https://img.shields.io/badge/status-v1.0%20stable--release-brightgreen)
![License](https://img.shields.io/badge/license-Source--Available%20Non--Commercial-orange)

DoorSim is a Windows desktop training, testing, and demonstration tool for interacting with simulated Genetec Softwire access control doors.

It provides a visual way to work with simulated doors, cardholders, readers, REX inputs, breakglass/manual station inputs, interlocking scenarios, and automated busy-site event generation — without requiring physical access control hardware.

---

## Overview

DoorSim was created to support technical training, demos, testing, and troubleshooting in lab environments where access control behaviour needs to be demonstrated clearly.

It connects to a local Genetec Softwire simulation environment and allows trainers or technical users to interact with simulated access control hardware visually.

DoorSim is intended for:

- Training sessions
- Lab testing
- Demo environments
- Access control behaviour demonstrations
- Softwire simulation exercises
- Internal learning and troubleshooting scenarios

It is **not** intended for production use.

---

## Features

### Manual Mode

DoorSim includes a full manual simulation mode for interacting with one or two simulated doors.

Manual Mode supports:

- Connecting to a local Softwire environment
- Loading doors from Softwire
- Loading cardholders from the Genetec Security Center Directory database
- Single Door View
- Two Door View
- Searchable door selectors
- Cardholder drag/drop onto readers
- Card-only reader testing
- PIN and Card + PIN testing
- Auto-enrol credential generation
- Reader feedback text and audio
- Door sensor open/close interaction
- REX press/release interaction
- Breakglass/manual station activation/reset
- Interlocking override/lockdown controls
- Live polling of door, reader, input, and lock state

### Auto Mode

Auto Mode generates automated access control activity for training, demos, and stress testing.

Auto Mode supports:

- Configurable number of events
- Extreme, relaxed, and custom delay modes
- Event profiles with weighted Normal / Forced / Held event generation
- Live event log
- Running summary counters
- Normal reader/cardholder events
- Normal Card + PIN events
- Normal REX events
- Forced-door events
- Held-open reader events
- Held-open REX events
- Held-door reservation to prevent event overlap
- Cleanup when stopping Auto Mode
- Retry guard to prevent infinite loops when the environment cannot generate valid events
- Safe handling if Softwire becomes unavailable during simulation

---

## Requirements

DoorSim is designed for Windows lab environments using Genetec Security Center and Softwire simulation.

Required:

- Windows
- Genetec Security Center installed
- A Security Center licence that includes Softwire
- Softwire downloaded and installed on the same server
- Genetec Security Center SQL database on the same server
- Softwire simulation enabled
- Local/training/demo environment

DoorSim is built with:

- C#
- WPF
- .NET 8

The release installer is intended to package the required .NET runtime, so end users should not normally need to install .NET manually.

---

## Important Disclaimer

DoorSim is an independent training, testing, and demonstration tool designed to interact with simulated Genetec Softwire environments.

DoorSim is **not** an official Genetec Inc. product and is not endorsed, supported, maintained, or warranted by Genetec Inc.

This tool is intended for local training, lab testing, and demonstration use only. It should not be used with production systems or live customer environments.

DoorSim sends simulated access control actions to Softwire and can activate simulated inputs such as door sensors, REX inputs, breakglass/manual station inputs, and interlocking inputs. Use it only in environments where you understand the configuration and have permission to test.

---

## Documentation

A full user guide is provided with the application and can be opened from the **Help** menu inside DoorSim.

The bundled guide covers:

- Connecting to Softwire
- Manual Mode
- Single Door View
- Two Door View
- Cardholder testing
- PIN and Card + PIN testing
- Door sensor, REX, and breakglass/manual station controls
- Interlocking controls
- Auto Mode
- Event profiles
- Event log messages
- Troubleshooting
- Known limitations

---

## Quick Start

1. Install or launch DoorSim on the Security Center / Softwire lab server.
2. Make sure Softwire is installed and running.
3. Make sure the Security Center Directory SQL database is available locally.
4. Open DoorSim.
5. Click **Connect**.
6. Enter the Softwire host and password.
7. Select a door in Manual Mode, or switch to **Mode > Auto Mode** for automated simulation.

---

## Project Structure

```text
Help/
    DoorSim_UserGuide.pdf
    DoorSim_Project_Map.txt

Images/
    Application and hardware UI images

Models/
    Data models used by the application

Services/
    Softwire, SQL, credential, and sound services

Sounds/
    Reader and feedback audio files

ViewModels/
    MVVM application logic

Views/
    WPF windows and user controls
```
For a more detailed developer-focused file map, see: ```Help/DoorSim_Project_Map.txt```

---

## Known Limitations

DoorSim is designed for simulation and training environments only.

Known considerations:

- DoorSim expects Softwire, Security Center, and SQL to be available on the same server.
- DoorSim is not designed for production access control systems.
- If Softwire is stopped or restarted during use, DoorSim will safely return to a reconnect state where possible.
- Some Softwire/Security Center states may persist after deleting and recreating doors. For example, REX or breakglass inputs may remain reported as shunted until maintenance mode is toggled in Security Desk.
- Auto Mode relies on the current door, cardholder, and Softwire configuration. If no suitable doors or cardholders exist, Auto Mode will retry and eventually stop using its retry guard.

---

## Reporting Issues

Please report bugs, problems, or improvement ideas using GitHub Issues:

[Report an issue](https://github.com/S8V8GE/DoorSim/issues)

When reporting an issue, please include:

- DoorSim version
- Security Center / Softwire version if known
- What you were trying to do
- What happened
- What you expected to happen
- Screenshots or log output if available
- Whether the issue occurred in Manual Mode or Auto Mode

---

## Source Code

The source code is available here:

[https://github.com/S8V8GE/DoorSim](https://github.com/S8V8GE/DoorSim)

---

## License

DoorSim is released under a source-available, non-commercial licence.

You may view, download, use, copy, and modify the software for personal,
educational, internal training, lab testing, and demonstration purposes.

Commercial sale, resale, paid redistribution, sublicensing, hosted-service use,
or monetisation of DoorSim or modified versions is not permitted without prior
written permission.

See the [LICENSE](LICENSE) file for details.

---

## Author

Created by **James Savage**.
