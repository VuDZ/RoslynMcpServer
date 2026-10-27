using System.Text.Json.Nodes;

namespace BuildHostFork.Rpc;

/// <summary>
/// Compares every RPC field; an explicit second mode sorts only MSBuild's unordered glob sets and, on Windows, accepts
/// paths that differ only in letter case.
/// </summary>
/// <remarks>
/// Windows paths are case-insensitive, and upstream returns whichever spelling of a path it loaded first: a project
/// folder spelled one way in the solution and another way in a ProjectReference reaches the response in either
/// spelling, depending on load order. References-first and evaluation ahead change that order, so on the office
/// solution two projects came back in the ProjectReference spelling (owner decision 2026-10-27: not a difference on
/// Windows). Such values are counted and listed apart; every other string, and every path on Unix, stays exact.
/// </remarks>
internal static class ProjectFileInfoComparison
{
    public static int Run(string leftPath, string rightPath, bool globSets = false)
    {
        var left = Read(leftPath);
        var right = Read(rightPath);
        if (globSets)
        {
            SortGlobSets(left);
            SortGlobSets(right);
        }

        var ignorePathCase = globSets && OperatingSystem.IsWindows();
        var all = Differences(left, right, "$", ignorePathCase).ToArray();
        var differences = all.Where(difference => !difference.PathCaseOnly).ToArray();
        var pathCase = all.Where(difference => difference.PathCaseOnly).ToArray();
        var mode = !globSets ? "raw ordered arrays"
            : ignorePathCase ? "glob excludes/removes sorted, path letter case ignored" : "glob excludes/removes sorted";
        Console.WriteLine($"Full RPC comparison ({mode}): {(differences.Length == 0 ? "identical" : "different")}; {differences.Length} differing value(s).");
        foreach (var difference in differences.Take(30))
        {
            Console.WriteLine(difference);
        }

        if (ignorePathCase)
        {
            Console.WriteLine($"Path values that differ only in letter case: {pathCase.Length}.");
            foreach (var difference in pathCase.Take(30))
            {
                Console.WriteLine(difference);
            }
        }

        return differences.Length == 0 ? 0 : 1;
    }

    public static int SelfCheck()
    {
        var info = JsonNode.Parse("""
            {"isEmpty":false,"language":"C#","filePath":"P.csproj","outputFilePath":"P.dll",
             "outputRefFilePath":"ref/P.dll","intermediateOutputFilePath":"obj/P.dll",
             "generatedFilesOutputDirectory":"generated","defaultNamespace":"P","targetFramework":"net10.0",
             "targetFrameworkIdentifier":".NETCoreApp","targetFrameworkVersion":"v10.0",
             "commandLineArgs":["/out:P.dll"],"documents":[{"filePath":"P.cs","logicalPath":"Link/P.cs","folders":["Link"]}],
             "additionalDocuments":[{"filePath":"extra.txt"}],"analyzerConfigDocuments":[{"filePath":".editorconfig"}],
             "projectReferences":[{"path":"Q.csproj","aliases":[],"referenceOutputAssembly":true}],
             "projectCapabilities":["CSharp"],"contentFilePaths":["content.txt"],"projectAssetsFilePath":"project.assets.json",
             "packageReferences":[{"name":"Package","version":"1.0"}],"metadataReferences":[{"path":"R.dll"}],
             "codePage":65001,"checksumAlgorithm":"SHA256",
             "fileGlobs":[{"includes":["**/*.cs"],"excludes":["obj/**","bin/**"],"removes":["Old.cs"]}]}
            """)!.AsObject();
        var checks = 0;
        foreach (var field in info.Select(property => property.Key))
        {
            var changed = info.DeepClone().AsObject();
            changed[field] = null;
            Expect(Differences(info, changed, "$").Any(), $"changed {field}");
        }

        var reordered = info.DeepClone();
        reordered["fileGlobs"]![0]!["excludes"] = new JsonArray("bin/**", "obj/**");
        Expect(Differences(info, reordered, "$").Any(), "glob array order");
        var folder = info.DeepClone();
        folder["documents"]![0]!["folders"]![0] = "Other";
        Expect(Differences(info, folder, "$").Any(), "nested document folder");
        Expect(Differences(JsonNode.Parse("{\"x\":null}"), JsonNode.Parse("{}"), "$").Any(), "missing versus null");
        Expect(Differences(JsonNode.Parse("{\"x\":null}"), JsonNode.Parse("{\"x\":\"\"}"), "$").Any(), "null versus empty");
        Expect(!Differences(info, info.DeepClone(), "$").Any(), "identical clone");
        var setsLeft = JsonNode.Parse("""{"Projects":{"P":{"Infos":[]}}}""")!.AsObject();
        setsLeft["Projects"]!["P"]!["Infos"]!.AsArray().Add(info.DeepClone());
        var setsRight = setsLeft.DeepClone().AsObject();
        setsRight["Projects"]!["P"]!["Infos"]![0]!["fileGlobs"]![0]!["excludes"] = new JsonArray("bin/**", "obj/**");
        SortGlobSets(setsLeft);
        SortGlobSets(setsRight);
        Expect(!Differences(setsLeft, setsRight, "$").Any(), "glob sets keep the same patterns");
        setsRight["Projects"]!["P"]!["Infos"]![0]!["fileGlobs"]![0]!["excludes"]![0] = "changed/**";
        SortGlobSets(setsRight);
        Expect(Differences(setsLeft, setsRight, "$").Any(), "changed glob pattern is rejected");
        CheckPathCase(Expect);
        Console.WriteLine($"RPC comparison self-checks passed: {checks}.");
        return 0;

        void Expect(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"RPC comparison self-check failed: {name}.");
            }

