namespace BuildOrderProbe.Models;

/// <summary>How many steps of one kind a run made and their summed duration in milliseconds.</summary>
internal sealed record PhaseTotal(int Count, double Ms);
