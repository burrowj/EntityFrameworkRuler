﻿using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntityFrameworkRuler.Editor.Models;
using EntityFrameworkRuler.Generator;

namespace EntityFrameworkRuler.Editor.Dialogs;

internal sealed partial class RulesFromEdmxViewModel : ObservableObject {
    private readonly Action<SaveRulesResponse> onGenerated;

    public RulesFromEdmxViewModel(string edmxFilePath = null, string targetProjectPath = null, Action<SaveRulesResponse> onGenerated = null) {
        this.onGenerated = onGenerated;
        SuggestedEdmxFiles = new ObservableCollection<ObservableFileInfo>();
        if (edmxFilePath.HasNonWhiteSpace() && edmxFilePath.EndsWithIgnoreCase(".edmx")) {
            SuggestedEdmxFiles.Add(new ObservableFileInfo(new FileInfo(edmxFilePath.Trim())));
            SelectedEdmxFile = SuggestedEdmxFiles[0];
        }
        if (targetProjectPath.HasNonWhiteSpace()) {
            TargetProjectPath = targetProjectPath;
            if (SuggestedEdmxFiles.Count == 0) FindEdmxFilesNear(targetProjectPath);
        }
    }

    private async void FindEdmxFilesNear(string path) {
        try {
            if (path.EndsWithIgnoreCase(".csproj") || path.EndsWithIgnoreCase(".edmx") || path.EndsWithIgnoreCase(".json")) path = new FileInfo(path).Directory?.FullName;
            if (path.IsNullOrWhiteSpace()) return;
            var files = await path
                .FindEdmxFilesNearProjectAsync()
                .ConfigureAwait(true);
            files.Select(o => new ObservableFileInfo(o)).ForAll(o => suggestedEdmxFiles.Add(o));
            if (suggestedEdmxFiles?.Count == 1) SelectedEdmxFile = suggestedEdmxFiles[0];
        } catch {
        }
    }

    [ObservableProperty] private ObservableCollection<ObservableFileInfo> suggestedEdmxFiles;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private ObservableFileInfo selectedEdmxFile;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))] private string targetProjectPath;
    [ObservableProperty] private bool noPluralize;
    [ObservableProperty] private bool includeUnknowns;
    [ObservableProperty] private bool compactRules;
    [ObservableProperty] private bool useDatabaseNames;
    [ObservableProperty] private bool compactRulesEnabled;


    partial void OnSelectedEdmxFileChanged(ObservableFileInfo? value) {
    }

    partial void OnIncludeUnknownsChanged(bool value) {
        CompactRulesEnabled = value; // can only compact rules when we are including unknown elements.
        if (!CompactRulesEnabled) CompactRules = false;
    }
    [RelayCommand]
    private void EdmxBrowse() {
        // Configure open file dialog box
        var dialog = new Microsoft.Win32.OpenFileDialog {
            FileName = "Document", // Default file name
            DefaultExt = ".edmx", // Default file extension
            Filter = "Edmx (.edmx)|*.edmx",
            Title = "Select an EDMX file" // Filter files by extension
        };
        // Show open file dialog box
        var result = dialog.ShowDialog();

        // Process open file dialog box results
        if (result != true) return;
        // Open document
        var filename = dialog.FileName;
        SelectedEdmxFile = new ObservableFileInfo(new FileInfo(filename));
    }
    [RelayCommand]
    private void ProjectBrowse() {
        // Configure open file dialog box
        var dialog = new Microsoft.Win32.OpenFileDialog {
            FileName = "Document", // Default file name
            DefaultExt = ".csproj", // Default file extension
            Filter = "Project (.csproj)|*.csproj",
            Title = "Select a target EF Core project" // Filter files by extension
        };
        // Show open file dialog box
        var result = dialog.ShowDialog();

        // Process open file dialog box results
        if (result != true) return;
        // Open document
        var filename = dialog.FileName;
        if (filename.IsNullOrWhiteSpace()) return;
        TargetProjectPath = new FileInfo(filename).Directory?.FullName;
    }

    private bool CanGenerate => SelectedEdmxFile != null && TargetProjectPath.HasNonWhiteSpace();

    [RelayCommand(CanExecute = nameof(CanGenerate), AllowConcurrentExecutions = false)]
    private async Task Generate() {
        try {
            var sb = new StringBuilder();
            var hasError = false;
            var generatorOptions = new GeneratorOptions() {
                EdmxFilePath = SelectedEdmxFile.Path,
                ProjectBasePath = TargetProjectPath,
                CompactRules = CompactRules,
                IncludeUnknowns = IncludeUnknowns,
                NoPluralize = NoPluralize,
                UseDatabaseNames = UseDatabaseNames
            };
            var generator = new RuleGenerator(generatorOptions);
            generator.OnLog += GeneratorOnLog;
            var response = await generator.TryGenerateRulesAsync().ConfigureAwait(true);

            var rule = response.DbContextRule;
            if (rule?.Schemas?.Count > 0) {
                // rule generated.
                var saveResponse = await generator.TrySaveRules(response.Rules, TargetProjectPath, null);
                if (saveResponse.Errors.Any()) {
                    MessageBox.Show(saveResponse.Errors.Join(Environment.NewLine), "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
                } else {
                    if (onGenerated == null) {
                        MessageBox.Show(sb.ToString(), "Completed successfully", MessageBoxButton.OK, MessageBoxImage.Information);
                    } else {
                        onGenerated?.Invoke(saveResponse);
                    }
                }
            } else {
                MessageBox.Show(sb.ToString(), "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
            generator.OnLog -= GeneratorOnLog;
            void GeneratorOnLog(object sender, Common.LogMessage logMessage) {
                if (!hasError && logMessage.Type == Common.LogType.Error) hasError = true;
                sb.AppendLine(logMessage.ToString());
            }
        } catch (Exception ex) {
            MessageBox.Show(ex.Message, "Something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}