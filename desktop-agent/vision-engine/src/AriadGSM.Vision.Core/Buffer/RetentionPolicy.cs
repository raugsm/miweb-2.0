namespace AriadGSM.Vision.Buffer;

public sealed record RetentionPolicy(TimeSpan Retention, double MaxStorageGb)
{
    public static RetentionPolicy DefaultLive { get; } = new(TimeSpan.FromHours(1), 40);

    public bool ShouldDelete(DateTimeOffset lastWrite, DateTimeOffset now)
    {
        return now - lastWrite > Retention;
    }

    public long MaxBytes => (long)(MaxStorageGb * 1024 * 1024 * 1024);
}

