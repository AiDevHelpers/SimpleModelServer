using SimpleModelServer.UI.Core;
using Xunit;

namespace SimpleModelServer.UI.Tests.Core
{
	public class DiagnosticLoggerTests
	{
		[Fact]
		public void Constructor_InitializesWithSource()
		{
			var logger = new DiagnosticLogger("TestSource");
			Assert.NotNull(logger);
		}

		[Fact]
		public void Info_DoesNotThrow()
		{
			var logger = new DiagnosticLogger("TestSource");
			logger.Info("Test message");
			// No exception should be thrown
		}

		[Fact]
		public void Warning_DoesNotThrow()
		{
			var logger = new DiagnosticLogger("TestSource");
			logger.Warning("Test warning");
			// No exception should be thrown
		}

		[Fact]
		public void Error_WithMessage_DoesNotThrow()
		{
			var logger = new DiagnosticLogger("TestSource");
			logger.Error("Test error");
			// No exception should be thrown
		}

		[Fact]
		public void Error_WithException_DoesNotThrow()
		{
			var logger = new DiagnosticLogger("TestSource");
			var ex = new InvalidOperationException("Test exception");
			logger.Error("Test error", ex);
			// No exception should be thrown
		}

		[Fact]
		public void Debug_DoesNotThrow()
		{
			var logger = new DiagnosticLogger("TestSource");
			logger.Debug("Test debug");
			// No exception should be thrown
		}
	}
}
