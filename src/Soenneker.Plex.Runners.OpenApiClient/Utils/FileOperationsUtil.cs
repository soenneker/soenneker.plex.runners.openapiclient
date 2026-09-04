using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Soenneker.Extensions.String;
using Soenneker.Git.Util.Abstract;
using Soenneker.Plex.Runners.OpenApiClient.Utils.Abstract;
using Soenneker.Utils.Dotnet.Abstract;
using Soenneker.Utils.Environment;
using Soenneker.Utils.Process.Abstract;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Extensions.ValueTask;
using Soenneker.Kiota.Util.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.File.Download.Abstract;
using System.Collections.Generic;

namespace Soenneker.Plex.Runners.OpenApiClient.Utils;

/// <inheritdoc cref="IFileOperationsUtil" />
public sealed class FileOperationsUtil : IFileOperationsUtil
{
    private readonly ILogger<FileOperationsUtil> _logger;
    private readonly IConfiguration _configuration;
    private readonly IGitUtil _gitUtil;
    private readonly IDotnetUtil _dotnetUtil;
    private readonly IProcessUtil _processUtil;
    private readonly IKiotaUtil _kiotaUtil;
    private readonly IFileDownloadUtil _fileDownloadUtil;
    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;

    public FileOperationsUtil(ILogger<FileOperationsUtil> logger, IConfiguration configuration, IGitUtil gitUtil, IDotnetUtil dotnetUtil, IProcessUtil processUtil, 
        IFileDownloadUtil fileDownloadUtil, IFileUtil fileUtil, IDirectoryUtil directoryUtil, IKiotaUtil kiotaUtil)
    {
        _logger = logger;
        _configuration = configuration;
        _gitUtil = gitUtil;
        _dotnetUtil = dotnetUtil;
        _processUtil = processUtil;
        _kiotaUtil = kiotaUtil;
        _fileDownloadUtil = fileDownloadUtil;
        _fileUtil = fileUtil;
        _directoryUtil = directoryUtil;
    }

    public async ValueTask Process(CancellationToken cancellationToken = default)
    {
        string gitDirectory = await _gitUtil.CloneToTempDirectory($"https://github.com/soenneker/{Constants.Library.ToLowerInvariantFast()}", cancellationToken: cancellationToken);

        string targetFilePath = Path.Combine(gitDirectory, "openapi.yaml");

        await _fileUtil.DeleteIfExists(targetFilePath, cancellationToken: cancellationToken);

        string openApiDocumentUrl = _configuration["Plex:ClientGenerationUrl"] ?? "https://raw.githubusercontent.com/LukasParke/plex-api-spec/refs/heads/main/plex-api-spec.yaml";

        string? filePath = await _fileDownloadUtil.Download(openApiDocumentUrl,
            targetFilePath, fileExtension: ".yaml", cancellationToken: cancellationToken);

        if (filePath is null || !await _fileUtil.Exists(filePath, cancellationToken))
            throw new InvalidOperationException("The Plex OpenAPI document was not downloaded.");

        await _kiotaUtil.EnsureInstalled(cancellationToken);

        string srcDirectory = Path.Combine(gitDirectory, "src", Constants.Library);

        await DeleteGeneratedSources(srcDirectory, cancellationToken);

        await _kiotaUtil.Generate(filePath, "PlexOpenApiClient", Constants.Library, gitDirectory, cancellationToken).NoSync();

        await BuildAndPush(gitDirectory, cancellationToken).NoSync();
    }

    private async ValueTask DeleteGeneratedSources(string directoryPath, CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(directoryPath);
        string projectFile = Path.Combine(root, $"{Constants.Library}.csproj");

        if (!await _directoryUtil.Exists(root, cancellationToken) || !await _fileUtil.Exists(projectFile, cancellationToken))
            throw new InvalidOperationException($"Refusing to clean '{root}' because the generated-client project file was not found.");

        List<string> files = await _directoryUtil.GetFilesByExtension(root, "", true, cancellationToken);
        foreach (string file in files)
        {
            string fullPath = EnsureWithinDirectory(root, file);

            if (!fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                await _fileUtil.Delete(fullPath, ignoreMissing: true, log: false, cancellationToken);
        }

        List<string> dirs = await _directoryUtil.GetAllDirectoriesRecursively(root, cancellationToken);
        foreach (string dir in dirs.OrderByDescending(static value => value.Length))
        {
            string fullPath = EnsureWithinDirectory(root, dir);
            List<string> dirFiles = await _directoryUtil.GetFilesByExtension(fullPath, "", false, cancellationToken);
            List<string> subDirs = await _directoryUtil.GetAllDirectories(fullPath, cancellationToken);

            if (dirFiles.Count == 0 && subDirs.Count == 0)
                await _directoryUtil.Delete(fullPath, cancellationToken);
        }
    }

    private static string EnsureWithinDirectory(string root, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootPrefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to modify a path outside '{root}'.");

        return fullPath;
    }

    private async ValueTask BuildAndPush(string gitDirectory, CancellationToken cancellationToken)
    {
        string projFilePath = Path.Combine(gitDirectory, "src", Constants.Library, $"{Constants.Library}.csproj");

        await _dotnetUtil.Restore(projFilePath, cancellationToken: cancellationToken);

        bool successful = await _dotnetUtil.Build(projFilePath, true, "Release", false, cancellationToken: cancellationToken);

        if (!successful)
            throw new InvalidOperationException("The generated Plex OpenAPI client did not build; no changes were pushed.");

        string gitHubToken = EnvironmentUtil.GetVariableStrict("GH__TOKEN");
        string name = EnvironmentUtil.GetVariableStrict("GIT__NAME");
        string email = EnvironmentUtil.GetVariableStrict("GIT__EMAIL");

        await _gitUtil.CommitAndPush(gitDirectory, "Automated update", gitHubToken, name, email, cancellationToken);
    }
}
