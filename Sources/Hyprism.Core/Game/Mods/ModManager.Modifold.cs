// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using Hyprism.Core.Infrastructure;
using Hyprism.Core.Models;

namespace Hyprism.Core.Game.Mods;

/// <summary>Resolves and installs Modifold dependency graphs.</summary>
public partial class ModManager
{
    private async Task<List<ModDependency>> GetModifoldDependenciesAsync(string projectId, string fileId)
    {
        try
        {
            return await _modifoldClient.GetDependenciesAsync(projectId, fileId);
        }
        catch (Exception exception)
        {
            Logger.Warning("ModManager", $"Could not load Modifold dependencies: {exception.Message}");
            return [];
        }
    }

    private async Task<bool> InstallModifoldFileAsync(
        string projectId, string fileId, string instancePath, Action<string, string>? onProgress,
        CancellationToken cancellationToken, string? gameVersion, IReadOnlyList<ModFileInfo> roots,
        Dictionary<string, List<(ModInfo Mod, ModFileInfo File)>> plans)
    {
        try
        {
            if (plans.Count == 0)
            {
                var resolvedPlans = await ResolveModifoldPlansAsync(roots, instancePath, gameVersion, cancellationToken);
                foreach (var (key, plan) in resolvedPlans)
                    plans.Add(key, plan);
            }

            foreach (var (mod, file) in plans[$"{projectId}:{fileId}"])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var installedMods = GetInstanceInstalledMods(instancePath);
                var installed = installedMods.FirstOrDefault(candidate => candidate.Enabled &&
                    candidate.Source == "modifold" && candidate.SourceProjectId == mod.Id && candidate.FileId == file.Id &&
                    File.Exists(Path.Combine(instancePath, "UserData", "Mods", Path.GetFileName(candidate.FileName))));
                if (installed is not null)
                {
                    installed.Dependencies = file.Dependencies;
                    await SaveInstanceModsAsync(instancePath, installedMods);
                    continue;
                }

                if (mod.Id != projectId)
                    onProgress?.Invoke("dependency", mod.Name);
                if (!await InstallRawModifoldFileAsync(mod, file, instancePath, onProgress, cancellationToken))
                    return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Logger.Warning("ModManager", $"Modifold dependency installation failed: {exception.Message}");
            return false;
        }
    }

    private async Task<Dictionary<string, List<(ModInfo Mod, ModFileInfo File)>>> ResolveModifoldPlansAsync(
        IReadOnlyList<ModFileInfo> roots, string instancePath, string? gameVersion, CancellationToken cancellationToken)
    {
        var installedMods = GetInstanceInstalledMods(instancePath)
            .Where(mod => File.Exists(Path.Combine(instancePath, "UserData", "Mods", Path.GetFileName(mod.FileName))))
            .ToList();
        var resolved = new Dictionary<string, (ModInfo Mod, ModFileInfo File)>(StringComparer.Ordinal);
        var pins = new Dictionary<string, string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var completed = new HashSet<string>(StringComparer.Ordinal);
        var plans = new Dictionary<string, List<(ModInfo Mod, ModFileInfo File)>>(StringComparer.Ordinal);
        var rootIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var attempts = new HashSet<string>(StringComparer.Ordinal);
        var needsReplan = false;

        void Pin(string id, string version)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ModDependencyException("Modifold returned a required dependency without a project ID");
            if (string.IsNullOrWhiteSpace(version) || version == "latest")
                return;
            if (pins.TryGetValue(id, out var existing) && existing != version)
                throw new ModDependencyException($"Conflicting Modifold versions requested for {id}: {existing}, {version}");
            pins[id] = version;
        }

