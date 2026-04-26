using AriadGSM.Vision.Buffer;
using AriadGSM.Vision.Capture;
using AriadGSM.Vision.ChangeDetection;
using AriadGSM.Vision.Config;
using AriadGSM.Vision.Events;
using AriadGSM.Vision.Pipeline;

TestRetentionPolicy();
TestFrameDiffer();
TestVisionEventContract();
await TestSyntheticPipeline();

Console.WriteLine("AriadGSM Vision tests OK");
return 0;

static void TestRetentionPolicy()
{
    var policy = new RetentionPolicy(TimeSpan.FromHours(1), 40);
    var now = DateTimeOffset.UtcNow;
    Assert(policy.ShouldDelete(now.AddHours(-2), now), "old frame should be deleted");
    Assert(!policy.ShouldDelete(now.AddMinutes(-30), now), "fresh frame should stay");
}

static void TestFrameDiffer()
{
    var now = DateTimeOffset.UtcNow;
    var a = new ScreenFrame("a", now, 1, 1, [1, 2, 3], "a", "synthetic");
    var b = new ScreenFrame("b", now, 1, 1, [1, 2, 3], "a", "synthetic");
    var c = new ScreenFrame("c", now, 1, 1, [3, 2, 1], "c", "synthetic");
    var differ = new FrameDiffer();
    Assert(differ.Compare(a, b) == 0, "identical frames should not change");
    Assert(differ.Compare(a, c) > 0, "different frames should change");
}

static void TestVisionEventContract()
{
    var now = DateTimeOffset.UtcNow;
    var frame = new ScreenFrame("sample", now, 1, 1, [1], "hash", "synthetic");
    var saved = new SavedFrame("sample", @"D:\AriadGSM\vision-buffer\sample.bin", now, "hash", true);
    var visionEvent = VisionEventFactory.Create(frame, saved, new VisionOptions());
    var errors = ContractValidator.Validate(visionEvent);
    Assert(errors.Count == 0, string.Join("; ", errors));
}

static async Task TestSyntheticPipeline()
{
    var root = Path.Combine(Path.GetTempPath(), "ariadgsm-vision-test-" + Guid.NewGuid().ToString("N"));
    var options = new VisionOptions
    {
        CaptureMode = "synthetic",
        StorageRoot = root,
        EventsFile = Path.Combine(root, "events.jsonl"),
        StateFile = Path.Combine(root, "health.json"),
        RetentionHours = 1,
        MaxStorageGb = 1,
        MaxCycles = 2,
        CaptureIntervalMs = 20,
        MinEventIntervalMs = 0
    };
    try
    {
        var pipeline = new VisionPipeline(options);
        var summary = await pipeline.RunContinuousAsync(maxCycles: 2);
        Assert(summary.FramesCaptured == 2, "continuous pipeline should capture two frames");
        Assert(summary.EventsWritten >= 1, "continuous pipeline should write at least one event");
        Assert(File.Exists(options.EventsFile), "events file should exist");
        Assert(File.Exists(options.StateFile), "state file should exist");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
