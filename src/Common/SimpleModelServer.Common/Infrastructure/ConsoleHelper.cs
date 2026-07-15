using SimpleModelServer.Common.Interfaces;

namespace SimpleModelServer.Common.Infrastructure;

public sealed class ConsoleHelper : IConsoleHelper
{
    public void WriteLine(string message, ConsoleColor color)
    {
        ConsoleColor original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = original;
    }

    public void WriteOverwrite(string text, int padding)
    {
        Console.Write('\r' + text.PadRight(padding));
    }
}
