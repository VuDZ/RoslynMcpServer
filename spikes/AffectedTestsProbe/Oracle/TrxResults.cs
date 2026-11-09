using System.Xml.Linq;

namespace AffectedTestsProbe;

internal static class TrxResults
{
    public static IReadOnlyList<TrxRow> Read(string text)
    {
        var document = XDocument.Parse(text);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "UnitTestResult")
            .Select(element => new TrxRow(element.Attribute("testId")?.Value ?? "", element.Attribute("outcome")?.Value ?? ""))
            .Where(row => row.TestId.Length > 0)
            .ToArray();
    }
}
