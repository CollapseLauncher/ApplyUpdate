using Hi3Helper.Http;
using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;

namespace ApplyUpdate
{
    internal class Program
    {
        public const string repoURL = "https://github.com/neon-nyan/CollapseLauncher-ReleaseRepo/raw/main";

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
            "ApplyUpdate.exe",
            "release",
            "CollapseLauncher.Hi3CacheUpdater.cmd",
            "_Temp",

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

        static void Main(string[] args)
        {
            Process proc;
            if (Directory.GetCurrentDirectory().Trim('\\') != realExecDir.Trim('\\'))
            {
                Console.WriteLine($"Moving to the right working directory ({realExecDir})...");
                Directory.SetCurrentDirectory(realExecDir);
            }

            string zipPath = Path.Combine(tempDir, "latest");
            string zipExtractPath = Path.Combine(tempDir, "_Extract");
            int count = 5;

            Console.WriteLine($"Current working directory: {workingDir}");

            int cw = 1;
            while (IsCollapseRunning())
            {
                Console.WriteLine($"Waiting for Collapse Launcher to close... (Attempt: {cw})");
                Thread.Sleep(1000);
                cw++;
            }

            if (args.Length > 0 && args[0] == "reapply")
            {
                while (true)
                {
                    // Remove old folders
                    try
                    {
                        foreach (string oldPath in Directory.EnumerateDirectories(workingDir, "app-*", SearchOption.TopDirectoryOnly))
                        {
                            Directory.Delete(oldPath, true);
                        }

                        string pkgPath = Path.Combine(workingDir, "packages");
                        if (Directory.Exists(pkgPath))
                        {
                            Directory.Delete(pkgPath, true);
                        }

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
                    Directory.Delete(tempDir, true);
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

            Console.Write("Getting Collapse release channel information... ");

            if (Directory.Exists(zipExtractPath))
            {
                Directory.Delete(zipExtractPath, true);
            }

            if (!Directory.Exists(tempDir))
            {
                Directory.CreateDirectory(tempDir);
            }

            string stamp = DetermineReleaseChannel();
            if (stamp == null)
            {
                Console.ReadLine();
                return;
            }

            Console.WriteLine($"Found! (using: {stamp} channel)");

            CleanupOldFiles();

            if (!File.Exists(zipPath))
            {
                string packageURL = CombineURLFromString(repoURL, "squirrel", stamp, "latest");
                using (Http httpClient = new Http(true))
                {
                    Console.Write($"Initializing package download... ");
                    httpClient.DownloadProgress += (_, progress) =>
                    {
                        Console.Write($"\rDownloading package {Math.Round(progress.ProgressPercentage, 2)}% ({SummarizeSizeSimple(progress.Speed)}/s)" +
                            $"\t[{SummarizeSizeSimple(progress.SizeDownloaded)} / {SummarizeSizeSimple(progress.SizeToBeDownloaded)}]...       ");
                    };
                    httpClient.Download(packageURL, zipPath, (byte)(Environment.ProcessorCount > 8 ? 8 : Environment.ProcessorCount), false, default).GetAwaiter().GetResult();
                    httpClient.Merge().GetAwaiter().GetResult();
                    Console.WriteLine("Done!");
                }
            }

            // Extract the file
            ExtractPackage(zipPath, zipExtractPath);

            // Restart ApplyUpdate to apply the update
            proc = new Process()
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = realExecPath,
                    Arguments = "reapply"
                }
            };
            proc.Start();
        }

        private static bool IsCollapseRunning() => Process.GetProcessesByName("CollapseLauncher").Length != 0;

        private static void MoveExtractedPackage(string source, string destination)
        {
            int offset = source.Length + 1;

            foreach (string sourceFile in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                string baseName = sourceFile.AsSpan().Slice(offset).ToString();
                string destPath = Path.Combine(destination, baseName);
                string destDir = Path.GetDirectoryName(destPath);

                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                try
                {
                    Console.Write($"Moving: {baseName}... ");
                    File.Move(sourceFile, destPath, true);
                    Console.WriteLine($"Done!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed! Copying it instead!\r\n{ex}");
                    try
                    {
                        Console.Write($"Copying: {baseName}... ");
                        File.Copy(sourceFile, destPath, true);
                        Console.WriteLine($"Done!");
                    }
                    catch
                    {
                        Console.WriteLine($"still Failed!\r\n{ex}");
                        throw;
                    }
                    return;
                }
            }
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
            foreach (string path in Directory.EnumerateDirectories(workingDir, "*", SearchOption.TopDirectoryOnly))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        Console.Write($"Deleting folder: {path.AsSpan().Slice(offset)}... ");
                        Directory.Delete(path, true);
                        Console.WriteLine("Done!");
                    }
                    catch (Exception ex) { Console.WriteLine($"Error!\r\n{ex}"); }
                }
            }

