using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using DoorSim.Models;
using System.Text.Json;

namespace DoorSim.Services;

// Service responsible for communicating with the Softwire HTTP API.
//
// Handles:
// - Authentication (login)
// - Maintaining session (cookies)
// - Sending HTTP requests to Softwire endpoints
//
// This is the concrete implementation of ISoftwireService,
// used by the ViewModel to interact with Softwire without
// knowing how the HTTP communication works.


// Concrete implementation of ISoftwireService.
// Encapsulates all HTTP logic required to interact with Softwire.
public class SoftwireService : ISoftwireService
{
    // --- Internal HTTP state ---
    // These maintain the connection/session with Softwire.

    // HTTP client used to send requests to Softwire
    private HttpClient? _client;
    // Stores session cookies (used for authentication persistence)
    private CookieContainer? _cookies;

    // Attempts to authenticate with Softwire using provided credentials.
    //
    // Steps:
    // 1. Create HTTP client with cookie support
    // 2. Send login request to /Login endpoint
    // 3. Store session cookies if successful
    //
    // Returns:
    // - true  → login successful
    // - false → login failed
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

    // Retrieves doors from Softwire.
    // First gets the door list, then follows each Href to retrieve full door details.
    public async Task<List<SoftwireDoor>> GetDoorsAsync()
    {
        var doors = new List<SoftwireDoor>();

        if (_client == null)
            return doors;

        var listResponse = await _client.GetAsync("/Doors/");

        if (!listResponse.IsSuccessStatusCode)
            return doors;

        var listJson = await listResponse.Content.ReadAsStringAsync();

        using var listDocument = JsonDocument.Parse(listJson);

        foreach (var item in listDocument.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("Href", out var hrefProperty))
                continue;

            var href = hrefProperty.GetString();

            if (string.IsNullOrWhiteSpace(href))
                continue;

            var doorResponse = await _client.GetAsync(href);

            if (!doorResponse.IsSuccessStatusCode)
                continue;

            var doorJson = await doorResponse.Content.ReadAsStringAsync();

            using var doorDocument = JsonDocument.Parse(doorJson);
            var door = doorDocument.RootElement;

            doors.Add(new SoftwireDoor
            {
                Id = door.TryGetProperty("Id", out var id) ? id.GetString() ?? "" : "",
                Name = door.TryGetProperty("Name", out var name) ? name.GetString() ?? "" : "",
                DoorIsLocked = door.TryGetProperty("IsLocked", out var locked) && locked.GetBoolean()
            });
        }

        return doors.OrderBy(d => d.Name).ToList();
    }
}
