using RoslynMcpServer.Tests.Support;
using Xunit;

namespace RoslynMcpServer.Tests.WorkspaceLoadCache;

/// <summary>
/// Pins the tests that the main suite does not run because they need <see cref="PinnedSdk"/>. Moving a failing
/// test into an excluded category is cheaper than fixing it, so every entry is listed here: a test that builds
/// with the pinned SDK belongs here, any other test does not.
/// </summary>
public sealed class PinnedSdkTestInventoryTests
{
    [Fact]
    public void Pinned_sdk_category_matches_the_reviewed_list()
    {
        Assert.Equal(
            _expected.Order(StringComparer.Ordinal).ToArray(),
            TestCategoryInventory.Names(TestCategories.RequiresPinnedSdk));
    }

    private static readonly string[] _expected =
    [
        // Real builds of fixtures pinned to the SDK; the observations are compared with recorded evidence.
        "DependencyEvidenceRunnerTests.Real_build_and_dtb_observations_do_not_authorize_unproven_closure",
        "DependencyEvidenceRunnerTests.Pinned_sdk_and_pack_reresolution_reads_current_installation_without_loading_analyzers",
        "DependencyRazorWitnessTests.Scoped_css_changes_fresh_Razor_output_without_changing_selected_source_regions",
        "HydrateHostExperimentTests.Ordinary_fixture_build_uses_its_sdk_pin_despite_inherited_msbuild_overrides",
        "HydrateHostExperimentTests.Sdk_glob_edit_add_rename_remove_build_and_keep_project_bytes",
        "HydrateHostExperimentTests.Linked_edit_preserves_exact_encoding_bytes_and_every_membership",
        "HydrateHostExperimentTests.Reconciliation_updates_all_roles_from_disk_without_persistence_or_dtb",
        "HydrateHostExperimentTests.Ordinary_open_negative_witness_observes_open_and_raw_logger_artifact",
        // Also skips itself without the SDK or ASP.NET Core; a skip would fail CI, the category keeps it out.
        "OracleFixtureBuilderTests.Fresh_msbuild_reproduces_the_observed_razor_generator_output",
    ];
}
