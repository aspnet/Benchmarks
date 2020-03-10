using System;
using System.Runtime.InteropServices;

namespace BenchmarksDriver
{
    public class Log
    {
        public static bool IsVerbose { get; set; }
        public static bool IsQuiet { get; set; }

        public static void Quiet(string message)
        {
            Console.WriteLine(message);
        }

        public static void Write(string message, bool notime = false, bool error = false)
        {
            if (error)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            if (!IsQuiet)
            {
                var time = DateTime.Now.ToString("hh:mm:ss.fff");
                if (notime)
                {
                    Console.WriteLine(message);
                }
                else
                {
                    Console.WriteLine($"[{time}] {message}");
                }
            }

            Console.ResetColor();
        }

        public static void Verbose(string message)
        {
            if (IsVerbose && !IsQuiet)
            {
                Write(message);
            }
        }

        public static void DisplayOutput(string content)
        {
            if (String.IsNullOrEmpty(content))
            {
                return;
            }

            #region Switching console mode on Windows to preserve colors for stdout

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var iStdOut = GetStdHandle(STD_OUTPUT_HANDLE);
                if (GetConsoleMode(iStdOut, out uint outConsoleMode))
                {
                    var tempConsoleMode = outConsoleMode;

                    outConsoleMode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING | DISABLE_NEWLINE_AUTO_RETURN;
                    if (!SetConsoleMode(iStdOut, outConsoleMode))
                    {
                        Console.WriteLine($"failed to set output console mode, error code: {GetLastError()}");
                    }

                    if (!SetConsoleMode(iStdOut, tempConsoleMode))
                    {
                        Console.WriteLine($"failed to restore console mode, error code: {GetLastError()}");
                    }
                }
            }

            #endregion

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Convert LF
                content = content?.Replace("\n", Environment.NewLine) ?? "";
            }

            Log.Write(content.Trim(), notime: true);
        }

        private const int STD_OUTPUT_HANDLE = -11;
        private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        private const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        [DllImport("kernel32.dll")]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

    }
}
