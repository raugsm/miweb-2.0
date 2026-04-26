using AriadGSM.Vision.Capture;

namespace AriadGSM.Vision.ChangeDetection;

public sealed class FrameDiffer
{
    public double Compare(ScreenFrame? previous, ScreenFrame current)
    {
        if (previous is null)
        {
            return 1.0;
        }

        if (previous.Hash == current.Hash)
        {
            return 0.0;
        }

        if (previous.Data.Length == 0 || current.Data.Length == 0)
        {
            return 1.0;
        }

        var max = Math.Max(previous.Data.Length, current.Data.Length);
        var min = Math.Min(previous.Data.Length, current.Data.Length);
        var changed = Math.Abs(previous.Data.Length - current.Data.Length);
        for (var index = 0; index < min; index++)
        {
            if (previous.Data[index] != current.Data[index])
            {
                changed++;
            }
        }
        return Math.Clamp((double)changed / max, 0.0, 1.0);
    }
}