        async Task<(ModInfo Mod, ModFileInfo File)> Resolve(string id, string version)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(id) ||
                id.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
                throw new ModDependencyException($"Invalid Modifold project ID: {id}");
            return await _modifoldClient.ResolveInstallFileAsync(id, version, gameVersion, cancellationToken)
                ?? throw new ModDependencyException($"Modifold file {id}:{version} is missing or incompatible with the game");
        }

        // Resolve explicit selections first so dependencies cannot replace a selected version.
        foreach (var root in roots)
        {
            var node = await Resolve(root.ModId, root.Id);
            Pin(node.Mod.Id, root.Id);
            resolved[node.Mod.Id] = node;
            rootIds[$"{root.ModId}:{root.Id}"] = node.Mod.Id;
        }
        var rootPins = new Dictionary<string, string>(pins, StringComparer.Ordinal);

        foreach (var node in resolved.Values)
            foreach (var dependency in node.File.Dependencies.Where(dependency =>
                         dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency))
                Pin(dependency.ModId, dependency.FileId);

        foreach (var id in resolved.Keys.ToArray())
            if (pins.TryGetValue(id, out var pinnedVersion) && resolved[id].File.Id != pinnedVersion)
                resolved[id] = await Resolve(id, pinnedVersion);

        async Task Visit(string id, List<(ModInfo Mod, ModFileInfo File)> plan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visiting.Add(id))
                throw new ModDependencyException($"Circular Modifold dependency detected at {id}");
            if (completed.Contains(id))
            {
                visiting.Remove(id);
                return;
            }

            var node = resolved[id];
            foreach (var dependency in node.File.Dependencies.Where(dependency =>
                         dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency))
                Pin(dependency.ModId, dependency.FileId);
            foreach (var dependency in node.File.Dependencies.Where(dependency =>
                         dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency))
            {
                if (!resolved.TryGetValue(dependency.ModId, out var child))
                {
                    var installed = installedMods.FirstOrDefault(mod => mod.Enabled &&
                        mod.Source == "modifold" && mod.SourceProjectId == dependency.ModId);
                    if (pins.TryGetValue(dependency.ModId, out var pin))
                        child = await Resolve(dependency.ModId, pin);
                    else
                        child = (installed is null || string.IsNullOrWhiteSpace(installed.FileId) ? null :
                            await _modifoldClient.ResolveInstallFileAsync(dependency.ModId, installed.FileId,
                                gameVersion, cancellationToken)) ?? await Resolve(dependency.ModId, "latest");
                    resolved[dependency.ModId] = child;
                }

                if (pins.TryGetValue(dependency.ModId, out var requiredVersion) && child.File.Id != requiredVersion)
                {
                    resolved[dependency.ModId] = await Resolve(dependency.ModId, requiredVersion);
                    needsReplan = true;
                }
                await Visit(dependency.ModId, plan);
            }

            visiting.Remove(id);
            completed.Add(id);
            plan.Add(node);
        }

        do
        {
            var signature = string.Join('|', resolved.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}:{pair.Value.File.Id}"));
            if (!attempts.Add(signature))
                throw new ModDependencyException("Modifold dependency versions could not be reconciled");
            needsReplan = false;
            visiting.Clear();
            completed.Clear();
            plans.Clear();
            pins.Clear();
            foreach (var (id, version) in rootPins)
                pins.Add(id, version);
            foreach (var rootId in rootIds.Values.Distinct(StringComparer.Ordinal))
                foreach (var dependency in resolved[rootId].File.Dependencies.Where(dependency =>
                             dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency))
                    Pin(dependency.ModId, dependency.FileId);
            foreach (var root in roots)
            {
                var key = $"{root.ModId}:{root.Id}";
                if (plans.ContainsKey(key))
                    continue;
                var plan = new List<(ModInfo Mod, ModFileInfo File)>();
                await Visit(rootIds[key], plan);
                plans.Add(key, plan);
            }
        }
        while (needsReplan);

        // A pinned version may remove nodes discovered while resolving an unpinned file.
        var plannedIds = plans.Values.SelectMany(plan => plan).Select(node => node.Mod.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in resolved.Keys.Where(id => !plannedIds.Contains(id)).ToArray())
            resolved.Remove(id);

        var effectiveVersions = installedMods.Where(mod => mod.Enabled && mod.Source == "modifold")
            .GroupBy(mod => mod.SourceProjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().FileId, StringComparer.Ordinal);
        foreach (var (id, node) in resolved)
            effectiveVersions[id] = node.File.Id;
        var relations = resolved.Values.Select(node => node.File.Dependencies)
            .Concat(installedMods.Where(mod => mod.Enabled && mod.Source == "modifold" &&
                !resolved.ContainsKey(mod.SourceProjectId)).Select(mod => mod.Dependencies));
        foreach (var dependency in relations.SelectMany(dependencies => dependencies).Where(dependency =>
                     dependency.Source == "modifold"))
        {
            if (!effectiveVersions.TryGetValue(dependency.ModId, out var version))
                continue;
            var matches = string.IsNullOrWhiteSpace(dependency.FileId) || dependency.FileId == version;
            if ((dependency.RelationType == CurseForgeDependencyRelationType.Incompatible && matches) ||
                (dependency.RelationType == CurseForgeDependencyRelationType.RequiredDependency && !matches))
                throw new ModDependencyException($"Conflicting Modifold dependency {dependency.ModId}:{dependency.FileId}");
        }

        return plans;
    }
}
