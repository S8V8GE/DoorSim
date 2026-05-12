using System.Windows.Media;

namespace DoorSim.Models;

// Represents one row in the Auto Mode event log.
//
// Auto Mode adds log entries for high-level simulation events and detailed per-event actions, for example:
//      - simulation started/stopped,
//      - event type selected,
//      - door/cardholder/reader selected,
//      - input activated/released,
//      - action succeeded,
//      - action failed or was skipped.
//
// Some rows can also be visual separators. Separator rows are not real log messages; they exist only to make groups of event activity easier to read.
public class AutoSimulationLogEntry
{
    // True when this row is only used as a visual separator between event groups.
    // Separator rows hide normal time/event text and use a neutral brush.
    public bool IsSeparator { get; set; }

    // Timestamp captured when the log entry is created.
    public DateTime Timestamp { get; set; } = DateTime.Now;

    // User-facing time text. Separator rows intentionally show no time so they do not look like real events.
    public string TimeText => IsSeparator ? "" : Timestamp.ToString("HH:mm:ss");

    // Current event number for event-specific rows. Null is used for general simulation messages such as start/stop/cleanup.
    public int? EventNumber { get; set; }

    // Total number of requested events for the current Auto Mode run. Null is used for general simulation messages.
    public int? TotalEvents { get; set; }

    // User-facing event progress text, for example "12/100". General simulation messages show "-", while separator rows show nothing.
    public string EventText
    {
        get
        {
            if (IsSeparator)
                return "";

            return EventNumber == null || TotalEvents == null
                ? "-"
                : $"{EventNumber}/{TotalEvents}";
        }
    }

    // Log level text shown in the log. Expected values are normally: Info, Success, Warning, Error.
    public string Level { get; set; } = "Info";

    // Event category shown in the log, for example Normal, Forced, Held, or "-".
    public string EventType { get; set; } = "-";

    // Door name associated with the log entry. General messages use "-".
    public string DoorName { get; set; } = "-";

    // A 'nice' and readable log message.
    public string Message { get; set; } = "";

    // Brush used by the Auto Mode view to colour-code log levels.
    // This keeps the XAML simple: the view can bind directly to LevelBrush rather than duplicating colour-selection logic in converters or styles.
    public Brush LevelBrush
    {
        get
        {
            if (IsSeparator)
                return new SolidColorBrush(Color.FromRgb(90, 90, 90));

            return Level switch
            {
                "Success" => new SolidColorBrush(Color.FromRgb(40, 200, 120)),
                "Warning" => new SolidColorBrush(Color.FromRgb(230, 170, 40)),
                "Error" => new SolidColorBrush(Color.FromRgb(220, 80, 80)),
                _ => new SolidColorBrush(Color.FromRgb(200, 200, 200))
            };
        }
    }

}
