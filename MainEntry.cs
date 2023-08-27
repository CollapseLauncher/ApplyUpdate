using Brotli;
using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace ApplyUpdateGUI
{
    internal class MainEntry : Application
    {
        #region Extends
        [DllImport("kernel32.dll")]
        public static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        public static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        [DllImport("Kernel32.dll")]
        public static extern void AllocConsole();

        [DllImport("Kernel32.dll")]
        public static extern void FreeConsole();

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        #endregion

        private static IntPtr m_consoleHandle;

        [STAThread]
        static void Main(string[] args)
        {
#if DEBUG
            AllocateConsole();
#endif

            consoleWinPtr = GetConsoleWindow();
            m_App = new App();
            m_App.Exit += (a, b) =>
            {
#if !DEBUG
                ShowWindow(consoleWinPtr, 5);
                Console.WriteLine("Quit from ApplyUpdate and restored the console window.");
#endif
            };

            if (args.Length != 0 && args[0].ToLower() == "compress")
            {
#if !DEBUG
                AllocateConsole();
#endif
                if (CompressMode(args) > 0) Console.ReadLine();
                return;
            }

            if (Directory.GetCurrentDirectory().Trim('\\') != UpdateTask.realExecDir.Trim('\\'))
            {
                Console.WriteLine($"Moving to the right working directory ({UpdateTask.realExecDir})...");
                Directory.SetCurrentDirectory(UpdateTask.realExecDir);
            }

            RunApp();
        }

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
            using (BrotliStream bso = new BrotliStream(fso, CompressionMode.Compress, true))
            {
                bso.SetQuality(11);
                bso.SetWindow(24);

                Console.WriteLine("Input path: " + args[1]);
                Console.WriteLine("Output path: " + args[2]);
                byte[] buffer = new byte[4 << 14];
                Console.WriteLine("Input filesize: " + fsi.Length + " bytes");

                int read = 0;
                long curRead = 0;
                long length = fsi.Length;
                while ((read = fsi.Read(buffer, 0, buffer.Length)) > 0)
                {
                    curRead += read;
                    Console.Write($"\rCompressing: {Math.Round(((double)curRead / length) * 100, 4)}%...");
                    bso.Write(buffer, 0, read);
                }
                Console.WriteLine(" Completed!");
                Console.WriteLine("Output filesize: " + fso.Length + " bytes");
                Console.WriteLine($"Compression ratio: {Math.Round((double)fso.Length / fsi.Length * 100, 4)}%");
            }

            return 0;
        }

        internal static IntPtr consoleWinPtr { get; set; }
        internal static App m_App { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void RunApp()
        {
#if !DEBUG
            ShowWindow(consoleWinPtr, 0);
#endif
            m_App.InitializeComponent();
            m_App.Run();
        }

        private static void AllocateConsole()
        {
            if (m_consoleHandle != IntPtr.Zero)
            {
                IntPtr consoleWindow = GetConsoleWindow();
                ShowWindow(consoleWindow, 5);
                return;
            }

            AllocConsole();
            m_consoleHandle = GetStdHandle(-11);
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "ApplyUpdate Console";

            if (!GetConsoleMode(m_consoleHandle, out uint mode))
            {
                throw new ContextMarshalException("Failed to initialize console mode!");
            }

            if (!SetConsoleMode(m_consoleHandle, mode | 12))
            {
                throw new ContextMarshalException($"Failed to set console mode with error code: {GetLastError()}");
            }
        }
    }
}
