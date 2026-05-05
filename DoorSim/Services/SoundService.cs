using DoorSim.Models;
using System.Media;
using System.Windows;

namespace DoorSim.Services;

// Centralised sound service for DoorSim.
//
// This keeps all application audio behaviour in one place instead of scattering
// SoundPlayer logic across views.
//
// Important design rule:
// - Credential-presented sounds are played at the moment DoorSim sends the
//   card/PIN to Softwire.
// - Reader LED polling does not trigger audio, because polling may happen later
//   and creates delayed/duplicate beeps.
// - Access denied feedback may play a warning pattern.
public class SoundService
{
    /*
      #############################################################################
                                  Sound paths
      #############################################################################
    */

    private const string CredentialBeepPath = "pack://application:,,,/Sounds/Credential_Beep.wav";


    /*
      #############################################################################
                                  Public sound methods
      #############################################################################
    */

    // Plays the standard credential-presented sound immediately.
    //
    // Fire-and-forget is intentional here because UI actions should not wait for
    // the WAV file to finish playing.
    public void PlayCredentialPresented()
    {
        _ = PlayResourceSoundAsync(CredentialBeepPath);
    }

    // Plays the standard access denied warning sound.
    //
    // For now this uses the same beep three times.
    // Later we can replace this with a dedicated denied WAV file.
    public async Task PlayAccessDeniedAsync()
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

    // Plays the appropriate sound when a cardholder is presented to a reader.
    //
    // Future easter eggs belong here.
    public void PlayCardholderPresented(Cardholder cardholder)
    {
        if (ShouldPlaySimpsonEasterEgg(cardholder))
        {
            // Future:
            // PlayResourceSoundAsync("pack://application:,,,/Sounds/Doh.wav");
            //
            // For now, fall back to the normal credential beep.
            PlayCredentialPresented();
            return;
        }

        PlayCredentialPresented();
    }


    /*
      #############################################################################
                                  Easter egg rules
      #############################################################################
    */

    // Detects whether the cardholder should trigger the future Simpsons easter egg.
    private bool ShouldPlaySimpsonEasterEgg(Cardholder cardholder)
    {
        if (string.IsNullOrWhiteSpace(cardholder.CardholderName))
            return false;

        return cardholder.CardholderName.Contains(
            "Simpson",
            StringComparison.OrdinalIgnoreCase);
    }


    /*
      #############################################################################
                                  Low-level playback
      #############################################################################
    */

    // Plays a WAV resource on a background thread.
    //
    // Uses PlaySync inside Task.Run so the SoundPlayer and stream stay alive for
    // the whole sound. This is more reliable than calling Play() and immediately
    // disposing the player.
    private async Task PlayResourceSoundAsync(string resourcePath)
    {
        await Task.Run(() => PlayResourceSoundSync(resourcePath));
    }

    // Plays a WAV resource synchronously.
    //
    // This method is intentionally private. Public methods decide whether to run
    // it on a background thread or as part of a sequence.
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