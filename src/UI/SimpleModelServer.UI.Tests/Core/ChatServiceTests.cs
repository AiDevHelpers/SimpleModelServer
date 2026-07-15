using System.Net;
using System.Net.Http;
using Moq;
using SimpleModelServer.UI.Core;
using Xunit;
using Xunit.Sdk;

namespace SimpleModelServer.UI.Tests.Core
{
    public class ChatServiceTests
    {
        [Fact]
        public void Constructor_RequiresSettingsManager()
        {
            Assert.Throws<ArgumentNullException>(() => new ChatService(null!));
        }

        [Fact]
        public async Task TestConnectionAsync_ReturnsTrue_OnSuccess()
        {
            var mockHttpClient = new Mock<HttpClient>();
            var settings = new SettingsManager();
            var mockHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
            var httpClient = new HttpClient(mockHandler);

            var service = new ChatService(settings, httpClient);
            var result = await service.TestConnectionAsync(TestContext.Current.CancellationToken);

            Assert.True(result);
        }

        [Fact]
        public async Task TestConnectionAsync_ReturnsFalse_OnFailure()
        {
            var settings = new SettingsManager();
            var mockHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
            var httpClient = new HttpClient(mockHandler);

            var service = new ChatService(settings, httpClient);
            var result = await service.TestConnectionAsync(TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task TestConnectionAsync_ReturnsFalse_OnException()
        {
            var settings = new SettingsManager();
            var mockHandler = new MockHttpMessageHandler(null, new HttpRequestException("Connection failed"));
            var httpClient = new HttpClient(mockHandler);

            var service = new ChatService(settings, httpClient);
            var result = await service.TestConnectionAsync(TestContext.Current.CancellationToken);

            Assert.False(result);
        }

        [Fact]
        public async Task GetAvailableModelsAsync_ReturnsEmptyList_OnFailure()
        {
            var settings = new SettingsManager();
            var mockHandler = new MockHttpMessageHandler(HttpStatusCode.BadRequest);
            var httpClient = new HttpClient(mockHandler);

            var service = new ChatService(settings, httpClient);
            var models = await service.GetAvailableModelsAsync(TestContext.Current.CancellationToken);

            Assert.Empty(models);
        }

        /// <summary>Mock handler for testing HTTP requests</summary>
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode? _statusCode;
            private readonly Exception? _exception;

            public MockHttpMessageHandler(HttpStatusCode? statusCode, Exception? exception = null)
            {
                _statusCode = statusCode;
                _exception = exception;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                if (_exception != null)
                {
                    return Task.FromException<HttpResponseMessage>(_exception);
                }

                return Task.FromResult(new HttpResponseMessage(_statusCode ?? HttpStatusCode.OK));
            }
        }
    }
}
