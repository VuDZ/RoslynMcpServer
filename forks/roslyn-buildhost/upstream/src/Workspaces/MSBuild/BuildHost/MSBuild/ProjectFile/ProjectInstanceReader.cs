// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Roslyn.Utilities;
using MSB = Microsoft.Build;

namespace Microsoft.CodeAnalysis.MSBuild;

internal readonly struct ProjectInstanceReader
{
    private readonly ProjectCommandLineProvider? _commandLineProvider;
    public readonly MSB.Evaluation.Project? Project;
    public readonly MSB.Execution.ProjectInstance _projectInstance;

    private readonly string _projectFullPath;
    private readonly string _projectDirectory;

    public string Language { get; }

    public ProjectInstanceReader(
        string language,
        ProjectCommandLineProvider? commandLineReader,
        MSB.Execution.ProjectInstance projectInstance,
        MSB.Evaluation.Project? project)
    {
        Language = language;
        _commandLineProvider = commandLineReader;
        _projectInstance = projectInstance;
        Project = project;

        // The ProjectInstance we get from BuildResult.ProjectStateAfterBuild returns a limited ProjectInstance that
        // only has MSBuild properties and items available; in this case the ProjectInstance.FullPath will be an empty string.
        // Since in the out-of-process build case we will still have the project evaluation, we can get the full path from there.
        _projectFullPath = Project?.FullPath ?? _projectInstance.FullPath;
        Contract.ThrowIfTrue(string.IsNullOrEmpty(_projectFullPath));
        _projectDirectory = PathUtilities.EnsureTrailingSeparator(PathUtilities.GetDirectoryName(_projectFullPath));
    }

    public ProjectFileInfo CreateProjectFileInfo()
    {
        var commandLineArgs = TryGetCommandLineArgs(_projectInstance);

        using var propertiesSpan = BuildHostTrace.Start("extract-properties", _projectFullPath);
        var outputFilePath = _projectInstance.ReadPropertyString(PropertyNames.TargetPath);
        if (!RoslynString.IsNullOrWhiteSpace(outputFilePath))
        {
            outputFilePath = GetAbsolutePathRelativeToProject(outputFilePath);
        }

        var outputRefFilePath = _projectInstance.ReadPropertyString(PropertyNames.TargetRefPath);
        if (!RoslynString.IsNullOrWhiteSpace(outputRefFilePath))
        {
            outputRefFilePath = GetAbsolutePathRelativeToProject(outputRefFilePath);
        }

        var generatedFilesOutputDirectory = _projectInstance.ReadPropertyString(PropertyNames.CompilerGeneratedFilesOutputPath);
        generatedFilesOutputDirectory = RoslynString.IsNullOrWhiteSpace(generatedFilesOutputDirectory)
            ? null
            : GetAbsolutePathRelativeToProject(generatedFilesOutputDirectory);

        var intermediateOutputFilePath = _projectInstance.GetItems(ItemNames.IntermediateAssembly).FirstOrDefault()?.EvaluatedInclude;
        if (!RoslynString.IsNullOrWhiteSpace(intermediateOutputFilePath))
        {
            intermediateOutputFilePath = GetAbsolutePathRelativeToProject(intermediateOutputFilePath);
        }

        var projectAssetsFilePath = _projectInstance.ReadPropertyString(PropertyNames.ProjectAssetsFile);

        // Right now VB doesn't have the concept of "default namespace". But we conjure one in workspace 
        // by assigning the value of the project's root namespace to it. So various feature can choose to 
        // use it for their own purpose.
        // In the future, we might consider officially exposing "default namespace" for VB project 
        // (e.g. through a <defaultnamespace> msbuild property)
        var defaultNamespace = _projectInstance.ReadPropertyString(PropertyNames.RootNamespace) ?? string.Empty;

        var targetFramework = _projectInstance.ReadPropertyString(PropertyNames.TargetFramework);
        if (RoslynString.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = null;
        }

        var targetFrameworkIdentifier = _projectInstance.ReadPropertyString(PropertyNames.TargetFrameworkIdentifier);

        var targetFrameworkVersion = _projectInstance.ReadPropertyString(PropertyNames.TargetFrameworkVersion);
        propertiesSpan?.Set("targetFramework", targetFramework);
        propertiesSpan?.Dispose();

        DocumentFileInfo[] docs;
        using (var span = BuildHostTrace.Start("extract-documents", _projectFullPath))
        {
            docs = _projectInstance.GetDocuments()
                .Where(IsNotTemporaryGeneratedFile)
                .Select(MakeDocumentFileInfo)
                .ToArray();
            span?.Set("documents", docs.Length);
        }

        DocumentFileInfo[] additionalDocs;
        DocumentFileInfo[] analyzerConfigDocs;
        using (var span = BuildHostTrace.Start("extract-additional-editorconfig", _projectFullPath))
        {
            additionalDocs = _projectInstance.GetAdditionalFiles()
                .Select(MakeNonSourceFileDocumentFileInfo)
                .ToArray();

            analyzerConfigDocs = _projectInstance.GetEditorConfigFiles()
                .Select(MakeNonSourceFileDocumentFileInfo)
                .ToArray();
            span?.Set("additionalDocuments", additionalDocs.Length);
            span?.Set("analyzerConfigDocuments", analyzerConfigDocs.Length);
        }

        using var itemsSpan = BuildHostTrace.Start("extract-items", _projectFullPath);
        var packageReferences = _projectInstance.GetPackageReferences();

        // Do not pass metadata references if we have command line args that already specify them.
        var metadataReferences = commandLineArgs.Length == 0 ? _projectInstance.GetMetadataReferences().ToArray() : [];

        var projectCapabilities = _projectInstance.GetItems(ItemNames.ProjectCapability).Select(item => item.ToString()).ToArray();
        var contentFileInfo = GetContentFiles(_projectInstance);
        var codePage = _projectInstance.ReadCodePage();

        var checksumAlgorithm = _projectInstance.ReadPropertyString(PropertyNames.ChecksumAlgorithm);
        if (string.IsNullOrEmpty(checksumAlgorithm))
        {
            // F# uses PdbChecksumAlgorithm property name
            checksumAlgorithm = _projectInstance.ReadPropertyString(PropertyNames.PdbChecksumAlgorithm);
        }

        itemsSpan?.Set("packageReferences", packageReferences.Length);
        itemsSpan?.Set("metadataReferences", metadataReferences.Length);
        itemsSpan?.Set("projectCapabilities", projectCapabilities.Length);
        itemsSpan?.Set("contentFiles", contentFileInfo.Length);
        itemsSpan?.Dispose();

        FileGlobs[] fileGlobs;
        using (var span = BuildHostTrace.Start("extract-globs", _projectFullPath))
        {
            fileGlobs = Project?.GetAllGlobs().Select(GetFileGlobs).ToArray() ?? [];
            span?.Set("fileGlobs", fileGlobs.Length);
            span?.Set("includes", fileGlobs.Sum(glob => glob.Includes.Length));
            span?.Set("excludes", fileGlobs.Sum(glob => glob.Excludes.Length));
            span?.Set("removes", fileGlobs.Sum(glob => glob.Removes.Length));
            span?.Set("imports", Project?.Imports.Count ?? 0);
        }

        using var resultSpan = BuildHostTrace.Start("extract-result", _projectFullPath);
        var info = new ProjectFileInfo()
        {
            Language = Language,
            FilePath = _projectFullPath,
            OutputFilePath = outputFilePath,
            OutputRefFilePath = outputRefFilePath,
            GeneratedFilesOutputDirectory = generatedFilesOutputDirectory,
            IntermediateOutputFilePath = intermediateOutputFilePath,
            DefaultNamespace = defaultNamespace,
            TargetFramework = targetFramework,
            TargetFrameworkIdentifier = targetFrameworkIdentifier,
            TargetFrameworkVersion = targetFrameworkVersion,
            ProjectAssetsFilePath = projectAssetsFilePath,
            CommandLineArgs = commandLineArgs,
            Documents = docs,
            AdditionalDocuments = additionalDocs,
            AnalyzerConfigDocuments = analyzerConfigDocs,
            ProjectReferences = [.. _projectInstance.GetProjectReferences()],
            PackageReferences = packageReferences,
            MetadataReferences = metadataReferences,
            CodePage = codePage,
            ChecksumAlgorithm = checksumAlgorithm,
            ProjectCapabilities = projectCapabilities,
            ContentFilePaths = contentFileInfo,
            FileGlobs = fileGlobs
        };
        resultSpan?.Set("projectReferences", info.ProjectReferences.Length);
        return info;

        static FileGlobs GetFileGlobs(MSB.Evaluation.GlobResult g)
        {
            return new FileGlobs(
                Includes: [.. g.IncludeGlobs.Select(PathUtilities.ExpandAbsolutePathWithRelativeParts)],
                Excludes: [.. g.Excludes.Select(PathUtilities.ExpandAbsolutePathWithRelativeParts)],
                Removes: [.. g.Removes.Select(PathUtilities.ExpandAbsolutePathWithRelativeParts)]);
        }
    }

    private static string[] GetContentFiles(MSB.Execution.ProjectInstance project)
    {
        var contentFiles = project
            .GetItems(ItemNames.Content)
            .Select(item => item.GetMetadataValue(MetadataNames.FullPath))
            .ToArray();
        return contentFiles;
    }

    private string[] TryGetCommandLineArgs(MSB.Execution.ProjectInstance project)
    {
        if (_commandLineProvider == null)
        {
            return [];
        }

        string[] commandLineArgs;
        using (var span = BuildHostTrace.Start("extract-compiler-args", _projectFullPath))
        {
            commandLineArgs = _commandLineProvider.GetCompilerCommandLineArgs(project)
                .Select(item => item.ItemSpec)
                .ToArray();
            span?.Set("arguments", commandLineArgs.Length);
        }

        if (commandLineArgs.Length == 0)
        {
            // We didn't get any command-line args, which likely means that the build
            // was not successful. In that case, try to read the command-line args from
            // the ProjectInstance that we have. This is a best effort to provide something
            // meaningful for the user, though it will likely be incomplete.
            using var span = BuildHostTrace.Start("extract-fallback-args", _projectFullPath);
            commandLineArgs = _commandLineProvider.ReadCommandLineArgs(project);
            span?.Set("arguments", commandLineArgs.Length);
        }

        return commandLineArgs;
    }

    private static bool IsNotTemporaryGeneratedFile(MSB.Framework.ITaskItem item)
        => !Path.GetFileName(item.ItemSpec).StartsWith("TemporaryGeneratedFile_", StringComparison.Ordinal);

    private DocumentFileInfo MakeDocumentFileInfo(MSB.Framework.ITaskItem documentItem)
    {
        var filePath = GetDocumentFilePath(documentItem);
        var logicalPath = GetDocumentLogicalPath(documentItem, _projectDirectory);
        var isLinked = IsDocumentLinked(documentItem);

        var folders = GetRelativeFolders(documentItem);
        return new DocumentFileInfo(filePath, logicalPath, isLinked, isGenerated: false, folders);
    }

    private DocumentFileInfo MakeNonSourceFileDocumentFileInfo(MSB.Framework.ITaskItem documentItem)
    {
        var filePath = GetDocumentFilePath(documentItem);
        var logicalPath = GetDocumentLogicalPath(documentItem, _projectDirectory);
        var isLinked = IsDocumentLinked(documentItem);

        var folders = GetRelativeFolders(documentItem);
        return new DocumentFileInfo(filePath, logicalPath, isLinked, isGenerated: false, folders);
    }

    private string[] GetRelativeFolders(MSB.Framework.ITaskItem documentItem)
    {
        var linkPath = documentItem.GetMetadata(MetadataNames.Link);
        if (!RoslynString.IsNullOrEmpty(linkPath))
        {
            return PathUtilities.GetDirectoryName(linkPath).Split(PathUtilities.DirectorySeparatorChar, PathUtilities.AltDirectorySeparatorChar);
        }
        else
        {
            var filePath = documentItem.ItemSpec;
            var relativePath = PathUtilities.GetDirectoryName(PathUtilities.GetRelativePath(_projectDirectory, filePath));
            var folders = relativePath == null ? [] : relativePath.Split([PathUtilities.DirectorySeparatorChar, PathUtilities.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            return folders;
        }
    }

    /// <summary>
    /// Resolves the given path that is possibly relative to the project directory.
    /// </summary>
    /// <remarks>
    /// The resulting path is absolute but might not be normalized.
    /// </remarks>
    private string GetAbsolutePathRelativeToProject(string path)
    {
        // TODO (tomat): should we report an error when drive-relative path (e.g. "C:goo.cs") is encountered?
        var absolutePath = FileUtilities.ResolveRelativePath(path, _projectDirectory) ?? path;
        return FileUtilities.TryNormalizeAbsolutePath(absolutePath) ?? absolutePath;
    }

    private string GetDocumentFilePath(MSB.Framework.ITaskItem documentItem)
        => GetAbsolutePathRelativeToProject(documentItem.ItemSpec);

    private static bool IsDocumentLinked(MSB.Framework.ITaskItem documentItem)
        => !RoslynString.IsNullOrEmpty(documentItem.GetMetadata(MetadataNames.Link));

    private static string GetDocumentLogicalPath(MSB.Framework.ITaskItem documentItem, string projectDirectory)
    {
        var link = documentItem.GetMetadata(MetadataNames.Link);
        if (!RoslynString.IsNullOrEmpty(link))
        {
            // if a specific link is specified in the project file then use it to form the logical path.
            return link;
        }
        else
        {
            var filePath = documentItem.ItemSpec;

            if (!PathUtilities.IsAbsolute(filePath))
            {
                return filePath;
            }

            var normalizedPath = FileUtilities.TryNormalizeAbsolutePath(filePath);
            if (normalizedPath == null)
            {
                return filePath;
            }

            // If the document is within the current project directory (or subdirectory), then the logical path is the relative path 
            // from the project's directory.
            if (normalizedPath.StartsWith(projectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedPath[projectDirectory.Length..];
            }
            else
            {
                // if the document lies outside the project's directory (or subdirectory) then place it logically at the root of the project.
                // if more than one document ends up with the same logical name then so be it (the workspace will survive.)
                return PathUtilities.GetFileName(normalizedPath);
            }
        }
    }
}
