using DoorSim.Models;
using System.Media;
using System.Windows;

namespace DoorSim.Services;

// Centralised sound service for DoorSim.
//
// This keeps all application audio behaviour in one place instead of scattering SoundPlayer logic across views.
//
// Sound categories:
//      - Credential presented: immediate single beep when DoorSim sends a card/PIN.
//      - Access granted: confirmation sound after Softwire grants access.
//      - Access denied: warning sound after Softwire denies access.
//      - Reader alert: used for reader LED changes not caused by a pending credential decision.
//
// Future easter eggs belong here, for example:
//      - Name contains: ?? + Access granted = EasterEgg??_Granted
//      - Name contains: ?? + Access denied  = EasterEgg??_Denied
//
// Audio resource notes:
//      - SoundPlayer requires real WAV files.
//      - Use PCM WAV, ideally 16-bit, 44.1 kHz or 48 kHz.
//      - Do not rename MP3/M4A files to .wav; they will fail to play (as I found out!).
//      - WPF sound resources should use Build Action = Resource.
public class SoundService
{
    /*
      #############################################################################
                                  Resource Paths
      #############################################################################
    */

    // Core sound effect for Readers.
    private const string CredentialBeepPath = "pack://application:,,,/Sounds/Credential_Beep.wav";

    // Easter egg sounds. Add future optional character/theme sounds here as WPF pack resource paths.
    private const string EasterEgg01GrantedPath = "pack://application:,,,/Sounds/EasterEgg01_Granted.wav";
    private const string EasterEgg01DeniedPath = "pack://application:,,,/Sounds/EasterEgg01_Denied.wav";
    private const string EasterEgg02GrantedPath = "pack://application:,,,/Sounds/EasterEgg02_Granted.wav";
    private const string EasterEgg02DeniedPath = "pack://application:,,,/Sounds/EasterEgg02_Denied.wav";


    /*
      #############################################################################
                                  Timing State
      #############################################################################
    */

    // Tracks when the immediate credential-presented beep last started.
    // Decision sounds wait until MinimumDecisionSoundGap has elapsed so access granted/denied audio does not collide with the initial card-presented beep.
    private DateTime _lastCredentialPresentedUtc = DateTime.MinValue;

    // Minimum gap between the initial credential-presented beep and the final access decision sound... (worked OK for me at 900ms, but could be tweaked based on testing/feedback).
    private static readonly TimeSpan MinimumDecisionSoundGap = TimeSpan.FromMilliseconds(900);


    /*
      #############################################################################
                             Public Sound API/Methods
      #############################################################################
    */

    // Plays the immediate credential-presented sound.
    // This should fire as soon as DoorSim sends the card/PIN to Softwire, just like presenting a real credential to a reader.
    public void PlayCredentialPresented()
    {
        _lastCredentialPresentedUtc = DateTime.UtcNow;

        _ = PlayResourceSoundAsync(CredentialBeepPath);
    }

