$ErrorActionPreference = 'Stop'
$fixtureRoot = $PSScriptRoot
$repositoryRoot = Split-Path (Split-Path $fixtureRoot -Parent) -Parent
$evidenceRoot = Join-Path $repositoryRoot 'docs/rapid-tia/epoch-1-project-level/evidence/task-06-adapter-names'
$adapters = @(
    @{ Project='XunitV2'; Id='xunit-v2'; Namespace='AdapterNames.Xunit'; Generic='ClosedGenericTests.Generic' },
    @{ Project='XunitV3'; Id='xunit-v3'; Namespace='AdapterNames.Xunit'; Generic='ClosedGenericTests.Generic' },
    @{ Project='NUnit'; Id='nunit'; Namespace='AdapterNames.NUnit'; Generic='GenericTests<Int32>.Generic' },
    @{ Project='MSTest'; Id='mstest'; Namespace='AdapterNames.MSTest'; Generic='ClosedGenericTests.Generic' }
)
$catalog = foreach ($adapter in $adapters) {
    $id = $adapter.Id
    $ns = $adapter.Namespace
    $diagnosticPath = Join-Path $fixtureRoot "artifacts/$id.discovery.log"
    $lines = @(Get-Content -LiteralPath $diagnosticPath)
    # VSTest appends when the diagnostic file already exists; use the final complete request.
    $rawMessage = $lines | Where-Object {
        $_.Contains('TestRequestSender.OnDiscoveryMessageReceived: Received message: ') -and
        $_.Contains('"MessageType":"TestDiscovery.Completed"')
    } | Select-Object -Last 1
    if (-not $rawMessage) { throw "Missing complete discovery message for $id." }
    $message = $rawMessage.Substring($rawMessage.IndexOf('{')) | ConvertFrom-Json
    $cases = @($message.Payload.LastDiscoveredTests)
    if ($message.Payload.IsAborted -or $message.Payload.TotalTests -ne 5 -or $cases.Count -ne 5) {
        throw "Incomplete discovery for $id."
    }
    $discoveryHash = (Get-FileHash -LiteralPath $diagnosticPath -Algorithm SHA256).Hash
    @(
        "Original diagnostic: spikes/AdapterTestNames/artifacts/$id.discovery.log"
        "Original file SHA256: $discoveryHash"
        'Original startup lines and complete discovery message (unchanged):'
        $lines[0]
        $lines[1]
        $rawMessage
    ) | Set-Content -LiteralPath (Join-Path $evidenceRoot "$id.raw-discovery.txt") -Encoding utf8

    $needles = [ordered]@{
        'nested-source'="$ns.Outer.Inner.Nested"
        'nested'="$ns.Outer+Inner.Nested"
        'generic-source'="$ns.GenericTests<T>.Generic"
        'generic'="$ns.$($adapter.Generic)"
        'inherited-owner'="$ns.InheritedBase.Inherited"
        'inherited'="$ns.InheritedTests.Inherited"
        'parameters'="$ns.ParameterTests.Rows"
    }
    $trxExcerpts = [System.Collections.Generic.List[string]]::new()
    $filterResults = foreach ($probe in $needles.Keys) {
        $trxPath = Join-Path $fixtureRoot "artifacts/$id/$probe.trx"
        [xml]$trx = Get-Content -Raw -LiteralPath $trxPath
        $counters = $trx.TestRun.ResultSummary.Counters
        $expected = if ($probe -in @('nested-source', 'generic-source', 'inherited-owner')) { 0 } elseif ($probe -eq 'parameters') { 2 } else { 1 }
        $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
        if ([int]$counters.total -ne $expected -or [int]$counters.passed -ne $expected -or
            [int]$counters.executed -ne $expected -or [int]$counters.failed -ne 0 -or $results.Count -ne $expected) {
            throw "Unexpected counters for $id/$probe."
        }
        $identities = foreach ($result in $results) {
            $discovered = @($cases | Where-Object Id -EQ $result.testId)
            if ($discovered.Count -ne 1 -or $result.outcome -ne 'Passed' -or
                -not $discovered[0].FullyQualifiedName.Contains($needles[$probe], [StringComparison]::OrdinalIgnoreCase)) {
                throw "Execution/discovery identity mismatch for $id/$probe."
            }
            [ordered]@{
                TestCaseId=$result.testId
                DisplayName=$result.testName
                Outcome=$result.outcome
                DiscoveredFullyQualifiedName=$discovered[0].FullyQualifiedName
            }
        }
        $trxHash = (Get-FileHash -LiteralPath $trxPath -Algorithm SHA256).Hash
        $trxExcerpts.Add("Probe: $probe; filter: FullyQualifiedName~$($needles[$probe])")
        $trxExcerpts.Add("Original TRX: spikes/AdapterTestNames/artifacts/$id/$probe.trx")
        $trxExcerpts.Add("Original TRX SHA256: $trxHash")
        foreach ($line in Get-Content -LiteralPath $trxPath) {
            if ($line.TrimStart().StartsWith('<UnitTestResult ') -or $line.TrimStart().StartsWith('<TestMethod ') -or
                $line.TrimStart().StartsWith('<Counters ') -or $line.TrimStart().StartsWith('<ResultSummary ')) {
                $trxExcerpts.Add($line)
            }
        }
        $trxExcerpts.Add('')
        [ordered]@{
            Probe=$probe
            Filter="FullyQualifiedName~$($needles[$probe])"
            ExpectedTotal=$expected
            Total=[int]$counters.total
            Executed=[int]$counters.executed
            Passed=[int]$counters.passed
            Failed=[int]$counters.failed
            Interpretation=$(if ($expected -eq 0) { 'zero-match; negative probe, not a test pass' } else { 'positive selection; all selected cases passed' })
            Results=@($identities)
        }
    }
    $trxExcerpts | Set-Content -LiteralPath (Join-Path $evidenceRoot "$id.trx-excerpts.txt") -Encoding utf8
    [xml]$project = Get-Content -Raw -LiteralPath (Join-Path $fixtureRoot "$($adapter.Project)/$($adapter.Project).csproj")
    $assets = Get-Content -Raw -LiteralPath (Join-Path $fixtureRoot "$($adapter.Project)/obj/project.assets.json") | ConvertFrom-Json
    $resolvedPackages = @($assets.libraries.PSObject.Properties.Name | Sort-Object)
    if ($id -eq 'xunit-v3' -and @($resolvedPackages | Where-Object { $_ -like 'Microsoft.Testing.Platform*' }).Count -ne 0) {
        throw 'The xUnit v3 probe unexpectedly includes MTP.'
    }
    [ordered]@{
        Adapter=$id
        Project="spikes/AdapterTestNames/$($adapter.Project)/$($adapter.Project).csproj"
        PackageReferences=@($project.Project.ItemGroup.PackageReference | ForEach-Object { "$($_.Include)/$($_.Version)" })
        ResolvedPackages=$resolvedPackages
        DiscoveryOriginalSha256=$discoveryHash
        RawDiscovery="$id.raw-discovery.txt"
        DiscoveryTotal=[int]$message.Payload.TotalTests
        Cases=@($cases | Select-Object Id,FullyQualifiedName,DisplayName,ExecutorUri)
        Filters=@($filterResults)
    }
}
[ordered]@{
    Captured='2026-11-05'
    Baseline='422e90b'
    RuntimeModelId='unknown'
    Sdk='10.0.402'
    Runtime='10.0.13'
    VstestConsole='18.7.0-release-26505-111'
    Configuration='Release'
    Adapters=@($catalog)
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'catalog.json') -Encoding utf8
'Verified: four complete discoveries, 20 cases, 28 TRX runs, 20 passed cases in 16 positive runs, 12 expected zero-match probes.'
