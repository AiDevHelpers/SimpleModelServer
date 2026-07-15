using SimpleModelServer.Common.Infrastructure;

using Xunit;

namespace SimpleModelServer.Common.Tests;

/// <summary>
///     Covers ConsoleHelper's two console-writing helpers. Zero coverage before this — small
///     surface area, but WriteLine's "restore the original color even after writing" behavior and
///     WriteOverwrite's exact padding/carriage-return format are both easy to silently break
///     (e.g. a refactor that forgets the try/finally-equivalent restore) without a test catching
///     it, since neither would show up as a compile error.
/// </summary>
public sealed class ConsoleHelperTests
{
    [Fact]
    public void WriteLine_RestoresOriginalForegroundColor_AfterWriting()
    {
        var helper = new ConsoleHelper();
        ConsoleColor original = Console.ForegroundColor;
        try
        {
            using StringWriter writer = new();
            Console.SetOut(writer);

            helper.WriteLine("hello", ConsoleColor.Green);

            Assert.Equal(original, Console.ForegroundColor);
            Assert.Contains("hello", writer.ToString());
        }
        finally
        {
            Console.ForegroundColor = original;
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }
    }

    [Fact]
    public void WriteOverwrite_WritesCarriageReturnAndPadsToWidth()
    {
        var helper = new ConsoleHelper();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);

            helper.WriteOverwrite("hi", 5);

            Assert.Equal("\rhi   ", writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void WriteOverwrite_TextLongerThanPadding_IsNotTruncated()
    {
        var helper = new ConsoleHelper();
        using StringWriter writer = new();
        TextWriter originalOut = Console.Out;
        try
        {
            Console.SetOut(writer);

            helper.WriteOverwrite("a longer string", 3);

            Assert.Equal("\ra longer string", writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }
}
