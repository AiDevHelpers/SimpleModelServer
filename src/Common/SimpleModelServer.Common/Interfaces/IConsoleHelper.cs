namespace SimpleModelServer.Common.Interfaces;

public interface IConsoleHelper
{
	void WriteLine(string message, ConsoleColor color);
	void WriteOverwrite(string text, int padding);
}
