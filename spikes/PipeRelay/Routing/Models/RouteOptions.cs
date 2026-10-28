namespace PipeRelay.Routing.Models;

internal sealed record RouteOptions(string Root, string Descriptor, string Context = "default",
    int LoadMilliseconds = 150, bool FailLoad = false, bool CrashBeforeReady = false,
    string? BeforeReadyBarrier = null, string? LoadBarrier = null, string? LaunchBarrier = null,
    string? BeforeLeaseBarrier = null, string? BeforeClosingBarrier = null, string? ExitBarrier = null);
