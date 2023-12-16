using CollapseLauncher;
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
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Interop;
using static ApplyUpdateGUI.UpdateTask;

namespace ApplyUpdateGUI
{
    public struct CDNURLProperty
    {
        public string URLPrefix { get; set; }
        public string Name { get; set; }
        public bool PartialDownloadSupport { get; set; }
    }

    public class AppUpdateVersionProp
    {
        public string ver { get; set; }
        public long time { get; set; }
        public List<AppUpdateVersionFileProp> f { get; set; }
    }

    public class AppUpdateVersionFileProp
    {
        public string p { get; set; }
        public string crc { get; set; }
        public long s { get; set; }
    }



    public struct GameVersion
    {
        public GameVersion(int major, int minor, int build, int revision = 0)
        {
            Major = major;
            Minor = minor;
            Build = build;
            Revision = revision;
        }

        public GameVersion(int[] ver)
        {
            if (!(ver.Length == 3 || ver.Length == 4))
            {
                throw new ArgumentException($"Version array entered should have length of 3 or 4!");
            }

            Major = ver[0];
            Minor = ver[1];
            Build = ver[2];
            Revision = 0;
            if (ver.Length == 4)
            {
                Revision = ver[3];
            }
        }

        public GameVersion(Version version)
        {
            Major = version.Major;
            Minor = version.Minor;
            Build = version.Build;
            Revision = 0;
        }

