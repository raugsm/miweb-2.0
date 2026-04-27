using System.Text;

namespace AriadGSM.Perception.Text;

public static class TextRepair
{
    private static readonly string[] MojibakeMarkers =
    [
        "\u00C3",
        "\u00C2",
        "\u00E2",
        "\u00F0\u0178"
    ];

    public static string Repair(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !LooksLikeMojibake(value))
        {
            return value ?? string.Empty;
        }

        try
        {
            var candidate = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            return Score(candidate) <= Score(value) ? candidate : value;
        }
        catch
        {
            return value;
        }
    }

    private static bool LooksLikeMojibake(string value)
    {
        return MojibakeMarkers.Any(value.Contains);
    }

    private static int Score(string value)
    {
        var score = 0;
        foreach (var marker in MojibakeMarkers)
        {
            score += value.Split(marker, StringSplitOptions.None).Length - 1;
        }

        score += value.Count(character => character == '\uFFFD');
        return score;
    }
}
