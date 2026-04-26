using System.Text.Json;
using AriadGSM.Vision.Buffer;

namespace AriadGSM.Vision.Config;

public sealed class VisionOptions
{
    public string CaptureMode { get; init; } = "gdi";

    public string StorageRoot { get; init; } = VisionDefaults.StorageRoot;

    public double RetentionHours { get; init; } = 1;

    public double MaxStorageGb { get; init; } = 40;

    public int CaptureIntervalMs { get; init; } = 100;

    public bool RawFramesUploadedToCloud { get; init; } = false;

    public string EventsFile { get; init; } = @"desktop-agent\runtime\vision-events.jsonl";

    public string StateFile { get; init; } = @"desktop-agent\runtime\vision-health.json";

    public static VisionOptions Load(string path)
    {
        if (!File.Exists(path))
        {
            return new VisionOptions();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<VisionOptions>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new VisionOptions();
    }

    public RetentionPolicy ToRetentionPolicy()
    {
        return new RetentionPolicy(TimeSpan.FromHours(RetentionHours), MaxStorageGb);
    }
}
