using System.Diagnostics;

namespace TaskManager.Tracing
{
    // вывод трассировки в консоль приглушенным цветом
    public sealed class ColorConsoleTraceListener : TraceListener
    {
        public static readonly object ConsoleLock = new();

        public ColorConsoleTraceListener() : base("ConsoleTrace")
        {
        }

        public override void Write(string? message)
        {
            lock (ConsoleLock)
            {
                var old = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(message);
                Console.ForegroundColor = old;
            }
        }

        public override void WriteLine(string? message)
        {
            if (NeedIndent)
                WriteIndent();

            Write("  " + message + Environment.NewLine);
            NeedIndent = true;
        }
    }
}
