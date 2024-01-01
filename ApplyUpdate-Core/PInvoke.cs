using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ApplyUpdate
{
    public static class PInvoke
    {
        public static bool IsWindows11 = GetIsWindows11();
        public static IClassicDesktopStyleApplicationLifetime m_window;
        public static IntPtr m_consoleWindow { get => GetConsoleWindow(); }
        public static IntPtr m_consoleHandle { get => GetStdHandle(-11); }

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

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        public static extern int MessageBoxExA(IntPtr hWnd, string text, string caption, uint type);
        #endregion

        public static void AllocateConsole()
        {
            if (m_consoleHandle != IntPtr.Zero)
            {
                ShowWindow(m_consoleWindow, 5);
                return;
            }

            AllocConsole();
            ShowWindow(m_consoleWindow, 5);

            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "ApplyUpdate Console";

            if (!GetConsoleMode(m_consoleHandle, out uint mode))
            {
                throw new ContextMarshalException("Failed to initialize console mode!");
            }

            if (!SetConsoleMode(m_consoleHandle, mode | 12))
            {
                throw new ContextMarshalException($"Failed to set console mode with error code: {PInvoke.GetLastError()}");
            }
        }
        private static bool GetIsWindows11()
        {
            OperatingSystem osDetail = Environment.OSVersion;
            ReadOnlySpan<char> versionStrSpan = osDetail.Version.ToString().AsSpan();
            int count = versionStrSpan.Count('.');
            if (count != 3) return false;
            ++count;

            Span<Range> ranges = stackalloc Range[count];
            Span<ushort> w_windowsVersionNumbers = stackalloc ushort[count];
            versionStrSpan.Split(ranges, '.');
            for (int i = 0; i < count; i++)
                _ = ushort.TryParse(versionStrSpan[ranges[i]], out w_windowsVersionNumbers[i]);

            return w_windowsVersionNumbers[2] >= 22000;
        }
    }
}
