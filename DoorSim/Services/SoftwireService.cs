using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using DoorSim.Models;
using System.Text.Json;

namespace DoorSim.Services;

// Concrete HTTP implementation of ISoftwireService.
//
// Responsibilities:
//      - authenticate with Softwire,
//      - maintain the cookie-based session,
//      - call Softwire HTTP endpoints,
//      - parse Softwire JSON into DoorSim models,
//      - simulate input changes and reader swipes.
//
// This implementation is intended for DoorSim training/demo environments.
// It currently accepts self-signed certificates during login; do not reuse that behaviour in production software!


public class SoftwireService : ISoftwireService 
{

    /*
      #############################################################################
                               HTTP Session State
      #############################################################################
    */

    // Authenticated HTTP client used for all Softwire API calls.
    // Created after a successful login attempt.
    private HttpClient? _client;

    // Cookie container used to preserve the Softwire login session.
    private CookieContainer? _cookies;


    /*
      #############################################################################
                         Authentication and Connection
      #############################################################################
    */

    // Attempts to authenticate with Softwire using provided credentials.
    //
    // Steps:
    //      1. Create HTTP client with cookie support
    //      2. Send login request to /Login endpoint
    //      3. Store session cookies if successful
    //
    // Returns:
    //      - true  → login successful
    //      - false → login failed
    public async Task<bool> LoginAsync(string hostname, string username, string password)
    {
        // Create a new cookie container to store session cookies
        _cookies = new CookieContainer();

        // Configure HTTP handler to:
        // - Use cookies for session management
        // - Ignore SSL certificate validation (for local/self-signed Softwire)
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            // WARNING:
            // This bypasses SSL certificate validation and accepts all certificates.
            //
            // This is required for:
            // - Local Softwire instances
            // - Training and lab environments using self-signed certificates
            //
            // DO NOT use in production environments, as it exposes the application
            // to man-in-the-middle (MITM) attacks.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        // Create HTTP client pointing to the Softwire base URL
        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{hostname}")
        };

        // Configure request headers to expect JSON responses
        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        // Build login form data (URL-encoded)
        var loginBody = new StringContent(
            $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&Login=Login",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        // Send login request to Softwire
        var response = await _client.PostAsync("/Login", loginBody);

        // Return true if login succeeded (HTTP 200), otherwise false
        return response.IsSuccessStatusCode;
    }


