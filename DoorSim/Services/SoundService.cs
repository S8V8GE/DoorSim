using DoorSim.Models;
using System.Media;
using System.Windows;

namespace DoorSim.Services;

// Centralised sound service for DoorSim.
//
// This keeps all application audio behaviour in one place instead of scattering
// SoundPlayer logic across views.
//
// Sound categories:
// - Credential presented: immediate single beep when DoorSim sends a card/PIN.
// - Access granted: confirmation sound after Softwire grants access.
// - Access denied: warning sound after Softwire denies access.
// - Reader alert: used for reader LED changes not caused by a pending credential decision.
//
// Future easter eggs belong here, for example:
// - Simpson + granted = "Woo Hoo"
// - Simpson + denied  = "D'oh"
public class SoundService
{
    /*
      #############################################################################
                                  Sound paths
      #############################################################################
    */

    private const string CredentialBeepPath = "pack://application:,,,/Sounds/Credential_Beep.wav";

    // Easter Egg sounds (future ones added here):
    private const string SimpsonGrantedPath = "pack://application:,,,/Sounds/Simpsons_Granted.wav";
    private const string SimpsonDeniedPath = "pack://application:,,,/Sounds/Simpsons_Denied.wav";

    // Tracks when the immediate credential-presented beep last started.
    //
    // Access granted / denied sounds use this so they do not fire too close to the initial card-presented beep.
    private DateTime _lastCredentialPresentedUtc = DateTime.MinValue;

    // Minimum gap between the initial card-presented beep and the final decision sound. Adjust this later if needed... (worked OK for me at 900ms, but could be tweaked based on testing/feedback).
    private static readonly TimeSpan MinimumDecisionSoundGap = TimeSpan.FromMilliseconds(900);


    /*
      #############################################################################
                                  Public sound methods
      #############################################################################
    */

    // Plays the immediate credential-presented sound.
    //
    // This should fire as soon as DoorSim sends the card/PIN to Softwire,
    // just like presenting a real credential to a reader.
    public void PlayCredentialPresented()
    {
        _lastCredentialPresentedUtc = DateTime.UtcNow;

        _ = PlayResourceSoundAsync(CredentialBeepPath);
    }

    // Plays the sound for an access granted decision.
    //
    // This happens shortly after Softwire reports that access was granted.
    // The small delay makes the sequence feel like:
    // credential presented beep -> reader decision beep.
    public async void PlayAccessGranted(Cardholder? cardholder)
    {
        await WaitForDecisionSoundGapAsync();

        if (ShouldPlaySimpsonEasterEgg(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(SimpsonGrantedPath);

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
    //
    // This happens after Softwire reports that access was denied.
    // Later, easter egg sounds such as "D'oh" can be added here.
    public async Task PlayAccessDeniedAsync(Cardholder? cardholder)
    {
        await WaitForDecisionSoundGapAsync();

        if (ShouldPlaySimpsonEasterEgg(cardholder))
        {
            var played = await TryPlayResourceSoundAsync(SimpsonDeniedPath);

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
    //
    // This is for reader LED changes that are NOT part of a pending access
    // decision, such as door forced / door held open style behaviour.
    public void PlayReaderAlert()
    {
        _ = PlayResourceSoundAsync(CredentialBeepPath);
    }


    /*
      #############################################################################
                                  Easter egg rules
      #############################################################################
    */

    // Detects whether the cardholder should trigger the future Simpsons easter egg.
    private bool ShouldPlaySimpsonEasterEgg(Cardholder? cardholder)
    {
        if (cardholder == null)
            return false;

        if (string.IsNullOrWhiteSpace(cardholder.CardholderName))
            return false;

        return cardholder.CardholderName.Contains("Simpson", StringComparison.OrdinalIgnoreCase);
    }


    /*
      #############################################################################
                                  Low-level playback
      #############################################################################
    */

    // Attempts to play a WAV resource and returns whether it actually found/played it.
    //
    // This is useful for easter egg sounds because if the file path/build action/audio
    // format is wrong, we still want DoorSim to fall back to normal audio.
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

    // Waits until enough time has passed since the initial credential-presented beep before playing the access decision sound.
    //
    // This gives the user a natural sequence:
    // - card presented beep
    // - short pause
    // - granted / denied sound
    private async Task WaitForDecisionSoundGapAsync()
    {
        var elapsed = DateTime.UtcNow - _lastCredentialPresentedUtc;

        if (elapsed >= MinimumDecisionSoundGap)
            return;

        var remainingDelay = MinimumDecisionSoundGap - elapsed;

        await Task.Delay(remainingDelay);
    }

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
    //
    // Uses PlaySync inside Task.Run so the SoundPlayer and stream stay alive
    // for the whole sound. This is more reliable than calling Play() and
    // immediately disposing the player.
    private async Task PlayResourceSoundAsync(string resourcePath)
    {
        await Task.Run(() => PlayResourceSoundSync(resourcePath));
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