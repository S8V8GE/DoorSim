using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace DoorSim.Services;

public class SoftwireService : ISoftwireService
{
    private HttpClient? _client;
    private CookieContainer? _cookies;

    // Function for logging onto Softwire. Returns true if successful, false otherwise.
    public async Task<bool> LoginAsync(string hostname, string username, string password)
    {
        _cookies = new CookieContainer();

        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://{hostname}")
        };

        _client.DefaultRequestHeaders.Accept.Clear();
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var loginBody = new StringContent(
            $"username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&Login=Login",
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        var response = await _client.PostAsync("/Login", loginBody);

        return response.IsSuccessStatusCode;
    }
}
