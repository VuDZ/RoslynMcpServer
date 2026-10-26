using System.Text.Json.Nodes;

namespace BuildHostFork.Rpc;

/// <summary>Compares every RPC field; an explicit second mode sorts only MSBuild's unordered glob sets.</summary>
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

        var differences = Differences(left, right, "$").ToArray();
        var mode = globSets ? "glob excludes/removes sorted" : "raw ordered arrays";
        Console.WriteLine($"Full RPC comparison ({mode}): {(differences.Length == 0 ? "identical" : "different")}; {differences.Length} differing value(s).");
        foreach (var difference in differences.Take(30))
        {
            Console.WriteLine(difference);
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

    private static IEnumerable<string> Differences(JsonNode? left, JsonNode? right, string path)
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
                    yield return $"{path}.{key}: property added or removed";
                    continue;
                }

                foreach (var difference in Differences(leftObject[key], rightObject[key], $"{path}.{key}"))
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
                foreach (var difference in Differences(leftArray[index], rightArray[index], $"{path}[{index}]"))
                {
                    yield return difference;
                }
            }

            yield break;
        }

        yield return $"{path}: value differs";
    }
}
