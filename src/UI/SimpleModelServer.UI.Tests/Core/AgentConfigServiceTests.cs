using System.Net;
using System.Net.Http;
using System.Text;
using SimpleModelServer.UI.Core;
using Xunit;

namespace SimpleModelServer.UI.Tests.Core
{
    public class AgentConfigServiceTests
    {
        [Fact]
        public void Constructor_RequiresSettingsManager()
        {
            Assert.Throws<ArgumentNullException>(() => new AgentConfigService(null!));
        }

        [Fact]
        public async Task GetAgentsAsync_ParsesAgentsFromConfigResponse()
        {
            var settings = new SettingsManager();
            var json = """
            {
                "global": {},
                "agents": {
                    "code": { "systemPrompt": "You are a C# coding assistant.", "temperature": 0.2 },
                    "debug": { "systemPrompt": "You help debug issues.", "temperature": null }
                }
            }
            """;
            var handler = new StaticJsonHandler(HttpStatusCode.OK, json);
            var service = new AgentConfigService(settings, new HttpClient(handler));

            var agents = await service.GetAgentsAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, agents.Count);
            Assert.Contains(agents, a => a.Name == "code" && a.SystemPrompt == "You are a C# coding assistant." && a.Temperature == 0.2);
            Assert.Contains(agents, a => a.Name == "debug" && a.Temperature == null);
        }

        [Fact]
        public async Task GetAgentsAsync_ReturnsEmptyList_OnFailure()
        {
            var settings = new SettingsManager();
            var handler = new StaticJsonHandler(HttpStatusCode.InternalServerError, "{}");
            var service = new AgentConfigService(settings, new HttpClient(handler));

            var agents = await service.GetAgentsAsync(TestContext.Current.CancellationToken);

            Assert.Empty(agents);
        }

        [Fact]
        public async Task UpdateAgentPromptAsync_ReturnsSuggestGoldenSet_WhenServerFlagsIt()
        {
            var settings = new SettingsManager();
            var json = """
            {
                "success": true,
                "setting": "agents.code.systemPrompt",
                "requiresRestart": false,
                "suggestGoldenSet": true,
                "message": "Agent 'code' prompt updated and reloaded."
            }
            """;
            var handler = new StaticJsonHandler(HttpStatusCode.OK, json);
            var service = new AgentConfigService(settings, new HttpClient(handler));

            var result = await service.UpdateAgentPromptAsync("code", "New prompt", TestContext.Current.CancellationToken);

            Assert.True(result.Success);
            Assert.True(result.SuggestGoldenSet);
            Assert.Equal("Agent 'code' prompt updated and reloaded.", result.Message);
        }

        [Fact]
        public async Task UpdateAgentPromptAsync_ReturnsFailure_OnValidationError()
        {
            var settings = new SettingsManager();
            var json = """{ "error": "validation_failed", "message": "Prompt too long" }""";
            var handler = new StaticJsonHandler(HttpStatusCode.BadRequest, json);
            var service = new AgentConfigService(settings, new HttpClient(handler));

            var result = await service.UpdateAgentPromptAsync("code", "New prompt", TestContext.Current.CancellationToken);

            Assert.False(result.Success);
            Assert.False(result.SuggestGoldenSet);
            Assert.Equal("Prompt too long", result.Message);
        }

        /// <summary>Mock handler that always returns the same status code and JSON body.</summary>
        private sealed class StaticJsonHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _json;

            public StaticJsonHandler(HttpStatusCode statusCode, string json)
            {
                _statusCode = statusCode;
                _json = json;
            }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json")
                };
                return Task.FromResult(response);
            }
        }
    }
}