    // Plays the sound for an access granted decision.
    // This happens after Softwire reports that access was granted.
    // The decision sound waits briefly if needed so it does not collide with the initial credential-presented beep.
    // Later, easter egg sounds can be added here.
    public async Task PlayAccessGrantedAsync(Cardholder? cardholder)
    {
        await WaitForDecisionSoundGapAsync();

        if (ShouldPlayEasterEgg01(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(EasterEgg01GrantedPath);

            if (!played)
            {
                // Fallback so a bad/missing easter egg sound does not make the app silent.
                await PlayResourceSoundAsync(CredentialBeepPath);
            }

            return;
        }

        if (ShouldPlayEasterEgg02(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(EasterEgg02GrantedPath);

            if (!played)
            {
                // Fallback so a bad/missing easter egg sound does not make the app silent.
                await PlayResourceSoundAsync(CredentialBeepPath);
            }

            return;
        }

        await PlayResourceSoundAsync(CredentialBeepPath);
    }

    // Plays the sound for an access denied decision.
    // This happens after Softwire reports that access was denied.
    // Later, easter egg sounds can be added here.
    public async Task PlayAccessDeniedAsync(Cardholder? cardholder)
    {
        await WaitForDecisionSoundGapAsync();

        if (ShouldPlayEasterEgg01(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(EasterEgg01DeniedPath);

            if (!played)
            {
                // Fallback so a bad/missing easter egg sound does not make the app silent.
                await PlayTripleBeepAsync();
            }

            return;
        }

        if (ShouldPlayEasterEgg02(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(EasterEgg02DeniedPath);

            if (!played)
            {
                // Fallback so a bad/missing easter egg sound does not make the app silent.
                await PlayTripleBeepAsync();
            }

            return;
        }

        await PlayTripleBeepAsync();
    }

    // Plays a reader alert sound.
    // Used when reader LED changes are not related to a pending/recent access decision, for example door forced or door held open behaviour.
    public void PlayReaderAlert()
    {
        _ = PlayResourceSoundAsync(CredentialBeepPath);
    }


    /*
      #############################################################################
                                  Easter egg rules
      #############################################################################
    */

    // Returns true when a cardholder should use the EasterEgg01_Granted or _Denied sound.
    // Current rule: any cardholder name containing "Simpson", case-insensitive.
    private bool ShouldPlayEasterEgg01(Cardholder? cardholder)
    {
        if (cardholder == null)
            return false;

        if (string.IsNullOrWhiteSpace(cardholder.CardholderName))
            return false;

        return cardholder.CardholderName.Contains("Simpson", StringComparison.OrdinalIgnoreCase);
    }

    // Returns true when a cardholder should use the EasterEgg02_Granted or _Denied sound.
    // Current rule: any cardholder name containing "Pat Sharp", case-insensitive.
    private bool ShouldPlayEasterEgg02(Cardholder? cardholder)
    {
        if (cardholder == null)
            return false;

        if (string.IsNullOrWhiteSpace(cardholder.CardholderName))
            return false;

        return cardholder.CardholderName.Contains("Pat Sharp", StringComparison.OrdinalIgnoreCase);
    }


    /*
      #############################################################################
                                Decision Sound Timing
      #############################################################################
    */

    // Waits until enough time has passed since the initial credential-presented beep before playing the access decision sound.
    //
    // This gives the user a natural sequence:
    //      - card presented beep
    //      - short pause
    //      - granted / denied sound
    private async Task WaitForDecisionSoundGapAsync()
    {
        var elapsed = DateTime.UtcNow - _lastCredentialPresentedUtc;

        if (elapsed >= MinimumDecisionSoundGap)
            return;

        var remainingDelay = MinimumDecisionSoundGap - elapsed;

        await Task.Delay(remainingDelay);
    }


    /*
      #############################################################################
                                  Playback Helpers
      #############################################################################
    */

    // Plays three quick beeps in sequence.
    private async Task PlayTripleBeepAsync()
    {
        await Task.Run(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                PlayResourceSoundSync(CredentialBeepPath);

                Thread.Sleep(100);
            }
        });
    }

    // Plays a WAV resource on a background thread.
    // Uses PlaySync inside Task.Run so the SoundPlayer and stream stay alive for the whole sound. This is more reliable than calling Play() and immediately disposing the player.
    private async Task PlayResourceSoundAsync(string resourcePath)
    {
        await Task.Run(() => PlayResourceSoundSync(resourcePath));
    }

    // Attempts to play a WAV resource and returns whether it actually found/played it.
    // This is useful for easter egg sounds because if the file path/build action/audio format is wrong, we still want DoorSim to fall back to normal audio.
    private async Task<bool> TryPlayResourceSoundAsync(string resourcePath)
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri(resourcePath));

            if (streamInfo == null)
                return false;

            await Task.Run(() =>
            {
                using var player = new SoundPlayer(streamInfo.Stream);
                player.PlaySync();
            });

            return true;
        }
        catch
        {
            return false;
        }
    }

    // Plays a WAV resource synchronously.
    private void PlayResourceSoundSync(string resourcePath)
    {
        try
        {
            var streamInfo = Application.GetResourceStream(new Uri(resourcePath));

            if (streamInfo == null)
                return;

            using var player = new SoundPlayer(streamInfo.Stream);

            player.PlaySync();
        }
        catch
        {
            // Sound is non-critical. If playback fails, ignore it.
        }
    }

}