            foreach (string path in Directory.EnumerateFiles(workingDir, "*.*", SearchOption.AllDirectories))
            {
                if (!IsExceptedFiles(path))
                {
                    try
                    {
                        Console.Write($"Deleting file: {path.AsSpan().Slice(offset)}... ");
                        File.Delete(path);
                        Console.WriteLine("Done!");
                    }
                    catch (Exception ex) { Console.WriteLine($"Error!\r\n{ex}"); }
                }
            }

            string applyUpdateConfig = Path.Combine(workingDir, "ApplyUpdate.exe.config");
            if (File.Exists(applyUpdateConfig))
            {
                try
                {
                    Console.Write($"Deleting file: {applyUpdateConfig.AsSpan().Slice(offset)}... ");
                    File.Delete(applyUpdateConfig);
                    Console.WriteLine("Done!");
                }
                catch (Exception ex) { Console.WriteLine($"Error!\r\n{ex}"); }
            }
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
            using (MemoryStream ms = CopyBrotliToMemoryStream(fs))
            using (TarReader tar = new TarReader(ms))
            {
                while (tar.GetNextEntry() is TarEntry entry)
                {
                    CreateFileFromStream(outputFolder, entry);
                }
            }
        }

        private static readonly string[] SizeSuffixes = { "B", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };
        private static string SummarizeSizeSimple(double value, int decimalPlaces = 2)
        {
            byte mag = (byte)Math.Log(value, 1000);

            return string.Format("{0} {1}", Math.Round(value / (1L << (mag * 10)), decimalPlaces), SizeSuffixes[mag]);
        }

        private static string CombineURLFromString(ReadOnlySpan<char> baseURL, params string[] segments)
        {
            StringBuilder builder = new StringBuilder().Append(baseURL.TrimEnd('/'));

            foreach (ReadOnlySpan<char> a in segments)
            {
                bool isMacros = a.StartsWith("?");
                if (!isMacros)
                {
                    builder.Append('/');
                }
                builder.Append(a.Trim('/'));
            }

            return builder.ToString();
        }

        private static void CreateFileFromStream(string outputDir, TarEntry entry)
        {
            string pathTo = Path.Combine(outputDir, entry.Name);
            string pathDir = Path.GetDirectoryName(pathTo);
            if (!Directory.Exists(pathDir))
            {
                Directory.CreateDirectory(pathDir);
            }

            using (Stream stream = entry.DataStream)
            {
                if (stream != null)
                {
                    using (Stream fs = new FileStream(pathTo, FileMode.Create, FileAccess.Write))
                    {
                        Console.Write($"Extracting: {entry.Name}... ");
                        stream.CopyTo(fs);
                        Console.WriteLine($"Done!");
                    }
                }
            }
        }

        private static MemoryStream CopyBrotliToMemoryStream(Stream source)
        {
            MemoryStream memory = new MemoryStream();
            using (BrotliStream brotli = new BrotliStream(source, CompressionMode.Decompress, true))
            {
                Console.Write("Buffering package to memory...");
                brotli.CopyTo(memory);
                Console.WriteLine(" Done!");
            }

            memory.Position = 0;
            return memory;
        }
    }
}
