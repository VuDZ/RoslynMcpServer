namespace RoslynMcpServer.Services.Models;

/// <summary>Stable reasons stored on an input map. Whole-graph coverage stays unknown while any of these apply.</summary>
internal static class InputCoverageReason
{
    public const string LoadIncomplete = "load-incomplete";

    public const string UnconfirmedTargetFramework = "unconfirmed-target-framework";

    public const string MetadataOnlyDependency = "metadata-only-dependency";

    public const string ImportsNotInEvaluatedGraph = "imports-not-in-evaluated-graph";

    public const string RestoreInputsNotInEvaluatedGraph = "restore-inputs-not-in-evaluated-graph";

    public const string CustomTasksNotInEvaluatedGraph = "custom-tasks-not-in-evaluated-graph";

    public const string ExternalGlobCompletenessUnknown = "external-glob-completeness-unknown";

    public const string NonDocumentXamlAndResourcesUnknown = "xaml-and-resources-beyond-evaluated-documents-unknown";

    public const string WalkUpImportNotProven = "walk-up-file-is-a-candidate-not-a-proven-import";

    public const string WatcherDirectoryMissing = "watcher-directory-missing";

    public const string WatcherStartFailed = "watcher-start-failed";

    public const string WatcherError = "watcher-error";

    public const string WatcherBufferOverflow = "watcher-buffer-overflow";

    public const string DirectoryRenameInsideMembership = "directory-rename-inside-membership-region";

    public const string InputInaccessible = "input-inaccessible";

    public const string InputReadUnstable = "input-read-unstable";

    public const string InputMissing = "input-missing";

    public const string UnknownRoleOrProducer = "unknown-role-or-producer";

    public const string OwnersNotAttributed = "owners-not-attributed";
}