        public GameVersion(string version)
        {
            string[] ver = version.Split(new char[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (!(ver.Length == 3 || ver.Length == 4))
            {
                throw new ArgumentException($"Version in the config.ini should be in \"x.x.x\" or \"x.x.x.x\" format! (current value: \"{version}\")");
            }

            Revision = 0;
            if (!int.TryParse(ver[0], out Major)) throw new ArgumentException($"Major version is not a number! (current value: {ver[0]}");
            if (!int.TryParse(ver[1], out Minor)) throw new ArgumentException($"Minor version is not a number! (current value: {ver[1]}");
            if (!int.TryParse(ver[2], out Build)) throw new ArgumentException($"Build version is not a number! (current value: {ver[2]}");
            if (ver.Length == 4)
            {
                if (!int.TryParse(ver[3], out Revision)) throw new ArgumentException($"Revision version is not a number! (current value: {ver[3]}");
            }
        }

        public bool IsMatch(string versionToCompare)
        {
            GameVersion parsed = new GameVersion(versionToCompare);
            return IsMatch(parsed);
        }

        public bool IsMatch(GameVersion versionToCompare) => Major == versionToCompare.Major && Minor == versionToCompare.Minor && Build == versionToCompare.Build && Revision == versionToCompare.Revision;

        public GameVersion GetIncrementedVersion()
        {
            int NextMajor = Major;
            int NextMinor = Minor;

            NextMinor++;
            if (NextMinor >= 10)
            {
                NextMinor = 0;
                NextMajor++;
            }

            return new GameVersion(new int[] { NextMajor, NextMinor, Build, Revision });
        }

        public Version ToVersion() => new Version(Major, Minor, Build, Revision);
        public override string ToString() => $"{Major}.{Minor}.{Build}";

        public string VersionStringManifest { get => string.Join(".", VersionArrayManifest); }
        public string VersionString { get => string.Join(".", VersionArray); }
        public int[] VersionArrayManifest { get => new int[4] { Major, Minor, Build, Revision }; }
        public int[] VersionArray { get => new int[3] { Major, Minor, Build }; }
        public readonly int Major;
        public readonly int Minor;
        public readonly int Build;
        public readonly int Revision;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        static readonly int GWL_STYLE = -16;

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

        public MainWindow()
        {
            DataContext = this;
            InitializeComponent();

            Loaded += (sender, args) =>
            {
                // Theme.Apply(ThemeType.Light, IsWindows11() ? BackgroundType.Mica : BackgroundType.Acrylic);
                ApplicationThemeManager.Apply(ApplicationTheme.Light, IsWindows11() ? WindowBackdropType.Mica : WindowBackdropType.None, true);

                if (!IsWindows11())
                {
                    // this.AllowsTransparency = false;
                    this.Background = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                }

                uint windowLong = GetWindowLong(new WindowInteropHelper(this).Handle, GWL_STYLE);
                windowLong &= ~(uint)0x00080000;

                SetWindowLong(new WindowInteropHelper(this).Handle, GWL_STYLE, windowLong);
                UnsafeNativeMethods.ApplyWindowCornerPreference(new WindowInteropHelper(this).Handle, WindowCornerPreference.Round);

                UpdateCDNSelectorSubtitle.Text = string.Empty;

                RunCountdownTask();
            };
        }

        private CancellationTokenSource CountDownToken { get; set; }
        private string Stamp { get; set; }
        private bool IsStampFromFile { get; set; }

        private async Task<string> RunPrecheckTask()
        {
            if (Directory.GetCurrentDirectory().Trim('\\') != realExecDir.Trim('\\'))
            {
                Directory.SetCurrentDirectory(realExecDir);
            }

            string previousTitleStr = UpdateCDNSelectorTitle.Text;

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
                stamp = DetermineReleaseChannel();
                if (stamp == null)
                {
                    UpdateCDNRadioButtons.Visibility = Visibility.Collapsed;
                    UpdateCDNSelectorTitle.Text = "ERROR:\r\n\"release\" file doesn't have \"stable\" or \"preview\" string in it";
                    UpdateCDNSelectorSubtitle.Text = "Please check your \"release\" file and try again.";
                    return null;
                }
            }
            else
            {
                string selectedValue = ((ComboBoxItem)UpdateReleaseSelectorBox.SelectedValue).Content as string;
                stamp = selectedValue.ToLower();
                return stamp;
            }

            return stamp;
        }

        private void UpdateCDNComboBoxesCancelCountdown(object sender, MouseButtonEventArgs e)
        {
            CountDownToken.Cancel();
            UpdateCDNSelectorSubtitle.Text = "Select the CDN options and click \"Update Now!\" to start the update!";
        }

        private async void RunCountdownTask()
        {
            CountDownToken = new CancellationTokenSource();
            UpdateCDNComboBox.PreviewMouseDown += UpdateCDNComboBoxesCancelCountdown;
            UpdateReleaseSelectorBox.PreviewMouseDown += UpdateCDNComboBoxesCancelCountdown;
            int countdown = 5;
            const string CDNSelectorSubtitle = "The CDN will be automatically selected in: {0}";

            string releaseFile = TryGetStampFilePath();
            if (!(IsStampFromFile = File.Exists(releaseFile)))
            {
                UpdateReleaseSelectorBox.Visibility = Visibility.Visible;
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
            string rdName = (string)rdBtn.Content;
            FallbackCDNUtil.PreferredCDNIndex = FallbackCDNUtil.CDNList.FindIndex(x => x.Name == rdName);
            if (FallbackCDNUtil.PreferredCDNIndex < 0)
            {
                FallbackCDNUtil.PreferredCDNIndex = 0;
                return;
            }
        }

        private void DragEntireWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        public static bool IsWindows11()
        {
            OperatingSystem osDetail = Environment.OSVersion;
            ushort[] w_windowsVersionNumbers = osDetail.Version.ToString().Split('.').Select(ushort.Parse).ToArray();
            return w_windowsVersionNumbers[2] >= 22000;
        }

        private void UpdateCDNComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
            => FallbackCDNUtil.PreferredCDNIndex = ((ComboBox)sender).SelectedIndex;

        private async void StartUpdateRoutine(object sender, RoutedEventArgs e)
        {
            CountDownToken.Cancel();
            await InnerUpdateRoutine();
        }

        private async Task InnerUpdateRoutine()
        {
            Console.Write("Getting Collapse release channel information... ");
            if ((Stamp = await RunPrecheckTask()) == null) return;
            Console.WriteLine($"Found! (using: {Stamp} channel)");

            EventProgress += UpdateTask_UpdateProgress;
            EventStatus += UpdateTask_UpdateStatus;

            try
            {
                UpdateCDNSelectorPanel.Visibility = Visibility.Collapsed;
                UpdateProgressPanel.Visibility = Visibility.Visible;

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
                MainEntry.m_App.Shutdown();

                return;
            }
            catch (Exception ex)
            {
                SpawnError($"ERROR:\r\nError occured while applying update!\r\n{ex.Message}");
                Console.WriteLine($"Error occured while applying update!\r\n{ex}");
            }
            finally
            {
                EventProgress -= UpdateTask_UpdateProgress;
                EventStatus -= UpdateTask_UpdateStatus;
            }
        }

        private void UpdateInnoSetupLog(string path)
        {
            string directoryPath = Path.GetDirectoryName(path);
            string searchValue = GetPathWithoutDriveLetter(directoryPath);

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
                innoLog.Records.Add(DeleteFileRecord.Create(fileInfo.FullName));
            }

            foreach (DirectoryInfo subdirectories in currentDirectory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
            {
                RegisterDirOrFilesRecord(innoLog, subdirectories.FullName);
            }
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
                        break;
                    case RecordType.DeleteFile:
                        isRecordValid = IsInnoRecordContainsPath<DeleteFileFlags>(baseRecord, searchValue)
                                     && IsDeleteFileFlagsValid((DeleteFileRecord)baseRecord);
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
            UpdateCDNSelectorSubtitle.Visibility = Visibility.Collapsed;
            UpdateCDNProgressPanel.Visibility = Visibility.Visible;
            UpdateCDNSelectorPanel.Visibility = Visibility.Collapsed;
            UpdateCDNRadioButtons.Visibility = Visibility.Visible;
        }


        private void SpawnError(string message)
        {
            MainEntry.ShowWindow(MainEntry.consoleWinPtr, 5);
            UpdateCDNProgressPanel.Visibility = Visibility.Collapsed;
            UpdateCDNSelectorPanel.Visibility = Visibility.Visible;
            UpdateCDNRadioButtons.Visibility = Visibility.Collapsed;
            UpdateCDNSelectorSubtitle.Visibility = Visibility.Collapsed;
            UpdateCDNSelectorTitle.Text = message;
        }

        private GameVersion? GetOldVersion()
        {
            Version maxVersion = Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly)
                ?.Max(x =>
                {
                    try
                    {
                        string[] verSplit = x.Split('-');
                        if (verSplit.Length == 2)
                        {
                            return new GameVersion(verSplit[1]).ToVersion();
                        }
                        return new GameVersion(new int[3] { 0, 0, 0 }).ToVersion();
                    }
                    catch { return new GameVersion(new int[3] { 0, 0, 0 }).ToVersion(); }
                });

            string legacyPathDll = Path.Combine(Path.GetDirectoryName(launcherPath), "CollapseLauncher.dll");
            if (File.Exists(legacyPathDll))
            {
                FileVersionInfo execInfo = FileVersionInfo.GetVersionInfo(legacyPathDll);
                string version = execInfo.FileVersion;

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
                AppUpdateVersionProp updateInfo = JsonSerializer.Deserialize<AppUpdateVersionProp>(ms);
                return updateInfo;
            }
        }

        private async void UpdateTask_UpdateStatus(object sender, UpdateStatus e)
        {
            if (await CheckIfNeedRefreshStopwatchStatus())
            {
                Dispatcher.Invoke(() =>
                {
                    // throw new NotImplementedException();
                    // Status.Text = e.Status;
                    ActivityStatus.Text = e.ActivityStatus;
                    ActivitySubStatus.Text = e.ActivitySubStatus;
                    progressBar.IsIndeterminate = e.IsProgressIndetermined;
                });
            }
        }

        private async void UpdateTask_UpdateProgress(object sender, UpdateProgress e)
        {
            if (await CheckIfNeedRefreshStopwatchProgress())
            {
                Dispatcher.Invoke(() =>
                {
                    progressBar.Value = e.ProgressPercentage;
                    SpeedStatus.Text = string.Format("{0}/s", SummarizeSizeSimple(e.Speed));
                    TimeEstimation.Text = string.Format("{0:%h}h{0:%m}m{0:%s}s left", e.TimeLeft);
                });
            }
        }

        private Stopwatch _refreshStopwatchProgress = Stopwatch.StartNew();
        private Stopwatch _refreshStopwatchStatus = Stopwatch.StartNew();
        private int _refreshInterval = 66;
        protected async Task<bool> CheckIfNeedRefreshStopwatchProgress()
        {
            if (_refreshStopwatchProgress.ElapsedMilliseconds > _refreshInterval)
            {
                _refreshStopwatchProgress.Restart();
                return true;
            }

            await Task.Delay(_refreshInterval);
            return false;
        }
        protected async Task<bool> CheckIfNeedRefreshStopwatchStatus()
        {
            if (_refreshStopwatchStatus.ElapsedMilliseconds > _refreshInterval)
            {
                _refreshStopwatchStatus.Restart();
                return true;
            }

            await Task.Delay(_refreshInterval);
            return false;
        }
    }
}
