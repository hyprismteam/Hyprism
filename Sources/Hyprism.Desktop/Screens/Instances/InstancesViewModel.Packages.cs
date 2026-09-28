// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hyprism.Core.Game.Instances;
using Hyprism.Core.Models;

namespace Hyprism.Desktop.Screens.Instances;

public enum InstanceWizardStage { Choice, Download, Import, ExportKind, ExportFormat }

public sealed partial class InstancesViewModel
{
    private CancellationTokenSource? _instanceExportCancellation;
    private CancellationTokenSource? _instanceImportCancellation;
    private string? _exportingInstanceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWizardChoice))]
    [NotifyPropertyChangedFor(nameof(IsWizardDownload))]
    [NotifyPropertyChangedFor(nameof(IsWizardImport))]
    [NotifyPropertyChangedFor(nameof(IsWizardExportKind))]
    [NotifyPropertyChangedFor(nameof(IsWizardExportFormat))]
    [NotifyPropertyChangedFor(nameof(WizardAnimationPath))]
    private InstanceWizardStage _instanceWizardStage;

    [ObservableProperty]
    private int _exportFormatIndex;

    [ObservableProperty]
    private bool _isImportingInstancePackage;

    [ObservableProperty]
    private bool _isInstanceImportCancellationArmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstanceExportError))]
    private string _instanceExportError = string.Empty;

    private InstancePackageKind _selectedExportKind;

    public bool CanReturnToInstanceChoice { get; private set; }
    public bool IsWizardChoice => InstanceWizardStage == InstanceWizardStage.Choice;
    public bool IsWizardDownload => InstanceWizardStage == InstanceWizardStage.Download;
    public bool IsWizardImport => InstanceWizardStage == InstanceWizardStage.Import;
    public bool IsWizardExportKind => InstanceWizardStage == InstanceWizardStage.ExportKind;
    public bool IsWizardExportFormat => InstanceWizardStage == InstanceWizardStage.ExportFormat;
    public bool HasInstanceExportError => !string.IsNullOrWhiteSpace(InstanceExportError);
    public bool IsManagedInstanceExporting => _managedInstance is not null &&
        string.Equals(_managedInstance.Id, _exportingInstanceId, StringComparison.Ordinal);
    public bool CanExportManagedInstance => _managedInstance is not null &&
        _exportingInstanceId is null && !IsInstanceBusy(_managedInstance.Id);
    public string WizardAnimationPath => IsWizardExportKind || IsWizardExportFormat
        ? "/Assets/Lotties/share-reveal.json"
        : "/Assets/Lotties/server-reveal.json";
    public string ChoiceTitle => _localizer["instances.package.createChoiceTitle"];
    public string ChoiceHint => _localizer["instances.package.createChoiceHint"];
    public string ImportTitle => _localizer["instances.package.importTitle"];
    public string ImportHint => _localizer["instances.package.importHint"];
    public string ExportTitle => _localizer["instances.package.exportTitle"];
    public string ExportFormatTitle => _localizer["instances.package.formatTitle"];
    public string ExportFormatHint => _localizer["instances.package.formatHint"];
    public string ExportLabel => _localizer["common.export"];
    public string ExportingLabel => _localizer["common.exporting"];
    public string ImportLabel => _localizer["common.import"];
    public string DownloadChoiceLabel => _localizer["instances.package.download"];
    public string DownloadChoiceHint => _localizer["instances.package.downloadHint"];
    public string ImportChoiceHint => _localizer["instances.package.importChoiceHint"];
    public string ExportBuildLabel => _localizer["instances.package.build"];
    public string ExportHint => _localizer["instances.package.exportHint"];
    public string ExportBuildHint => _localizer["instances.package.buildHint"];
    public string ExportModpackLabel => _localizer["instances.package.modpack"];
    public string ExportModpackHint => _localizer["instances.package.modpackHint"];
    public string ExportGameLabel => _localizer["instances.package.game"];
    public string ExportGameHint => _localizer["instances.package.gameHint"];
    public string ExportFormatLabel => _localizer["instances.package.format"];
    public string ExportJsonLabel => _localizer["instances.package.json"];
    public string ExportZipLabel => _localizer["instances.package.zip"];
    public string PickPackageLabel => _localizer["instances.package.pickFile"];
    public string ImportMetadataHint => _localizer["instances.package.metadataOnly"];

    [RelayCommand]
    private void ChooseInstanceDownload() => InstanceWizardStage = InstanceWizardStage.Download;

    [RelayCommand]
    private void ChooseInstanceImport() => InstanceWizardStage = InstanceWizardStage.Import;

    [RelayCommand]
    private void BackInstanceWizard()
    {
        if (TryCancelInstanceImport())
            return;

        if (IsWizardChoice || IsWizardExportKind ||
            ((IsWizardDownload || IsWizardImport) && !CanReturnToInstanceChoice))
        {
            CloseInstanceCreatorCommand.Execute(null);
            return;
        }

        InstanceWizardStage = InstanceWizardStage switch
        {
            InstanceWizardStage.Download or InstanceWizardStage.Import => InstanceWizardStage.Choice,
            InstanceWizardStage.ExportFormat => InstanceWizardStage.ExportKind,
            _ => InstanceWizardStage
        };
    }

    [RelayCommand]
    private void OpenInstanceExport()
    {
        if (!CanExportManagedInstance)
            return;

        InstanceExportError = string.Empty;
        ExportFormatIndex = 0;
        CanReturnToInstanceChoice = false;
        InstanceWizardStage = InstanceWizardStage.ExportKind;
        IsInstanceCreatorOpen = true;
    }

    [RelayCommand]
    private void SelectExportKind(string? kind)
    {
        if (!Enum.TryParse<InstancePackageKind>(kind, true, out var selected))
            return;
        _selectedExportKind = selected;
        InstanceWizardStage = InstanceWizardStage.ExportFormat;
    }

    [RelayCommand]
    private async Task ExportInstanceAsync()
    {
        if (_managedInstance is not { } instance || _filePicker is null ||
            !CanExportManagedInstance)
            return;

        var format = ExportFormatIndex == 1 ? InstancePackageFormat.Zip : InstancePackageFormat.Json;
        var extension = format == InstancePackageFormat.Zip ? "zip" : "json";
        var name = string.Concat(instance.Name.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
        var destination = await _filePicker.SaveFileAsync(
            $"{name}-{_selectedExportKind.ToString().ToLowerInvariant()}.{extension}",
            format == InstancePackageFormat.Zip ? "ZIP archive|*.zip" : "JSON manifest|*.json");
        if (string.IsNullOrWhiteSpace(destination) || !IsInstanceCreatorOpen ||
            InstanceWizardStage != InstanceWizardStage.ExportFormat)
            return;

        var instancePath = _instances.GetInstancePathById(instance.Id);
        var meta = instancePath is null ? null : _instances.GetInstanceMeta(instancePath);
        if (instancePath is null || meta is null)
        {
            InstanceExportError = _localizer["instances.package.missingInstance"];
            return;
        }

        _instanceExportCancellation = new CancellationTokenSource();
        var cancellation = _instanceExportCancellation;
        _exportingInstanceId = instance.Id;
        NotifyExportStateChanged();
        IsInstanceCreatorOpen = false;
        try
        {
            await Task.Run(() => InstancePackageService.ExportAsync(
                instancePath, meta, _selectedExportKind, format, destination, cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            // The temporary file is removed by the package service.
        }
        catch (Exception exception)
        {
            InstanceExportError = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_instanceExportCancellation, cancellation))
            {
                _instanceExportCancellation = null;
                _exportingInstanceId = null;
                NotifyExportStateChanged();
            }
            cancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CancelInstanceExport() => _instanceExportCancellation?.Cancel();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ImportInstancePackageAsync()
    {
        if (IsImportingInstancePackage)
        {
            if (IsInstanceImportCancellationArmed)
                _instanceImportCancellation?.Cancel();
            return;
        }

        if (_filePicker is null)
            return;

        using var cancellation = new CancellationTokenSource();
        _instanceImportCancellation = cancellation;
        IsInstanceImportCancellationArmed = false;
        IsImportingInstancePackage = true;
        InstanceCreationError = string.Empty;
        try
        {
            var path = await _filePicker.BrowseInstancePackageAsync();
            if (string.IsNullOrWhiteSpace(path) || !IsInstanceCreatorOpen ||
                InstanceWizardStage != InstanceWizardStage.Import)
                return;
            cancellation.Token.ThrowIfCancellationRequested();

            var previousIds = _instances.GetCachedInstances()
                .Select(instance => instance.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                await _instances.ImportFromZipAsync(path, cancellation.Token);
            }
            else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                var package = await InstancePackageService.ReadJsonAsync(path, cancellation.Token);
                var source = package.Instance;
                var created = _instances.CreateInstanceMeta(
                    source.Branch, source.Version, source.Name, source.VersionName);
                var instancePath = _instances.GetInstancePathById(created.Id);
                if (instancePath is not null)
                {
                    created.Notes = source.Notes;
                    _instances.SaveInstanceMeta(instancePath, created);
                }
            }
            else
            {
                throw new InvalidDataException(_localizer["instances.package.unsupportedFile"]);
            }

            RefreshInstances();
            var imported = _instances.GetCachedInstances()
                .FirstOrDefault(instance => !previousIds.Contains(instance.Id));
            if (imported is not null)
                OpenInstanceDetailsCommand.Execute(imported.Id);
            IsInstanceCreatorOpen = false;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            InstanceCreationError = exception.Message;
        }
        finally
        {
            if (ReferenceEquals(_instanceImportCancellation, cancellation))
                _instanceImportCancellation = null;
            IsInstanceImportCancellationArmed = false;
            IsImportingInstancePackage = false;
        }
    }

    /// <summary>Allows the active import button to cancel after the pointer leaves it.</summary>
    public void ArmInstanceImportCancellation()
    {
        if (IsImportingInstancePackage)
            IsInstanceImportCancellationArmed = true;
    }

    /// <summary>Cancels an import that is waiting for file selection or reading a package.</summary>
    /// <returns>Whether an import was active.</returns>
    public bool TryCancelInstanceImport()
    {
        if (!IsImportingInstancePackage)
            return false;
        _instanceImportCancellation?.Cancel();
        return true;
    }

    private void NotifyExportStateChanged()
    {
        OnPropertyChanged(nameof(IsManagedInstanceExporting));
        OnPropertyChanged(nameof(CanExportManagedInstance));
    }
}