    // Checks if the current session is still valid by calling a simple endpoint.
    // Returns true if Softwire responds successfully.
    public async Task<bool> CheckConnectionAsync()
    {
        // Checks whether the current HTTP session still works by calling a lightweight endpoint used elsewhere by DoorSim.
        if (_client == null)
            return false;

        try
        {
            var response = await _client.GetAsync("/Doors/");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }


    /*
      #############################################################################
                               Door Discovery
      #############################################################################
    */

    // Retrieves doors from Softwire and maps the raw JSON into clean SoftwireDoor objects.
    //
    // Softwire door data is split across:
    //      - The door list endpoint (/Doors/)
    //      - Each door detail endpoint (Href)
    //      - The Roles array inside each door detail response
    //
    // This method extracts high-level door information plus hardware role information
    // such as lock, door sensor, readers, and reader modes.
    public async Task<List<SoftwireDoor>> GetDoorsAsync()
    {
        // Collection that will hold all parsed doors returned to the UI.
        var doors = new List<SoftwireDoor>();

        // If we are not connected (no HTTP client), return an empty list.
        // Prevents null reference issues and keeps calling code simple.
        if (_client == null)
            return doors;

        // Step 1: Retrieve the list of doors from Softwire.
        // This endpoint only returns summary objects (including Href),
        // not the full door configuration.
        var listResponse = await _client.GetAsync("/Doors/");

        // If the request failed, return an empty list.
        // We don't throw here because the UI handles "no doors" gracefully.
        if (!listResponse.IsSuccessStatusCode)
            return doors;

        // Read and parse the door list JSON response.
        var listJson = await listResponse.Content.ReadAsStringAsync();

        using var listDocument = JsonDocument.Parse(listJson);

        // Step 2: Iterate through each door summary.
        // Each item contains an Href that points to full door details.
        foreach (var item in listDocument.RootElement.EnumerateArray())
        {
            // Extract the Href for the full door configuration.
            // If missing, skip this entry.
            if (!item.TryGetProperty("Href", out var hrefProperty))
                continue;

            var href = hrefProperty.GetString();

            if (string.IsNullOrWhiteSpace(href))
                continue;

            // Step 3: Retrieve the full door configuration using the Href.
            var doorResponse = await _client.GetAsync(href);

            // Skip this door if the detail request failed.
            if (!doorResponse.IsSuccessStatusCode)
                continue;

            var doorJson = await doorResponse.Content.ReadAsStringAsync();

            // Parse the full door JSON.
            // This contains Roles, hardware mappings, and state information.
            using var doorDocument = JsonDocument.Parse(doorJson);
            var door = doorDocument.RootElement;

            // Temporary role-mapping variables for the current door.
            //
            // Softwire stores hardware assignments in the Roles array rather than as simple top-level door properties.
            // We collect the relevant role flags and device paths here, then use them to build one SoftwireDoor model at the end.
            //
            // Hardware role flags and device paths extracted from the Roles array. These are reset for each door being parsed.

            // Door Sensor
            bool hasDoorSensor = false;
            string doorSensorPath = "";

            // Lock/Strike
            bool hasLock = false;

            // Readers (Side A/In and Side B/Out)
            bool hasReaderSideIn = false;
            bool hasReaderSideOut = false;
            string readerSideInPath = "";
            string readerSideOutPath = "";
            bool inReaderRequiresCardAndPin = false;
            int inReaderPinTimeoutSeconds = 0;
            bool outReaderRequiresCardAndPin = false;
            int outReaderPinTimeoutSeconds = 0;

            // REX (Request to Exit) devices
            bool hasRexSideIn = false;
            bool hasRexSideOut = false;
            bool hasRexNoSide = false;
            string rexSideInPath = "";
            string rexSideOutPath = "";
            string rexNoSidePath = "";

            // Breakglass / ManualStation
            bool hasBreakGlass = false;
            string breakGlassPath = "";

            // Last access decision reported by Softwire for this door.
            // Used later to show Granted / Denied feedback under the correct reader.
            DateTime? lastDecisionTimeUtc = null;
            string lastDecisionReaderPath = "";
            bool lastDecisionGranted = false;
            bool lastDecisionDenied = false;

            // Inspect each role assigned to the door.
            // Roles describe which Softwire devices are acting as:
            // - OpenSensor
            // - Strike
            // - ReaderAuth
            // - REX
            // - Breakglass / Manual station
            if (door.TryGetProperty("Roles", out var roles))
            {
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.TryGetProperty("Type", out var type))
                    {
                        // Door sensor role.
                        // Provides the input path used later to read open/closed state.
                        if (type.TryGetProperty("OpenSensor", out var openSensor))
                        {
                            hasDoorSensor = true;

                            if (openSensor.TryGetProperty("Device", out var device))
                            {
                                doorSensorPath = device.GetString() ?? "";
                            }
                        }

                        // Strike role.
                        // Indicates that the door has a lock/strike configured.
                        if (type.TryGetProperty("Strike", out var strike))
                        {
                            hasLock = true;
                        }

                        // Reader role.
                        // Provides the reader device path, side (A/In or B/Out),
                        // and whether the reader is configured for Card + PIN.
                        if (type.TryGetProperty("ReaderAuth", out var readerAuth))
                        {
                            var side = "";

                            // Softwire uses Side A/B.
                            // In this app I interpret A as In and B as Out.
                            if (role.TryGetProperty("Side", out var roleSide))
                            {
                                if (roleSide.TryGetProperty("A", out _))
                                    side = "A";

                                if (roleSide.TryGetProperty("B", out _))
                                    side = "B";
                            }

                            var readerPath = "";

                            if (readerAuth.TryGetProperty("HardwareReader", out var hardwareReader))
                            {
                                readerPath = hardwareReader.GetString() ?? "";
                            }

                            // ReaderMode tells us whether this reader is Normal, CardAndPin, etc.
                            var requiresCardAndPin = false;
                            var pinTimeoutSeconds = 0;

                            if (readerAuth.TryGetProperty("ReaderMode", out var readerMode))
                            {
                                if (readerMode.TryGetProperty("CardAndPin", out var cardAndPin))
                                {
                                    requiresCardAndPin = true;

                                    // Softwire stores Card + PIN timeout in milliseconds.
                                    // Convert to seconds for the PIN countdown window.
                                    if (cardAndPin.TryGetProperty("Timeout", out var timeoutMs))
                                    {
                                        pinTimeoutSeconds = timeoutMs.GetInt32() / 1000;
                                    }
                                }
                            }

                            if (side == "A")
                            {
                                hasReaderSideIn = true;
                                readerSideInPath = readerPath;
                                inReaderRequiresCardAndPin = requiresCardAndPin;
                                inReaderPinTimeoutSeconds = pinTimeoutSeconds;
                            }

                            if (side == "B")
                            {
                                hasReaderSideOut = true;
                                readerSideOutPath = readerPath;
                                outReaderRequiresCardAndPin = requiresCardAndPin;
                                outReaderPinTimeoutSeconds = pinTimeoutSeconds;
                            }
                        }

                        // REX role.
                        // Provides the input path used to simulate request-to-exit.
                        // REX may be assigned to Side A, Side B, or no side (NA).
                        if (type.TryGetProperty("REX", out var rex))
                        {
                            var side = "";

                            // Softwire uses Side A/B/NA.
                            // In this app I interpret A as In, B as Out, and NA as no side.
                            if (role.TryGetProperty("Side", out var roleSide))
                            {
                                if (roleSide.TryGetProperty("A", out _))
                                    side = "A";

                                if (roleSide.TryGetProperty("B", out _))
                                    side = "B";

                                if (roleSide.TryGetProperty("NA", out _))
                                    side = "NA";
                            }

                            var rexPath = "";

                            if (rex.TryGetProperty("Device", out var device))
                            {
                                rexPath = device.GetString() ?? "";
                            }

                            if (side == "A")
                            {
                                hasRexSideIn = true;
                                rexSideInPath = rexPath;
                            }

                            if (side == "B")
                            {
                                hasRexSideOut = true;
                                rexSideOutPath = rexPath;
                            }

                            if (side == "NA")
                            {
                                hasRexNoSide = true;
                                rexNoSidePath = rexPath;
                            }
                        }

                        // Breakglass / manual station role.
                        // Provides the input path used to detect or simulate emergency door release.
                        if (type.TryGetProperty("ManualStation", out var manualStation))
                        {
                            hasBreakGlass = true;

                            if (manualStation.TryGetProperty("Device", out var device))
                            {
                                breakGlassPath = device.GetString() ?? "";
                            }
                        }
                    }
                }
            }

            // Parse the latest access decision reported by Softwire.
            // The reader path is stored so MainViewModel can later tighten decision matching if needed.
            // Current UI routing primarily uses the pending reader action registered when the swipe/PIN was sent.
            if (door.TryGetProperty("LastDecision", out var lastDecision))
            {
                if (lastDecision.TryGetProperty("TimeStampUtc", out var timestamp))
                {
                    if (DateTime.TryParse(timestamp.GetString(), out var parsedTimestamp))
                    {
                        lastDecisionTimeUtc = parsedTimestamp.ToUniversalTime();
                    }
                }

                if (lastDecision.TryGetProperty("Reader", out var reader))
                {
                    lastDecisionReaderPath = reader.GetString() ?? "";
                }

                if (lastDecision.TryGetProperty("Decision", out var decision))
                {
                    lastDecisionGranted = decision.TryGetProperty("Granted", out _);
                    lastDecisionDenied = decision.TryGetProperty("Denied", out _);
                }
            }

            // Convert the parsed JSON values into a clean UI-friendly model.
            doors.Add(new SoftwireDoor
            {
                Href = href,
                Id = door.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
                Name = door.TryGetProperty("Name", out var name) ? name.GetString() ?? "" : "",
                DoorIsLocked = door.TryGetProperty("IsLocked", out var locked) && locked.GetBoolean(),
                UnlockedForMaintenance = door.TryGetProperty("UnlockedForMaintenance", out var maintenance) && maintenance.GetBoolean(),
                HasDoorSensor = hasDoorSensor,
                HasLock = hasLock,
                DoorSensorDevicePath = doorSensorPath,
                HasReaderSideIn = hasReaderSideIn,
                HasReaderSideOut = hasReaderSideOut,
                ReaderSideInDevicePath = readerSideInPath,
                ReaderSideOutDevicePath = readerSideOutPath,
                InReaderRequiresCardAndPin = inReaderRequiresCardAndPin,
                InReaderPinTimeoutSeconds = inReaderPinTimeoutSeconds,
                OutReaderRequiresCardAndPin = outReaderRequiresCardAndPin,
                OutReaderPinTimeoutSeconds = outReaderPinTimeoutSeconds,
                HasRexSideIn = hasRexSideIn,
                HasRexSideOut = hasRexSideOut,
                HasRexNoSide = hasRexNoSide,
                RexSideInDevicePath = rexSideInPath,
                RexSideOutDevicePath = rexSideOutPath,
                RexNoSideDevicePath = rexNoSidePath,
                HasBreakGlass = hasBreakGlass,
                BreakGlassDevicePath = breakGlassPath,
                LastDecisionTimeUtc = lastDecisionTimeUtc,
                LastDecisionReaderPath = lastDecisionReaderPath,
                LastDecisionGranted = lastDecisionGranted,
                LastDecisionDenied = lastDecisionDenied
            });
        }