            checks++;
        }
    }

    private static void CheckPathCase(Action<bool, string> expect)
    {
        var path = Path.Combine(Path.GetTempPath(), "Folder", "P.csproj");
        var respelled = Path.Combine(Path.GetTempPath(), "FOLDER", "P.csproj");
        expect(IsPathCaseOnly(path, respelled), "path in another letter case");
        expect(IsPathCaseOnly("/reference:" + path, "/reference:" + respelled), "compiler option path in another letter case");
        expect(!IsPathCaseOnly("Namespace", "NAMESPACE"), "a name that is not a path stays exact");
        expect(!IsPathCaseOnly("/define:Trace", "/define:TRACE"), "an option that is not a path stays exact");
        expect(!IsPathCaseOnly(path, respelled + "x"), "another path is still a difference");
        var left = JsonNode.Parse($$"""{"filePath":{{JsonValue.Create(path).ToJsonString()}},"defaultNamespace":"P"}""");
        var right = JsonNode.Parse($$"""{"filePath":{{JsonValue.Create(respelled).ToJsonString()}},"defaultNamespace":"p"}""");
        var differences = Differences(left, right, "$", ignorePathCase: true).ToArray();
        expect(differences.Count(difference => difference.PathCaseOnly) == 1, "path case is counted apart");
        expect(differences.Count(difference => !difference.PathCaseOnly) == 1, "name case is still a difference");
        expect(Differences(left, right, "$").All(difference => !difference.PathCaseOnly), "exact mode keeps path case");
    }

    private static JsonObject Read(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        var projects = root["Projects"]!.AsArray();
        if (projects.Count == 0)
        {
            throw new InvalidDataException("An empty RPC capture cannot pass comparison.");
        }

        var byPath = new JsonObject();
        foreach (var project in projects)
        {
            var infos = project!["Infos"]!.AsArray();
            if (infos.Count == 0 || project["Diagnostics"] is not JsonArray)
            {
                throw new InvalidDataException("Incomplete RPC project capture.");
            }

            byPath.Add(project["Path"]!.GetValue<string>(), new JsonObject
            {
                ["Infos"] = infos.DeepClone(),
                ["Diagnostics"] = project["Diagnostics"]!.DeepClone(),
            });
        }

        return new JsonObject { ["MsBuild"] = root["MsBuild"]!.DeepClone(), ["Projects"] = byPath };
    }

    private static void SortGlobSets(JsonObject root)
    {
        foreach (var project in root["Projects"]!.AsObject())
        {
            foreach (var info in project.Value!["Infos"]!.AsArray())
            {
                foreach (var glob in info!["fileGlobs"]!.AsArray())
                {
                    foreach (var field in new[] { "excludes", "removes" })
                    {
                        // Keep duplicates and case. Includes, glob results and every other array retain their order.
                        var patterns = glob![field]!.AsArray().Select(pattern => pattern!.GetValue<string>())
                            .Order(StringComparer.Ordinal).Select(pattern => JsonValue.Create(pattern)).ToArray();
                        glob[field] = new JsonArray(patterns);
                    }
                }
            }
        }
    }

    private static IEnumerable<Difference> Differences(JsonNode? left, JsonNode? right, string path, bool ignorePathCase = false)
    {
        if (JsonNode.DeepEquals(left, right))
        {
            yield break;
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            foreach (var key in leftObject.Select(property => property.Key).Union(rightObject.Select(property => property.Key)))
            {
                if (leftObject.ContainsKey(key) != rightObject.ContainsKey(key))
                {
                    yield return new Difference($"{path}.{key}", "property added or removed");
                    continue;
                }

                foreach (var difference in Differences(leftObject[key], rightObject[key], $"{path}.{key}", ignorePathCase))
                {
                    yield return difference;
                }
            }

            yield break;
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray && leftArray.Count == rightArray.Count)
        {
            for (var index = 0; index < leftArray.Count; index++)
            {
                foreach (var difference in Differences(leftArray[index], rightArray[index], $"{path}[{index}]", ignorePathCase))
                {
                    yield return difference;
                }
            }

            yield break;
        }

        if (ignorePathCase
            && left is JsonValue leftValue && leftValue.TryGetValue<string>(out var leftText)
            && right is JsonValue rightValue && rightValue.TryGetValue<string>(out var rightText)
            && IsPathCaseOnly(leftText, rightText))
        {
            yield return new Difference(path, "path letter case only", PathCaseOnly: true);
            yield break;
        }

        yield return new Difference(path, "value differs");
    }

    /// <summary>
    /// Whether two strings are the same full path, or the same compiler option with a full path value
    /// (<c>/reference:C:\...</c>), apart from letter case.
    /// </summary>
    private static bool IsPathCaseOnly(string left, string right)
    {
        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // An option is matched first, by a name without directory separators: on Unix "/define:TRACE" would itself
        // pass for a full path.
        var separator = left.IndexOf(':');
        if (left.Length > 0 && left[0] is '/' or '-' && separator > 1 && left.AsSpan(1, separator - 1).IndexOfAny('/', '\\') < 0)
        {
            return Path.IsPathFullyQualified(left[(separator + 1)..])
                && string.Equals(left[..separator], right[..separator], StringComparison.Ordinal);
        }

        return Path.IsPathFullyQualified(left);
    }

    private readonly record struct Difference(string Location, string Description, bool PathCaseOnly = false)
    {
        public override string ToString() => $"{Location}: {Description}";
    }
}
