namespace AriadGSM.Vision.ChangeDetection;

public sealed record ChangedRegion(string RegionId, double Score, int Left, int Top, int Width, int Height);