        return doors.OrderBy(d => d.Name).ToList();
    }


    /*
      #############################################################################
                               Device State Queries
      #############################################################################
    */

    // Retrieves all simulated input devices from Softwire.
    //
    // Softwire exposes configured devices at:  /Devices/?details=true
    //
    // The response shape can vary, so this method scans the returned JSON tree and
    // accepts any object that contains a simulated input Href.
    //
    // Expected input Href:  /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    public async Task<List<SimulatedInput>> GetSimulatedInputsAsync()
    {
        var inputs = new List<SimulatedInput>();

        if (_client == null)
            return inputs;

        var response = await _client.GetAsync("/Devices/?details=true");

        if (!response.IsSuccessStatusCode)
            return inputs;

        var json = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(json);

        ScanForSimulatedInputs(document.RootElement, inputs);

        return inputs
            .OrderBy(i => i.Name)
            .ThenBy(i => i.DevicePath)
            .ToList();
    }

    // Recursively scans any JSON shape returned by /Devices/?details=true.
    //
    // This avoids depending on whether Softwire returns:
    // - an object with Input/Output/Reader arrays,
    // - a flat array,
    // - nested wrapper objects.
    //
    // Any nested object with a simulated input Href is accepted.
    private static void ScanForSimulatedInputs(JsonElement element, List<SimulatedInput> inputs)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            AddSimulatedInputIfValid(element, inputs);

            foreach (var property in element.EnumerateObject())
            {
                ScanForSimulatedInputs(property.Value, inputs);
            }

            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                ScanForSimulatedInputs(item, inputs);
            }
        }
    }

    // Adds a device to the simulated input list if it looks like a SIM input device.
    //
    // We identify inputs from the Href because it is the most reliable identifier
    // for a Softwire simulated input endpoint.
    private static void AddSimulatedInputIfValid(JsonElement device, List<SimulatedInput> inputs)
    {
        if (device.ValueKind != JsonValueKind.Object)
            return;

        var href = device.TryGetProperty("Href", out var hrefElement)
            ? hrefElement.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(href))
            return;

        // Keep simulated input devices only.
        if (!href.Contains("/Input/", StringComparison.OrdinalIgnoreCase))
            return;

        if (!href.Contains("/Bus/Sim/", StringComparison.OrdinalIgnoreCase))
            return;

        // Avoid duplicates if the same device appears in more than one nested part
        // of the response.
        if (inputs.Any(i => string.Equals(i.DevicePath, href, StringComparison.OrdinalIgnoreCase)))
            return;

        var displayName = device.TryGetProperty("DisplayName", out var displayNameElement)
            ? displayNameElement.GetString() ?? string.Empty
            : string.Empty;

        var isActive = device.TryGetProperty("Active", out var activeElement) &&
                       activeElement.ValueKind == JsonValueKind.True;

        var isShunted = device.TryGetProperty("IsShunted", out var shuntedElement) &&
                        shuntedElement.ValueKind == JsonValueKind.True;

        inputs.Add(new SimulatedInput
        {
            Id = href,
            Name = displayName,
            DevicePath = href,
            IsActive = isActive,
            IsShunted = isShunted
        });
    }

    // Retrieves the current state of a Softwire input device.
    //
    // Used for:
    //      - Door sensors
    //      - REX buttons
    //      - Breakglass inputs
    //
    // Softwire returns these states directly on the input object:
    //      - Online
    //      - Active
    //      - IsShunted
    public async Task<InputState?> GetInputStateAsync(string devicePath)
    {
        if (_client == null || string.IsNullOrWhiteSpace(devicePath))
            return null;

        var response = await _client.GetAsync(devicePath);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Input", out var input))
            return null;

        return new InputState
        {
            Online = input.TryGetProperty("Online", out var online) && online.GetBoolean(),
            Active = input.TryGetProperty("Active", out var active) && active.GetBoolean(),
            IsShunted = input.TryGetProperty("IsShunted", out var shunted) && shunted.GetBoolean()
        };
    }


    // Retrieves the current state of a Softwire reader device.
    //
    // Softwire reader endpoints return state information such as:
    //      - Online
    //      - IsShunted
    //      - LedColor
    //
    // Example reader path: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    public async Task<ReaderState?> GetReaderStateAsync(string readerPath)
    {
        if (_client == null || string.IsNullOrWhiteSpace(readerPath))
            return null;

        var response = await _client.GetAsync(readerPath);

        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("Reader", out var reader))
            return null;

        // Softwire represents LED colour as a discriminated object, for example: "LedColor": { "Green": [] }
        var ledColor = "Red";

        if (reader.TryGetProperty("LedColor", out var ledColorElement))
        {
            if (ledColorElement.TryGetProperty("Green", out _))
                ledColor = "Green";
            else if (ledColorElement.TryGetProperty("Red", out _))
                ledColor = "Red";
        }

        return new ReaderState
        {
            Online = reader.TryGetProperty("Online", out var online) && online.GetBoolean(),
            IsShunted = reader.TryGetProperty("IsShunted", out var shunted) && shunted.GetBoolean(),
            LedColor = ledColor
        };
    }


    /*
      #############################################################################
                               Simulated Input Actions
      #############################################################################
    */

    // Softwire expects simulated input state changes to be sent to: /{bus}/{iface}/Input
    //
    // Example:
    // Input pointer: /Devices/Bus/Sim/Port_A/Iface/1/Input/IN_01
    // PUT URI:       /Sim/Port_A/1/Input
    //
    // SetInputStateAsync sets the state of a simulated Softwire input.
    // Used to simulate door sensor changes, REX presses, and breakglass activation.
    public async Task<bool> SetInputStateAsync(string inputPointer, string state)
    {
        if (_client == null || string.IsNullOrWhiteSpace(inputPointer))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            inputPointer,
            @"/Devices/Bus/(.+)/Iface/([^/]+)/(.*)");

        if (!match.Success)
            return false;

        var bus = match.Groups[1].Value;
        var iface = match.Groups[2].Value;

        var uri = $"/{bus}/{iface}/Input";

        var body = new
        {
            Input = inputPointer,
            State = new Dictionary<string, object[]>
            {
                [state] = Array.Empty<object>()
            }
        };

        var json = JsonSerializer.Serialize(body);

        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        var response = await _client.PutAsync(uri, content);

        return response.IsSuccessStatusCode;
    }


    /*
      #############################################################################
                               Reader Swipe Actions
      #############################################################################
    */

    // Simulates a raw credential swipe on a Softwire reader.
    //
    // Softwire expects raw credential swipes to be sent to: /{bus}/{iface}/SwipeRaw
    //
    // Example:
    // Reader pointer: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    // PUT URI:        /Sim/Port_A/1/SwipeRaw
    //
    // Body:
    // {
    //   Reader:   readerPointer,
    //   Bytes:    hexadecimal credential value,
    //   BitCount: number of valid bits
    // }
    public async Task<bool> SwipeRawAsync(string readerPointer, string bytes, int bitCount)
    {
        if (_client == null || string.IsNullOrWhiteSpace(readerPointer))
            return false;

        if (string.IsNullOrWhiteSpace(bytes))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            readerPointer,
            @"/Devices/Bus/(.+)/Iface/([^/]+)/(.*)");

        if (!match.Success)
            return false;

        var bus = match.Groups[1].Value;
        var iface = match.Groups[2].Value;

        var uri = $"/{bus}/{iface}/SwipeRaw";

        var body = new
        {
            Reader = readerPointer,
            Bytes = bytes,
            BitCount = bitCount
        };

        var json = JsonSerializer.Serialize(body);

        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        var response = await _client.PutAsync(uri, content);

        return response.IsSuccessStatusCode;
    }


    // Simulates a 26-bit Wiegand swipe on a Softwire reader.
    //
    // Used for PIN entry in this app.
    // Softwire expects this to be sent to: /{bus}/{iface}/SwipeWiegand26
    //
    // Example:
    // Reader pointer: /Devices/Bus/Sim/Port_A/Iface/1/Reader/READER_01
    // PUT URI:        /Sim/Port_A/1/SwipeWiegand26
    //
    // Body:
    // {
    //   Reader: readerPointer,
    //   FAC:    facility,
    //   Card:   card
    // }
    public async Task<bool> SwipeWiegand26Async(string readerPointer, int facility, int card)
    {
        if (_client == null || string.IsNullOrWhiteSpace(readerPointer))
            return false;

        var match = System.Text.RegularExpressions.Regex.Match(
            readerPointer,
            @"/Devices/Bus/(.+)/Iface/([^/]+)/(.*)");

        if (!match.Success)
            return false;

        var bus = match.Groups[1].Value;
        var iface = match.Groups[2].Value;

        var uri = $"/{bus}/{iface}/SwipeWiegand26";

        var body = new
        {
            Reader = readerPointer,
            FAC = facility,
            Card = card
        };

        var json = JsonSerializer.Serialize(body);

        var content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        var response = await _client.PutAsync(uri, content);

        return response.IsSuccessStatusCode;
    }

}
