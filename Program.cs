using CollapseLauncher;
using Hi3Helper.Http;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApplyUpdate
{
    internal class Program
    {
        public static int PreferredCDNIndex = 0;

        public static string realExecPath = Process.GetCurrentProcess().MainModule.FileName;
        public static string realExecDir { get => Path.GetDirectoryName(realExecPath); }
        public static string workingDir { get => realExecDir; }
        public static string execPath = Path.Combine(workingDir, applyExec + ".exe");
        public static string tempDir = Path.Combine(workingDir, "_Temp");
        public static string sourcePath = Path.Combine(workingDir, Path.GetFileName(execPath));
        public const string applyExec = "ApplyUpdate";
        public static string launcherPath = Path.Combine(workingDir, "CollapseLauncher.exe");
        public static readonly string[] excludeDeleteFile = new string[]
        {
            // Generic Files
#if DEBUG
            "ApplyUpdate.",
#else
            "ApplyUpdate.exe",
#endif
            "config.ini",
            "release",
            "CollapseLauncher.Hi3CacheUpdater.cmd",
            "_Temp",
            "unins00",
            "unins000",

            // Game Files (we added this in-case the user uses the same directory as the executable)
            "Hi3SEA", "Hi3Global", "Hi3TW", "Hi3KR", "Hi3CN", "Hi3JP", "BH3",
            "GIGlb", "GICN", "GenshinImpact", "YuanShen",
            "SRGlb", "SRCN", "StarRail",
            "ZZZGlb", "ZZZCN", "ZZZ"
#if DEBUG
            // Hi3Helper.Http DLLs
            , "Hi3Helper.Http"
#endif
        };

        private static int CompressMode(params string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Please define Input and Output file path");
                return 1;
            }

            if (!File.Exists(args[1]))
            {
                Console.WriteLine("Input file doesn't exist!");
                Console.WriteLine("Path: " + args[1]);
                return 2;
            }

            if (!Directory.Exists(Path.GetDirectoryName(args[2])))
            {
                Console.WriteLine("Output directory from given path doesn't exist!");
                Console.WriteLine("Path: " + args[2]);
                return 2;
            }

            using (FileStream fsi = new FileStream(args[1], FileMode.Open, FileAccess.Read))
            using (FileStream fso = new FileStream(args[2], FileMode.Create, FileAccess.Write))
            using (BrotliStream bso = new BrotliStream(fso, CompressionLevel.SmallestSize))
            {
                Console.WriteLine("Input path: " + args[1]);
                Console.WriteLine("Output path: " + args[2]);
                Span<byte> buffer = stackalloc byte[4 << 14];
                Console.WriteLine("Input filesize: " + fsi.Length + " bytes");

                int read = 0;
                long curRead = 0;
                long length = fsi.Length;
                while ((read = fsi.Read(buffer)) > 0)
                {
                    curRead += read;
                    Console.Write($"\rCompressing: {Math.Round(((double)curRead / length) * 100, 4)}%...");
                    bso.Write(buffer.Slice(0, read));
                }
                Console.WriteLine(" Completed!");
                Console.WriteLine("Output filesize: " + fso.Length + " bytes");
                Console.WriteLine($"Compression ratio: {Math.Round((double)fso.Length / fsi.Length * 100, 4)}%");
            }

            return 0;
        }

        static async Task Main(params string[] args)
        {
            if (args.Length != 0 && args[0].ToLower() == "compress")
            {
                CompressMode(args);
                return;
            }

            Process proc;
            if (Directory.GetCurrentDirectory().Trim('\\') != realExecDir.Trim('\\'))
            {
                Console.WriteLine($"Moving to the right working directory ({realExecDir})...");
                Directory.SetCurrentDirectory(realExecDir);
            }

            string zipPath = Path.Combine(tempDir, "latest");
            string zipExtractPath = Path.Combine(tempDir, "_Extract");
            int count = 5;

            Console.WriteLine($"Current working directory: {Directory.GetCurrentDirectory()}");

            int cw = 1;
            while (IsCollapseRunning())
            {
                Console.WriteLine(GetBothAlignedString("Waiting for Collapse Launcher to close...", $"[Attempt: {cw}]"));
                Thread.Sleep(1000);
                cw++;
            }

            Console.Write("Getting Collapse release channel information... ");

            TryDoPathAction(zipExtractPath, PathAction.delete, PathType.directory);
            TryDoPathAction(tempDir, PathAction.create, PathType.directory);

            try
            {
                string stamp = DetermineReleaseChannel();
                if (stamp == null)
                {
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadLine();
                    return;
                }

                Console.WriteLine($"Found! (using: {stamp} channel)");
                if (!await SelectPreferredCDN()) return;

                CleanupOldFiles();

                if (!File.Exists(zipPath))
                {
                    string packageURL = FallbackCDNUtil.CombineURLFromString("squirrel", stamp, "latest");
                    using (Http httpClient = new Http(true))
                    {
                        Console.Write($"Initializing package download... ");
                        FallbackCDNUtil.DownloadProgress += (_, progress) =>
                        {
                            string print = GetBothAlignedString($"Downloading package: {Math.Round(progress.ProgressPercentage, 2)}% [{SummarizeSizeSimple(progress.Speed)}/s]...", $"[{SummarizeSizeSimple(progress.SizeDownloaded)} / {SummarizeSizeSimple(progress.SizeToBeDownloaded)}]");
                            Console.Write($"\r{print}");
                        };
                        await FallbackCDNUtil.DownloadCDNFallbackContent(httpClient, zipPath, (byte)(Environment.ProcessorCount > 8 ? 8 : Environment.ProcessorCount), packageURL, default);
                        Console.WriteLine();
                    }
                }

                // Extract the file
                ExtractPackage(zipPath, zipExtractPath);

                while (true)
                {
                    // Remove old folders
                    try
                    {
                        foreach (string oldPath in Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly))
                        {
                            TryDoPathAction(oldPath, PathAction.delete, PathType.directory);
                        }
                        TryDoPathAction(Path.Combine(workingDir, "packages"), PathAction.delete, PathType.directory);

                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed while removing old folder!\r\n{ex}");
                        Console.WriteLine("Retrying...");
                        Thread.Sleep(1000);
                    }
                }

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
                }

                // Launch Collapse
                Console.WriteLine($"Calling Launcher: {launcherPath}...");
                while (count > 0)
                {
                    Console.Write($"\rLaunching Collapse in {count}... ");
                    Thread.Sleep(1000);
                    count--;
                }
                proc = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = launcherPath
                    }
                };

                // Start Collapse
                proc.Start();

                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"A fatal error has occurred while running the installer!\r\n{ex}");
                Console.WriteLine("Press any key to exist...");
                Console.ReadLine();
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        private static async ValueTask<bool> SelectPreferredCDN()
        {
            Console.Write("Select your preferred CDN:");
            int i = 1;
            Console.WriteLine(string.Join(',', FallbackCDNUtil.CDNList.Select(x => $" {i++}) " + x.Name)));

            CancellationTokenSource selectToken = new CancellationTokenSource();
            try
            {
                Task.Run(() => GetSelectPreferredCDNValue(selectToken));
                int count = 10;
                while (count > 0)
                {
                    Console.Write($"\rSelect in {count--}... (Default: {PreferredCDNIndex + 1}: ({FallbackCDNUtil.CDNList[PreferredCDNIndex].Name}))> ");
                    await Task.Delay(1000, selectToken.Token);
                }
            }
            catch { }
            selectToken.Cancel();
            Console.WriteLine($"\r\nSelected {PreferredCDNIndex + 1}: {FallbackCDNUtil.CDNList[PreferredCDNIndex].Name}");

            return true;
        }

        private static void GetSelectPreferredCDNValue(CancellationTokenSource token)
        {
            while (true)
            {
                string selectionStr = Console.ReadLine();
                if (string.IsNullOrEmpty(selectionStr))
                {
                    token.Cancel();
                    Console.CursorTop--;
                    return;
                }
                if (!int.TryParse(selectionStr, out int selection) || selection > FallbackCDNUtil.CDNList.Count || selection < 0)
                {
                    Console.WriteLine("Input is invalid!");
                    continue;
                }

                if (token.IsCancellationRequested) return;
                token.Cancel();
                PreferredCDNIndex = selection - 1;
                Console.CursorTop--;
            }
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

        private static void TryCreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                FileInfo fileInfo = new FileInfo(path);
                fileInfo.IsReadOnly = false;
                fileInfo.Delete();
            }
        }

        private static void TryDoPathAction(string path, PathAction action, PathType type)
        {
            bool isFile = type == PathType.file;

            if (isFile && action == PathAction.create) throw new ArgumentException("File type doesn't support create action!", "action");

            try
            {
                if (isFile)
                {
                    TryDeleteFile(path);
                    return;
                }

                switch (action)
                {
                    case PathAction.create:
                        TryCreateDirectory(path);
                        break;
                    case PathAction.delete:
                        TryDeleteDirectory(path);
                        break;
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed while trying to {action} a {type}: {path} -> {ex.Message}", ex);
            }
        }

        enum PathAction
        {
            create,
            delete
        }

        enum PathType
        {
            file,
            directory
        }

        enum StringAlign
        {
            left, center, right
        }

        private static bool _isConsoleWindowHasWidth = IsConsoleWindowHasWidth();
        private static int _windowBufferWidth { get => _isConsoleWindowHasWidth ? Console.WindowWidth : 0; }

        private static bool IsConsoleWindowHasWidth()
        {
            try
            {
                _ = Console.WindowWidth;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetBothAlignedString(string leftString, string rightString)
        {
            if (_windowBufferWidth == 0)
            {
                return leftString + ' ' + rightString;
            }

            int spaceWidth = _windowBufferWidth - 1 - (leftString.Length + rightString.Length);
            return leftString + new string(' ', spaceWidth) + rightString;
        }

        private static StringBuilder _globalStrBuilder = new StringBuilder();

        private static void PrintAlignedString(string inputString, StringAlign align, bool isNewLine = false, bool isFlushCurrentLine = false)
        {
            int spaceLength = align switch
            {
                StringAlign.center => (int)(((double)_windowBufferWidth / 2) - ((double)inputString.Length / 2)),
                StringAlign.right => _windowBufferWidth - inputString.Length,
                _ => 0
            };

            if (isFlushCurrentLine)
            {
                Console.Write('\r' + new string(' ', spaceLength) + '\r');
            }
            else
            {
                if (_isConsoleWindowHasWidth) Console.CursorLeft = spaceLength;
            }

            if (isNewLine)
            {
                Console.WriteLine(inputString);
            }
            else
            {
                Console.Write(inputString);
            }
        }

        private static bool IsCollapseRunning() => Process.GetProcessesByName("CollapseLauncher").Length != 0;

        private static void MoveExtractedPackage(string source, string destination)
        {
            int offset = source.Length + 1;
            string[] files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);

            int i = 1;
            foreach (string sourceFile in files)
            {
                string baseName = sourceFile.AsSpan().Slice(offset).ToString();
                string destPath = Path.Combine(destination, baseName);
                string destDir = Path.GetDirectoryName(destPath);

                TryDoPathAction(destDir, PathAction.create, PathType.directory);

                try
                {
                    Console.Write('\r' + GetBothAlignedString($"Moving: {baseName}...", $"[{i++} / {files.Length}]"));
                    File.Move(sourceFile, destPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Failed! Copying it instead!\r\n{ex}");
                    try
                    {
                        Console.Write('\r' + GetBothAlignedString($"Copying: {baseName}...", $"[{i++} / {files.Length}]"));
                        File.Copy(sourceFile, destPath, true);
                        Console.WriteLine();
                    }
                    catch
                    {
                        Console.WriteLine($"still Failed!\r\n{ex}");
                        throw;
                    }
                    return;
                }
            }
            Console.WriteLine();
        }

        private static void CleanupOldFiles()
        {
            // This is a switch to determine that this file only deletes everything if
            // the current or old installation exist
            string switchPath1 = Path.Combine(workingDir, "CollapseLauncher.exe");
            string switchPath2 = Path.Combine(workingDir, "CollapseLauncher.dll");
            string switchPath3 = Path.Combine(workingDir, "Hi3Helper.Core.dll");
            string switchPath4 = Path.Combine(workingDir, "Hi3Helper.Http.dll");
            string switchPath5 = Path.Combine(workingDir, "Hi3Helper.EncTool.dll");
            if (!(File.Exists(switchPath1) && File.Exists(switchPath2) && File.Exists(switchPath3)
               && File.Exists(switchPath4) && File.Exists(switchPath5)))
            {
                return;
            }

            FileVersionInfo execInfo = FileVersionInfo.GetVersionInfo(switchPath1);
            string version = execInfo.FileVersion;

            Console.WriteLine($"\r\nYou have Collapse v{version} installed!");
            Console.WriteLine($"Collapse needs to clean-up all the old files before continuing the process.");
            Console.WriteLine($"Please make sure that you don't have other files unrelated with Collapse inside the app folder,\r\nEXCEPT the Game Files and Collapse App files.");
            Console.WriteLine($"If yes, then you MUST move it to another location or Collapse might accidentally DELETES IT!");
            Console.Write($"\r\nPress the Enter key to continue the clean-up process or CTRL+C to quit...");
            Console.ReadLine();
            Console.Write($"Please press the Enter key to confirm once again...");
            Console.ReadLine();

            int count = 5;
            while (count > 0)
            {
                Console.Write($"\rClean-up process will be started in {count}...");
                Thread.Sleep(1000);
                count--;
            }
            Console.WriteLine();

            int offset = workingDir.Length + 1;
            int i = 1;
            foreach (string path in Directory.EnumerateDirectories(workingDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        Console.Write('\r' + GetBothAlignedString($"Deleting folder: {path.AsSpan().Slice(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.directory);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Error!\r\n{ex}");
                    }
                }
            }

            foreach (string path in Directory.EnumerateFiles(workingDir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        Console.Write('\r' + GetBothAlignedString($"Deleting folder: {path.AsSpan().Slice(offset)}...", $"[{i++} / ?]"));
                        TryDoPathAction(path, PathAction.delete, PathType.file);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Error!\r\n{ex}");
                    }
                }
            }

            string applyUpdateConfig = Path.Combine(workingDir, "ApplyUpdate.exe.config");
            if (File.Exists(applyUpdateConfig))
            {
                try
                {
                    Console.Write('\r' + GetBothAlignedString($"Deleting folder: {applyUpdateConfig.AsSpan().Slice(offset)}...", $"[{i++} / ?]"));
                    TryDoPathAction(applyUpdateConfig, PathAction.delete, PathType.file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Error!\r\n{ex}");
                }
            }
            Console.WriteLine();
        }

        private static bool IsExceptedFiles(string path) => excludeDeleteFile.Any(x => path.Contains(x));

        private static string DetermineReleaseChannel()
        {
            string stampFilePath = Path.Combine(tempDir, "release");
            string stampFilePath2 = Path.Combine(workingDir, "release");

            string stampFile = File.Exists(stampFilePath) ? stampFilePath : stampFilePath2;

            if (!File.Exists(stampFile))
            {
                Console.WriteLine("Failed!");
                Console.WriteLine($"\"release\" file doesn't exist in \"_Temp\\release\" or current directory");

                return null;
            }

            string[] lines = File.ReadAllLines(stampFile);
            if (!(lines[0].ToLower() == "stable" || lines[0].ToLower() == "preview"))
            {
                Console.WriteLine($"Content of the \"release\" stamp file is not valid. The content of the file must be \"preview\" or \"stable\"");

                return null;
            }

            return lines[0];
        }

        private static void ExtractPackage(string packageFile, string outputFolder)
        {
            using (FileStream fs = new FileStream(packageFile, FileMode.Open, FileAccess.Read, FileShare.None, 4 << 10, FileOptions.DeleteOnClose))
            using (Stream ms = new BrotliStream(fs, CompressionMode.Decompress))
            using (TarReader tar = new TarReader(ms))
            {
                int i = 1;
                while (tar.GetNextEntry() is TarEntry entry)
                {
                    Console.Write('\r' + GetBothAlignedString($"Extracting: {entry.Name}...", $"[{i++} / ?]"));
                    CreateFileFromStream(outputFolder, entry);
                }
                tar.Dispose();
            }
            Console.WriteLine();
        }

        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        private static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), SizeSuffixes[mag]);
        }

        private static void CreateFileFromStream(string outputDir, TarEntry entry)
        {
            string pathTo = Path.Combine(outputDir, entry.Name);
            string pathDir = Path.GetDirectoryName(pathTo);
            TryDoPathAction(pathDir, PathAction.create, PathType.directory);

            Stream stream = entry.DataStream;
            if (stream != null)
            {
                using (Stream fs = new FileStream(pathTo, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fs);
                }
            }
        }
    }
}
