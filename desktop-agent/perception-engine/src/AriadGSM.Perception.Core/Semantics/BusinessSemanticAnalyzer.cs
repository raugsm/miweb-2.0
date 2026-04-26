using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AriadGSM.Perception.Semantics;

public sealed partial class BusinessSemanticAnalyzer
{
    public IReadOnlyList<BusinessSignal> Analyze(string text)
    {
        var normalized = Normalize(text);
        var signals = new List<BusinessSignal>();
        AddKeywordSignal(signals, normalized, "price_request", ["precio", "costo", "cuanto", "vale", "cotiza", "tarifa", "price", "prices", "cost"], 0.86);
        AddKeywordSignal(signals, normalized, "payment", ["pago", "pague", "pagado", "comprobante", "transferencia", "deposito", "yape", "plin", "nequi", "zelle", "pix", "banco"], 0.9);
        AddKeywordSignal(signals, normalized, "debt", ["deuda", "debe", "saldo", "cuenta", "reembolso", "devolver", "refund", "balance"], 0.88);
        AddKeywordSignal(signals, normalized, "service", ["samsung", "huawei", "xiaomi", "honor", "tecno", "infinix", "iphone", "frp", "mdm", "imei", "liberar", "unlock"], 0.78);
        AddKeywordSignal(signals, normalized, "urgency", ["urgente", "rapido", "asap", "ahora", "ya", "cliente esperando", "fast"], 0.75);
        AddCountrySignals(signals, normalized);
        AddAmountSignals(signals, normalized);
        AddLanguageHint(signals, normalized);
        return signals
            .GroupBy(signal => $"{signal.Kind}:{signal.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(signal => signal.Confidence).First())
            .OrderByDescending(signal => signal.Confidence)
            .ToArray();
    }

    private static void AddKeywordSignal(
        List<BusinessSignal> signals,
        string normalized,
        string kind,
        IReadOnlyList<string> keywords,
        double confidence)
    {
        var matches = keywords.Where(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        signals.Add(new BusinessSignal(kind, matches[0], Math.Clamp(confidence + Math.Min(0.08, matches.Length * 0.02), 0, 0.98)));
    }

    private static void AddCountrySignals(List<BusinessSignal> signals, string normalized)
    {
        var countries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mexico"] = "MX",
            ["mx"] = "MX",
            ["chile"] = "CL",
            ["cl"] = "CL",
            ["colombia"] = "CO",
            ["col"] = "CO",
            ["peru"] = "PE",
            ["pe"] = "PE",
            ["ecuador"] = "EC",
            ["usa"] = "US",
            ["estados unidos"] = "US"
        };

        foreach (var item in countries)
        {
            if (WholeWord(item.Key).IsMatch(normalized))
            {
                signals.Add(new BusinessSignal("country", item.Value, 0.72));
            }
        }
    }

    private static void AddAmountSignals(List<BusinessSignal> signals, string normalized)
    {
        foreach (Match match in AmountRegex().Matches(normalized))
        {
            var amount = match.Groups["amount"].Value;
            var currency = match.Groups["currency"].Success ? match.Groups["currency"].Value.ToUpperInvariant() : "UNKNOWN";
            if (currency == "UNKNOWN")
            {
                continue;
            }
            signals.Add(new BusinessSignal("amount", $"{amount} {currency}".Trim(), 0.82));
        }
    }

    private static void AddLanguageHint(List<BusinessSignal> signals, string normalized)
    {
        if (normalized.Contains("unlock", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("price", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("refund", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("payment", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new BusinessSignal("language_hint", "en", 0.62));
            return;
        }

        if (normalized.Contains("preco", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("pagamento", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(new BusinessSignal("language_hint", "pt", 0.6));
            return;
        }

        signals.Add(new BusinessSignal("language_hint", "es", 0.55));
    }

    private static string Normalize(string value)
    {
        var formD = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return string.Join(" ", builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static Regex WholeWord(string value)
    {
        return new Regex($@"(^|\W){Regex.Escape(value)}($|\W)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    [GeneratedRegex(@"\b(?:(?<currency>usd|usdt|mxn|cop|clp|pen|\$|s/)\s*)?(?<amount>\d+(?:[\.,]\d+)?)(?:\s*(?<currency>usd|usdt|mxn|cop|clp|pen))?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();
}
