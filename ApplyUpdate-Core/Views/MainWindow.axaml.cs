using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hi3Helper.EncTool.Parser.InnoUninstallerLog;
using Hi3Helper.Http;
using LibISULR;
using LibISULR.Flags;
using LibISULR.Records;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using static ApplyUpdate.UpdateTask;

namespace ApplyUpdate;

[JsonSerializable(typeof(AppUpdateVersionProp), GenerationMode = JsonSourceGenerationMode.Metadata)]
public partial class JSONContext : JsonSerializerContext { }

public partial class MainWindow : Window
{
    private CancellationTokenSource CountDownToken { get; set; }
    private string Stamp { get; set; }
    private bool IsStampFromFile { get; set; }

    public MainWindow()
    {
        this.Background = null;
        this.TransparencyLevelHint = new List<WindowTransparencyLevel>
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Transparent,
            WindowTransparencyLevel.None
        };
        InitializeComponent();
        PointerPressed += (obj, args) =>
        {
            if (WindowState == WindowState.Normal
            && args.Pointer.Captured.GetType() == typeof(Avalonia.Controls.Panel))
                BeginMoveDrag(args);
        };

        UpdateCDNSelectorSubtitle.Text = string.Empty;
        UpdateCDNComboBox.ItemsSource = FallbackCDNUtil.CDNList.Select(x => x.Name).ToList();
        UpdateCDNComboBox.SelectedIndex = FallbackCDNUtil.PreferredCDNIndex;
        UpdateCDNComboBox.SelectionChanged += UpdateCDNComboBox_SelectionChanged;
        RunCountdownTask();
    }

    private void UpdateCDNComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FallbackCDNUtil.PreferredCDNIndex = (sender as ComboBox).SelectedIndex;
    }

    private async Task<string> RunPrecheckTask()
    {
        if (Directory.GetCurrentDirectory().Trim('\\') != realExecDir.Trim('\\'))
        {
            Directory.SetCurrentDirectory(realExecDir);
        }

        string previousTitleStr = UpdateCDNSelectorTitle.Text!;

        while (IsCollapseRunning())
        {
            UpdateCDNSelectorTitle.Text = "Please close Collapse before applying the update!";
            UpdateCDNSelectorSubtitle.Text = "Waiting for Collapse to close...";
            UpdateCDNRadioButtons.IsEnabled = false;
            await Task.Delay(100);
        }

        UpdateCDNRadioButtons.IsEnabled = true;
        UpdateCDNSelectorTitle.Text = previousTitleStr;

        string stamp;
        if (IsStampFromFile)
        {
            stamp = DetermineReleaseChannel()!;
            if (stamp == null)
            {
                UpdateCDNRadioButtons.IsVisible = false;
                UpdateCDNSelectorTitle.Text = "ERROR:\r\n\"release\" file doesn't have \"stable\" or \"preview\" string in it";
                UpdateCDNSelectorSubtitle.Text = "Please check your \"release\" file and try again.";
                return null;
            }
        }
        else
        {
            string selectedValue = (((ComboBoxItem)UpdateReleaseSelectorBox.SelectedValue!).Content as string)!;
            stamp = selectedValue.ToLower();
            return stamp;
        }

        return stamp;
    }

    private void UpdateCDNComboBoxesCancelCountdown(object sender, PointerEventArgs e)
    {
        CountDownToken?.Cancel();
        UpdateCDNSelectorSubtitle.Text = "Select the CDN options and click \"Update Now!\" to start the update!";
    }

    private async void RunCountdownTask()
    {
        CountDownToken = new CancellationTokenSource();
        UpdateCDNComboBox.PointerEntered += UpdateCDNComboBoxesCancelCountdown;
        UpdateReleaseSelectorBox.PointerEntered += UpdateCDNComboBoxesCancelCountdown;
        int countdown = 5;
        const string CDNSelectorSubtitle = "The CDN will be automatically selected in: {0}";

        string releaseFile = TryGetStampFilePath();
        if (!(IsStampFromFile = File.Exists(releaseFile)))
        {
            UpdateReleaseSelectorBox.IsVisible = true;
            countdown += 5;
        }

        try
        {
            while (countdown > 0)
            {
                if (CountDownToken.Token.IsCancellationRequested) return;

                UpdateCDNSelectorSubtitle.Text = string.Format(CDNSelectorSubtitle, countdown--);
                await Task.Delay(1000, CountDownToken.Token);
            }
        }
        catch { return; }

        await InnerUpdateRoutine();
    }

    private void CDNRadioButtonSelected(object sender, RoutedEventArgs e)
    {
        RadioButton rdBtn = (RadioButton)sender;
        string rdName = (string)rdBtn.Content!;
        FallbackCDNUtil.PreferredCDNIndex = FallbackCDNUtil.CDNList.FindIndex(x => x.Name == rdName);
        if (FallbackCDNUtil.PreferredCDNIndex < 0)
        {
            FallbackCDNUtil.PreferredCDNIndex = 0;
            return;
        }
    }

    /*
    private void DragEntireWindow(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            this.DragMove();
    }
    */

    private void UpdateCDNComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        => FallbackCDNUtil.PreferredCDNIndex = ((ComboBox)sender).SelectedIndex;

    private async void StartUpdateRoutine(object sender, RoutedEventArgs e)
    {
        CountDownToken?.Cancel();
        await InnerUpdateRoutine();
    }

    private async Task InnerUpdateRoutine()
    {
        Console.Write("Getting Collapse release channel information... ");
        if ((Stamp = await RunPrecheckTask()) == null) return;
        Console.WriteLine($"Found! (using: {Stamp} channel)");

        EventProgress += UpdateTask_UpdateProgress!;
        EventStatus += UpdateTask_UpdateStatus!;

        try
        {
            UpdateCDNSelectorPanel.IsVisible = false;
            UpdateProgressPanel.IsVisible = true;

            AppUpdateVersionProp metadataProp = await FetchMetadata(Stamp);
            GameVersion newVersion = new GameVersion(metadataProp.ver);
            GameVersion? oldVersion = GetOldVersion();

            UpdateVersionOld.Text = oldVersion.HasValue ? oldVersion.Value.VersionString : "none";
            UpdateVersionNew.Text = newVersion.VersionString;
            ChannelName.Text = char.ToUpper(Stamp[0]) + Stamp.Substring(1);

            if (!await CleanupOldFiles()) Environment.Exit(int.MinValue);

            TryDoPathAction(zipExtractPath, PathAction.delete, PathType.directory);
            TryDoPathAction(tempDir, PathAction.create, PathType.directory);
            await DownloadPackage(Stamp);

            Status.Text = "Extracting package:";

            await ExtractPackage(zipPath, zipExtractPath);

            while (true)
            {
                // Remove old folders
                try
                {
                    Status.Text = "Removing old package:";
                    foreach (string oldPath in Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly))
                    {
                        ActivityStatus.Text = Path.GetFileName(oldPath);
                        TryDoPathAction(oldPath, PathAction.delete, PathType.directory);
                    }
                    TryDoPathAction(Path.Combine(workingDir, "packages"), PathAction.delete, PathType.directory);

                    break;
                }
                catch (Exception ex)
                {
                    SpawnError($"ERROR:\r\nFailed while removing old folder! Retrying...\r\n{ex.Message}");
                    Console.WriteLine("Retrying...");
                    Thread.Sleep(1000);

                    ReshowProgressPanel();
                }
            }

            Status.Text = "Moving extracted files:";

            // Move the file
            MoveExtractedPackage(zipExtractPath, workingDir);

            // Remove temp folder
            try
            {
                TryDoPathAction(tempDir, PathAction.delete, PathType.directory);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed while deleting temporary folder: tempDir. Skipping!\r\n{ex}");
                throw;
            }

            string innoSetupLogPath = Path.Combine(workingDir, "unins000.dat");
            if (File.Exists(innoSetupLogPath))
            {
                Status.Text = $"Updating uninstaller index file...";
                await Task.Run(() => UpdateInnoSetupLog(innoSetupLogPath));
            }

            Status.Text = $"Launcher has been updated to: {newVersion.VersionString}!";

            int count = 5;
            // Launch Collapse
            Console.WriteLine($"Calling Launcher: {launcherPath}...");
            while (count > 0)
            {
                ActivityStatus.Text = $"Launching Collapse in {count}...";
                ActivitySubStatus.Text = $"- / -";
                progressBar.Value = 100d;
                progressBar.IsIndeterminate = false;
                Console.Write($"\rLaunching Collapse in {count--}... ");
                await Task.Delay(1000);
            }
            Console.WriteLine();
            Process proc = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = launcherPath
                }
            };

            // Start Collapse
            proc.Start();
            PInvoke.m_window.Shutdown();
            return;
        }
        catch (Exception ex)
        {
            SpawnError($"ERROR:\r\nError occured while applying update!\r\n{ex.Message}");
            Console.WriteLine($"\r\nError occured while applying update!\r\n{ex}");
        }
        finally
        {
            EventProgress -= UpdateTask_UpdateProgress!;
            EventStatus -= UpdateTask_UpdateStatus!;
        }
    }

    private void UpdateInnoSetupLog(string path)
    {
        string directoryPath = Path.GetDirectoryName(path)!;
        string searchValue = GetPathWithoutDriveLetter(directoryPath);

        Console.WriteLine($"Updating Inno Setup file located at: {path}");
        try
        {
            using (InnoUninstLog innoLog = InnoUninstLog.Load(path, true))
            {
                // Always set the log to x64 mode
                innoLog.Header.IsLog64bit = true;

                // Clean up the existing file and directory records
                CleanUpInnoDirOrFilesRecord(innoLog, searchValue);

                // Try register the parent path
                RegisterDirOrFilesRecord(innoLog, directoryPath);

                // Save the Inno Setup log
                innoLog.Save(path);
                Console.WriteLine($"Inno Setup file: {path} has been successfully updated!");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Inno Setup file: {path} was failed due to an error: {ex}");
        }
    }

    private static string GetPathWithoutDriveLetter(string path)
    {
        int firstIndexOf = path.IndexOf('\\');
        return firstIndexOf > -1 ? path.Substring(firstIndexOf + 1) : path;
    }

    private static void RegisterDirOrFilesRecord(InnoUninstLog innoLog, string pathToRegister)
    {
        DirectoryInfo currentDirectory = new DirectoryInfo(pathToRegister);
        foreach (FileInfo fileInfo in currentDirectory.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            if (excludeDeleteFile.Any(x => x.IndexOf(fileInfo.FullName, StringComparison.OrdinalIgnoreCase) > -1)) continue;
            fileInfo.IsReadOnly = false;
            Console.WriteLine($"Registering Inno Setup record: (DeleteFileRecord){fileInfo.FullName}");
            innoLog.Records.Add(DeleteFileRecord.Create(fileInfo.FullName));
        }

        foreach (DirectoryInfo subdirectories in currentDirectory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
        {
            RegisterDirOrFilesRecord(innoLog, subdirectories.FullName);
        }
        Console.WriteLine($"Registering Inno Setup record: (DeleteDirOrFilesRecord){pathToRegister}");
        innoLog.Records.Add(DeleteDirOrFilesRecord.Create(pathToRegister));
    }

    private static void CleanUpInnoDirOrFilesRecord(InnoUninstLog innoLog, string searchValue)
    {
        int index = 0;
        do
        {
            BaseRecord baseRecord = innoLog.Records[index];
            bool isRecordValid = false;
            switch (baseRecord.Type)
            {
                case RecordType.DeleteDirOrFiles:
                    isRecordValid = IsInnoRecordContainsPath<DeleteDirOrFilesFlags>(baseRecord, searchValue)
                                 && IsDeleteDirOrFilesFlagsValid((DeleteDirOrFilesRecord)baseRecord);
                    Console.WriteLine($"Removing outdated Inno Setup record: (DeleteDirOrFilesRecord){((DeleteDirOrFilesRecord)baseRecord).Paths[0]}");
                    break;
                case RecordType.DeleteFile:
                    isRecordValid = IsInnoRecordContainsPath<DeleteFileFlags>(baseRecord, searchValue)
                                 && IsDeleteFileFlagsValid((DeleteFileRecord)baseRecord);
                    Console.WriteLine($"Removing outdated Inno Setup record: (DeleteFileRecord){((DeleteFileRecord)baseRecord).Paths[0]}");
                    break;
            }
            if (isRecordValid)
            {
                innoLog.Records.RemoveAt(index);
                continue;
            }
            ++index;
        } while (index < innoLog.Records.Count);
    }

    private static bool IsDeleteDirOrFilesFlagsValid(DeleteDirOrFilesRecord record) => (record.Flags ^ (DeleteDirOrFilesFlags.IsDir | DeleteDirOrFilesFlags.DisableFsRedir)) == 0;
    private static bool IsDeleteFileFlagsValid(DeleteFileRecord record) => (record.Flags & DeleteFileFlags.DisableFsRedir) != 0;
    private static bool IsInnoRecordContainsPath<TFlags>(BaseRecord record, string searchValue)
        where TFlags : Enum => ((BasePathListRecord<TFlags>)record)
        .Paths[0]
        .IndexOf(searchValue, StringComparison.OrdinalIgnoreCase) > -1;

    private void ReshowProgressPanel()
    {
        UpdateCDNSelectorSubtitle.IsVisible = false;
        UpdateCDNProgressPanel.IsVisible = true;
        UpdateCDNSelectorPanel.IsVisible = false;
        UpdateCDNRadioButtons.IsVisible = true;
        UpdateIndicator.IsVisible = true;
    }

    private void SpawnError(string message)
    {
        PInvoke.ShowWindow(PInvoke.m_consoleWindow, 5);
        UpdateCDNProgressPanel.IsVisible = false;
        UpdateCDNSelectorPanel.IsVisible = true;
        UpdateCDNRadioButtons.IsVisible = false;
        UpdateIndicator.IsVisible = false;
        UpdateCDNSelectorSubtitle.IsVisible = false;
        UpdateCDNSelectorTitle.Text = message;
    }

    private GameVersion? GetOldVersion()
    {
        Version maxVersion = Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly)
            ?.Max(x =>
            {
                int lastIndexOfBs = x.LastIndexOf('-');
                ReadOnlySpan<char> trimmedPath = x.AsSpan(lastIndexOfBs + 1);
                int manyOfDots = trimmedPath.Count('.') + 1;

                if (manyOfDots > 4)
                {
                    Console.WriteLine($"Failed to parse version from path: {x} due to many separator characters in the path. Returning default value!");
                    return new GameVersion(new int[4] { 0, 0, 0, 0 }).ToVersion();
                }

                Span<Range> ranges = stackalloc Range[manyOfDots];
                int manyOfSplits = trimmedPath.Split(ranges, '.', StringSplitOptions.RemoveEmptyEntries);

                Span<int> versionBuffer = stackalloc int[manyOfDots];
                for (int i = 0; i < versionBuffer.Length; i++)
                {
                    if (!int.TryParse(trimmedPath[ranges[i]], null, out versionBuffer[i]))
                    {
                        Console.WriteLine($"Failed to parse version from path: {x} due to invalid number while reading value: \"{trimmedPath[ranges[i]].ToString()}\". Returning default value!");
                        return new GameVersion(new int[4] { 0, 0, 0, 0 }).ToVersion();
                    }
                }

                return new GameVersion(versionBuffer).ToVersion();
            });

        string legacyPathDll = Path.Combine(Path.GetDirectoryName(launcherPath)!, "CollapseLauncher.dll");
        if (File.Exists(legacyPathDll))
        {
            FileVersionInfo execInfo = FileVersionInfo.GetVersionInfo(legacyPathDll);
            string version = execInfo.FileVersion!;

            GameVersion legacyVersion = new GameVersion(version);

            if (maxVersion != null) return maxVersion > legacyVersion.ToVersion() ? new GameVersion(maxVersion) : legacyVersion;
            return new GameVersion(version);
        }

        if (maxVersion != null) return new GameVersion(maxVersion);

        return null;
    }

    private async Task<AppUpdateVersionProp> FetchMetadata(string stamp)
    {
        using (MemoryStream ms = new MemoryStream())
        using (Http _httpClient = new Http())
        {
            await FallbackCDNUtil.DownloadCDNFallbackContent(_httpClient, ms, $"{stamp}/fileindex.json", default);
            ms.Position = 0;
            AppUpdateVersionProp updateInfo = (AppUpdateVersionProp)await JsonSerializer.DeserializeAsync(ms, typeof(AppUpdateVersionProp), JSONContext.Default)!;
            return updateInfo;
        }
    }

    private async void UpdateTask_UpdateStatus(object sender, UpdateStatus e)
    {
        if (await CheckIfNeedRefreshStopwatch(_refreshStopwatchStatus))
        {
            ActivityStatus.Text = e.ActivityStatus;
            ActivitySubStatus.Text = e.ActivitySubStatus;
            progressBar.IsIndeterminate = e.IsProgressIndetermined;
        }
    }

    private async void UpdateTask_UpdateProgress(object sender, UpdateProgress e)
    {
        if (await CheckIfNeedRefreshStopwatch(_refreshStopwatchProgress))
        {
            progressBar.Value = e.ProgressPercentage;
            SpeedStatus.Text = string.Format("{0}/s", SummarizeSizeSimple(e.Speed));
            TimeEstimation.Text = string.Format("{0:%h}h{0:%m}m{0:%s}s left", e.TimeLeft);
        }
    }

    private Stopwatch _refreshStopwatchProgress = Stopwatch.StartNew();
    private Stopwatch _refreshStopwatchStatus = Stopwatch.StartNew();
    private int _refreshInterval = 33;
    protected async ValueTask<bool> CheckIfNeedRefreshStopwatch(Stopwatch sw)
    {
        if (sw.ElapsedMilliseconds > _refreshInterval)
        {
            sw.Restart();
            return true;
        }

        await Task.Delay(_refreshInterval);
        return false;
    }
}
