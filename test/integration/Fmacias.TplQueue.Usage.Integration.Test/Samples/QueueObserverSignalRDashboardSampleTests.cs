using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class QueueObserverSignalRDashboardSampleTests
    {
        [Test]
        public async Task Wait_Run_RendersHttpSurface_AndPublishesProjectedDtos()
        {
            await using var harness = await QueueObserverSignalRDashboardHarness.StartAsync();

            var homePage = await harness.GetStringAsync("/");
            using var negotiateResponse = await harness.PostAsync("/hubs/queue-events/negotiate?negotiateVersion=1");
            var run = await harness.StartRunAsync("wait");
            var completedRun = await harness.WaitForRunAsync(run.RunId, status => status == "Completed");
            var events = await harness.GetEventsAsync(run.RunId);

            Assert.That(homePage, Does.Contain("Queue Observer SignalR Dashboard"));
            Assert.That(homePage, Does.Contain("@microsoft/signalr"));
            Assert.That(negotiateResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(completedRun.Status, Is.EqualTo("Completed"));
            Assert.That(completedRun.Scenario, Is.EqualTo("metadata"));
            Assert.That(events.Any(evt => evt.Name == "PublishDashboardSummary" && evt.Status == "RootSuccessed"), Is.True);
            Assert.That(events.Any(evt => evt.Name == "Refresh sidebar" && evt.Status == "Successed"), Is.True);
        }

        [Test]
        public async Task Cancel_Run_CancelsCollection_AndSkipsRootSuccess()
        {
            await using var harness = await QueueObserverSignalRDashboardHarness.StartAsync();

            var run = await harness.StartRunAsync("cancel");
            var completedRun = await harness.WaitForRunAsync(run.RunId, status => status == "Canceled");
            var events = await harness.GetEventsAsync(run.RunId);

            Assert.That(completedRun.Status, Is.EqualTo("Canceled"));
            Assert.That(completedRun.Scenario, Is.EqualTo("metadata"));
            Assert.That(events.Any(evt => evt.Name == "CollectDashboardRows" && evt.Status == "Canceled"), Is.True);
            Assert.That(events.Any(evt => evt.Name == "Refresh sidebar" && evt.Status == "Successed"), Is.True);
            Assert.That(events.Any(evt => evt.Name == "PublishDashboardSummary" && evt.Status == "RootSuccessed"), Is.False);
        }

        [Test]
        public async Task Payload_Wait_Run_ProjectsDetachedJsonSnapshots()
        {
            await using var harness = await QueueObserverSignalRDashboardHarness.StartAsync();

            var homePage = await harness.GetStringAsync("/");
            var run = await harness.StartPayloadRunAsync("wait");
            var completedRun = await harness.WaitForRunAsync(run.RunId, status => status == "Completed");
            var events = await harness.GetEventsAsync(run.RunId);

            Assert.That(homePage, Does.Contain("Payload Queue"));
            Assert.That(completedRun.Status, Is.EqualTo("Completed"));
            Assert.That(completedRun.Scenario, Is.EqualTo("payload"));
            Assert.That(events.Any(evt => evt.Scenario == "payload"), Is.True);
            Assert.That(events.Any(evt => evt.HasPayloadSnapshot), Is.True);
            Assert.That(events.Any(evt => !string.IsNullOrWhiteSpace(evt.PayloadHandlerKey)), Is.True);
            Assert.That(
                events.Any(evt => evt.SerializedPayload != null && evt.SerializedPayload.Contains("\"SourceGreetings\"", StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public async Task Sequential_Runs_ReuseTheSameInjectedQueue()
        {
            await using var harness = await QueueObserverSignalRDashboardHarness.StartAsync();

            var firstRun = await harness.StartRunAsync("wait");
            var completedFirstRun = await harness.WaitForRunAsync(firstRun.RunId, status => status == "Completed");
            var secondRun = await harness.StartRunAsync("wait");
            var completedSecondRun = await harness.WaitForRunAsync(secondRun.RunId, status => status == "Completed");

            Assert.That(completedFirstRun.QueueId, Is.Not.Null);
            Assert.That(completedSecondRun.QueueId, Is.EqualTo(completedFirstRun.QueueId));
        }

        private sealed class QueueObserverSignalRDashboardHarness : IAsyncDisposable
        {
            private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            private readonly Process _process;
            private readonly HttpClient _client;

            private QueueObserverSignalRDashboardHarness(Process process, HttpClient client)
            {
                _process = process;
                _client = client;
            }

            public static async Task<QueueObserverSignalRDashboardHarness> StartAsync()
            {
                var repoRoot = ResolveRepositoryRoot();
                var sampleRoot = Path.Combine(repoRoot, "samples", "QueueObserverSignalRDashboard");
                var testOutputDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                var targetFramework = testOutputDirectory.Name;
                var configuration = testOutputDirectory.Parent?.Name
                    ?? throw new InvalidOperationException("Unable to resolve the test configuration folder.");
                var sampleOutputDirectory = Path.Combine(sampleRoot, "bin", configuration, targetFramework);
                var sampleDllPath = Path.Combine(sampleOutputDirectory, "QueueObserverSignalRDashboard.dll");

                if (!File.Exists(sampleDllPath))
                {
                    throw new FileNotFoundException(
                        "QueueObserverSignalRDashboard must be built before the sample integration tests run.",
                        sampleDllPath);
                }

                var port = GetFreePort();
                var baseAddress = new Uri($"http://127.0.0.1:{port}");
                var listeningUrl = baseAddress.AbsoluteUri.TrimEnd('/');
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{sampleDllPath}\" --urls \"{listeningUrl}\"",
                    WorkingDirectory = sampleOutputDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = processStartInfo };
                process.Start();

                var client = new HttpClient
                {
                    BaseAddress = baseAddress,
                    Timeout = TimeSpan.FromSeconds(5)
                };

                await WaitForHealthyHomePageAsync(client).ConfigureAwait(false);
                return new QueueObserverSignalRDashboardHarness(process, client);
            }

            public async Task<string> GetStringAsync(string relativeUrl)
            {
                return await _client.GetStringAsync(relativeUrl).ConfigureAwait(false);
            }

            public async Task<HttpResponseMessage> PostAsync(string relativeUrl)
            {
                return await _client.PostAsync(relativeUrl, content: null).ConfigureAwait(false);
            }

            public Task<RunSnapshot> StartRunAsync(string mode)
            {
                return StartRunAsync("/api/sample/runs", mode);
            }

            public Task<RunSnapshot> StartPayloadRunAsync(string mode)
            {
                return StartRunAsync("/api/sample/payload-runs", mode);
            }

            private async Task<RunSnapshot> StartRunAsync(string routeBase, string mode)
            {
                using var response = await _client.PostAsync($"{routeBase}/{mode}", content: null).ConfigureAwait(false);
                var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.StatusCode != HttpStatusCode.Accepted)
                {
                    Assert.Fail($"Unexpected status code '{response.StatusCode}' while starting mode '{mode}'. Payload:{Environment.NewLine}{payload}");
                }

                return Deserialize<RunSnapshot>(payload);
            }

            public async Task<RunSnapshot> WaitForRunAsync(Guid runId, Func<string, bool> isCompleted)
            {
                var timeoutAt = DateTime.UtcNow.AddSeconds(15);

                while (DateTime.UtcNow < timeoutAt)
                {
                    var run = await GetRunAsync(runId).ConfigureAwait(false);
                    if (isCompleted(run.Status))
                    {
                        return run;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                return await GetRunAsync(runId).ConfigureAwait(false);
            }

            public async Task<EventSnapshot[]> GetEventsAsync(Guid runId)
            {
                var timeoutAt = DateTime.UtcNow.AddSeconds(5);

                while (DateTime.UtcNow < timeoutAt)
                {
                    var response = await _client.GetStringAsync($"/api/sample/runs/{runId}/events").ConfigureAwait(false);
                    var events = Deserialize<EventSnapshot[]>(response);
                    if (events.Length > 0)
                    {
                        return events;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                var finalResponse = await _client.GetStringAsync($"/api/sample/runs/{runId}/events").ConfigureAwait(false);
                return Deserialize<EventSnapshot[]>(finalResponse);
            }

            public async ValueTask DisposeAsync()
            {
                _client.Dispose();

                if (!_process.HasExited)
                {
                    try
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                _process.Dispose();
            }

            private async Task<RunSnapshot> GetRunAsync(Guid runId)
            {
                var payload = await _client.GetStringAsync($"/api/sample/runs/{runId}").ConfigureAwait(false);
                return Deserialize<RunSnapshot>(payload);
            }

            private static async Task WaitForHealthyHomePageAsync(HttpClient client)
            {
                var timeoutAt = DateTime.UtcNow.AddSeconds(15);

                while (DateTime.UtcNow < timeoutAt)
                {
                    try
                    {
                        using var response = await client.GetAsync("/").ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                Assert.Fail("QueueObserverSignalRDashboard did not become reachable within the timeout.");
            }

            private static T Deserialize<T>(string json)
            {
                var value = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (value == null)
                {
                    throw new InvalidOperationException("The sample returned an empty JSON payload.");
                }

                return value;
            }

            private static string ResolveRepositoryRoot()
            {
                var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "samples", "QueueObserverSignalRDashboard")))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }

                throw new InvalidOperationException("Unable to resolve the TplQueue.Usage repository root.");
            }

            private static int GetFreePort()
            {
                using var listener = new TcpListener(IPAddress.Loopback, port: 0);
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
        }

        private sealed class RunSnapshot
        {
            public Guid RunId { get; set; }
            public string Scenario { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string Mode { get; set; } = string.Empty;
            public string? QueueName { get; set; }
            public Guid? QueueId { get; set; }
        }

        private sealed class EventSnapshot
        {
            public string Scenario { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool HasPayloadSnapshot { get; set; }
            public string? SerializedPayload { get; set; }
            public string? PayloadHandlerKey { get; set; }
        }
    }
}
