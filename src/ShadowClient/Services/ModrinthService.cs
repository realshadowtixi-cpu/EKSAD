using System.Net.Http.Headers;
using System.Text.Json;

namespace ShadowClient.Services;

public sealed class ModrinthService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ShadowClient/0.1.0 (Windows; Minecraft launcher)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public async Task<List<ModrinthProject>> SearchAsync(
        string query, string gameVersion, string loader, string projectType,
        CancellationToken cancellationToken = default)
    {
        var facets = new List<string[]> { new[] { $"project_type:{projectType}" } };
        if (!string.IsNullOrWhiteSpace(gameVersion))
            facets.Add(new[] { $"versions:{gameVersion}" });
        if (projectType == "mod" && !string.IsNullOrWhiteSpace(loader) && loader != "all")
            facets.Add(new[] { $"categories:{loader}" });

        string url = "https://api.modrinth.com/v2/search?query=" + Uri.EscapeDataString(query) +
                     "&facets=" + Uri.EscapeDataString(JsonSerializer.Serialize(facets)) + "&limit=24&index=relevance";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var results = new List<ModrinthProject>();
        if (!document.RootElement.TryGetProperty("hits", out var hits)) return results;

        foreach (var hit in hits.EnumerateArray())
        {
            results.Add(new ModrinthProject
            {
                ProjectId = hit.TryGetProperty("project_id", out var id) ? id.GetString() ?? "" : "",
                Title = hit.TryGetProperty("title", out var title) ? title.GetString() ?? "Unknown" : "Unknown",
                Description = hit.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : ""
            });
        }
        return results;
    }

    public async Task<int> InstallLatestAsync(
        string projectId, string targetDirectory, string gameVersion, string loader,
        bool resolveDependencies, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(targetDirectory);
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return await InstallProjectRecursiveAsync(projectId, targetDirectory, gameVersion, loader,
            resolveDependencies, installed, 0, cancellationToken);
    }

    private async Task<int> InstallProjectRecursiveAsync(
        string projectId, string targetDirectory, string gameVersion, string loader,
        bool resolveDependencies, HashSet<string> installed, int depth, CancellationToken cancellationToken)
    {
        if (depth > 6 || !installed.Add(projectId)) return 0;

        string gameVersions = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { gameVersion }));
        string versionsUrl = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version?game_versions={gameVersions}";
        // Loader filtering applies to mods. Resource packs and shader packs are loader-independent.
        if (!string.IsNullOrWhiteSpace(loader) && loader != "minecraft" && loader != "all")
        {
            string loaders = Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }));
            versionsUrl += $"&loaders={loaders}";
        }
        using var versionsResponse = await Http.GetAsync(versionsUrl, cancellationToken);
        versionsResponse.EnsureSuccessStatusCode();
        using var versionsDoc = JsonDocument.Parse(await versionsResponse.Content.ReadAsStringAsync(cancellationToken));
        var version = versionsDoc.RootElement.EnumerateArray()
            .FirstOrDefault(v => v.TryGetProperty("version_type", out var vt) && vt.GetString() == "release");
        if (version.ValueKind == JsonValueKind.Undefined)
            version = versionsDoc.RootElement.EnumerateArray().FirstOrDefault();
        if (version.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"No compatible release was found for {projectId} on Minecraft {gameVersion} ({loader}).");

        int dependencyCount = 0;
        if (resolveDependencies && version.TryGetProperty("dependencies", out var dependencies))
        {
            foreach (var dependency in dependencies.EnumerateArray())
            {
                if (!dependency.TryGetProperty("dependency_type", out var depType) || depType.GetString() != "required")
                    continue;

                string? dependencyVersionId = dependency.TryGetProperty("version_id", out var versionId) ? versionId.GetString() : null;
                string? dependencyProjectId = dependency.TryGetProperty("project_id", out var dependencyProject) ? dependencyProject.GetString() : null;
                if (!string.IsNullOrWhiteSpace(dependencyProjectId))
                    dependencyCount += await InstallProjectRecursiveAsync(dependencyProjectId, targetDirectory,
                        gameVersion, loader, true, installed, depth + 1, cancellationToken);
                else if (!string.IsNullOrWhiteSpace(dependencyVersionId))
                    dependencyCount += await InstallVersionByIdAsync(dependencyVersionId, targetDirectory, cancellationToken);
            }
        }

        dependencyCount += await DownloadVersionFilesAsync(version, targetDirectory, cancellationToken);
        return dependencyCount;
    }

    private async Task<int> InstallVersionByIdAsync(string versionId, string targetDirectory, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync("https://api.modrinth.com/v2/version/" + Uri.EscapeDataString(versionId), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return await DownloadVersionFilesAsync(doc.RootElement, targetDirectory, cancellationToken);
    }


    private static async Task<int> DownloadVersionFilesAsync(JsonElement version, string targetDirectory, CancellationToken cancellationToken)
    {
        if (!version.TryGetProperty("files", out var files) || files.GetArrayLength() == 0)
            throw new InvalidOperationException("The selected Modrinth version has no downloadable files.");

        var selected = files.EnumerateArray().FirstOrDefault(f =>
            f.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True);
        if (selected.ValueKind == JsonValueKind.Undefined) selected = files[0];

        string url = selected.GetProperty("url").GetString() ?? throw new InvalidOperationException("Missing download URL.");
        string fileName = Path.GetFileName(selected.GetProperty("filename").GetString() ?? "downloaded-file.jar");
        string destination = Path.Combine(targetDirectory, fileName);
        if (File.Exists(destination)) return 0;

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output, cancellationToken);
        return 1;
    }
}
