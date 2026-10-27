namespace BuildOrderProbe.Models;

/// <summary>Which evaluation context a <see cref="SdkCacheProbe"/> run gives the host's evaluations.</summary>
internal enum SdkCacheMode
{
    /// <summary>None: every evaluation gets a context of its own, as in the 5.9.0 BuildHost.</summary>
    Isolated,

    /// <summary>One SharedSDKCache context per project, for its root, target framework and globs evaluations.</summary>
    Project,

    /// <summary>One SharedSDKCache context for every evaluation of the run.</summary>
    Solution,
}
