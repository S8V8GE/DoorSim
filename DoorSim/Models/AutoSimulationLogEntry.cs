using System.Windows.Media;

namespace DoorSim.Models;

// Represents one line in the Auto Mode event log.
//
// Auto Mode will add one of these each time something happens, for example:
//      - simulation started
//      - event type selected
//      - door selected
//      - cardholder selected
//      - action succeeded
//      - action failed / skipped
public class AutoSimulationLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public string TimeText => Timestamp.ToString("HH:mm:ss");

    public int? EventNumber { get; set; }

    public int? TotalEvents { get; set; }

    public string EventText =>
        EventNumber == null || TotalEvents == null
            ? "-"
            : $"{EventNumber}/{TotalEvents}";

    public string Level { get; set; } = "Info";

    public string EventType { get; set; } = "-";

    public string DoorName { get; set; } = "-";

    public string Message { get; set; } = "";

    public Brush LevelBrush
    {
        get
        {
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
