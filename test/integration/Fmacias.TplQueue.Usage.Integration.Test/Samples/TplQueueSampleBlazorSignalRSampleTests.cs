using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class TplQueueSampleBlazorSignalRSampleTests
    {
        [Test]
        public async Task DefaultDashboard_OffersStopAndContinuesBothWorkloadsBeyondTwoTicks()
        {
            // Arrange / Act: use the host's fixed workflow composition.
            await using var harness = await BlazorSignalRSampleHarness.StartAsync(true);
            var page = await harness.GetStringAsync("/");

            // Assert: the server owns delivery; HTTP requests only observe it.
            Assert.That(page, Does.Contain("data-stop-arrivals"));
            Assert.That(page, Does.Contain("Stop arrivals"));
            await harness.WaitForContinuousDashboardAsync();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PassiveTimelineDashboard_IsTheOnlyApplicationSurface(bool launchFromSource)
        {
            await using var harness = await BlazorSignalRSampleHarness.StartAsync(launchFromSource);

            var dashboardPage = await harness.WaitForCompletedDashboardAsync();
            var formerDashboardStatus = await harness.GetStatusCodeAsync("/tplqueue");
            var apiStatus = await harness.GetStatusCodeAsync("/api/etl/operations");
            var openApiStatus = await harness.GetStatusCodeAsync("/openapi/v1.json");

            Assert.Multiple(() =>
            {
                Assert.That(dashboardPage, Does.Contain("ETL execution timeline"));
                Assert.That(dashboardPage, Does.Contain("ParallelQ"));
                Assert.That(dashboardPage, Does.Contain("FifoQ"));
                Assert.That(dashboardPage, Does.Contain("CacheQ"));
                Assert.That(dashboardPage, Does.Contain("job-queue-timeline"));
                Assert.That(dashboardPage, Does.Contain("data-stop-arrivals"));
                Assert.That(dashboardPage, Does.Not.Contain("ChartJS"));
                Assert.That(dashboardPage, Does.Not.Contain("chart.umd"));
                Assert.That(dashboardPage, Does.Not.Contain("job-details"));
                Assert.That(dashboardPage, Does.Not.Contain("Select queue"));
                Assert.That(dashboardPage, Does.Not.Contain("Start workload"));
                Assert.That(dashboardPage, Does.Not.Contain("<nav"));
                Assert.That(dashboardPage, Does.Not.Contain(">About</a>"));
                Assert.That(formerDashboardStatus, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(apiStatus, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(openApiStatus, Is.EqualTo(HttpStatusCode.NotFound));
            });
            Assert.That(await harness.GetStatusCodeAsync("/job-monitor/src/job-queue-timeline.js"), Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await harness.GetStatusCodeAsync("/job-monitor/integrations/blazor/job-monitor.js"), Is.EqualTo(HttpStatusCode.OK));
        }

        private sealed class BlazorSignalRSampleHarness : IAsyncDisposable
        {
            private readonly Process _process;
            private readonly HttpClient _client;

            private BlazorSignalRSampleHarness(Process process, HttpClient client)
            {
                _process = process;
                _client = client;
            }

            public static async Task<BlazorSignalRSampleHarness> StartAsync(bool launchFromSource)
            {
                var repoRoot = ResolveRepositoryRoot();
                var sampleRoot = Path.Combine(repoRoot, "samples", "TplQueue.Sample.BlazorSignalR");
                var testOutputDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                var targetFramework = testOutputDirectory.Name;
                var configuration = testOutputDirectory.Parent?.Name
                    ?? throw new InvalidOperationException("Unable to resolve the test configuration folder.");
                var sampleOutputDirectory = Path.Combine(sampleRoot, "bin", configuration, targetFramework);
                var sampleDllPath = Path.Combine(sampleOutputDirectory, "TplQueue.Sample.BlazorSignalR.dll");

                if (!File.Exists(sampleDllPath))
                {
                    throw new FileNotFoundException(
                        "TplQueue.Sample.BlazorSignalR must be built before the integration test runs.",
                        sampleDllPath);
                }

                var port = GetFreePort();
                var baseAddress = new Uri($"http://127.0.0.1:{port}");
                var listeningUrl = baseAddress.AbsoluteUri.TrimEnd('/');
                var process = StartProcess(sampleDllPath, launchFromSource ? sampleRoot : sampleOutputDirectory, listeningUrl);
                var client = new HttpClient
                {
                    BaseAddress = baseAddress,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                var harness = new BlazorSignalRSampleHarness(process, client);

                try
                {
                    await WaitForHealthyHomePageAsync(client).ConfigureAwait(false);
                    return harness;
                }
                catch
                {
                    await harness.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }

            public Task<string> GetStringAsync(string relativeUrl)
            {
                return _client.GetStringAsync(relativeUrl);
            }

            public async Task WaitForContinuousDashboardAsync()
            {
                var deadline = DateTime.UtcNow.AddSeconds(25);
                while (DateTime.UtcNow < deadline)
                {
                    var page = await GetStringAsync("/");
                    var queues = Regex.Matches(page, "data-completed=\"(\\d+)\" data-total=\"(\\d+)\"");
                    if (queues.Count == 3 && queues.All(q => int.Parse(q.Groups[1].Value) >= 12)) return;
                    await Task.Delay(100);
                }
                Assert.Fail("Expected at least three ticks of ETL plus single jobs on all queues.");
            }

            public async Task<string> WaitForCompletedDashboardAsync()
            {
                var timeoutAt = DateTime.UtcNow.AddSeconds(20);
                var dashboardPage = string.Empty;

                while (DateTime.UtcNow < timeoutAt)
                {
                    dashboardPage = await GetStringAsync("/").ConfigureAwait(false);

                    if (ContainsCompletedQueueCards(dashboardPage))
                    {
                        return dashboardPage;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                Assert.Fail(
                    "The hosted workload did not complete two rounds of ETL and single jobs on every queue within the timeout.");
                return dashboardPage;
            }

            public async Task<HttpStatusCode> GetStatusCodeAsync(string relativeUrl)
            {
                using var response = await _client.GetAsync(relativeUrl).ConfigureAwait(false);
                return response.StatusCode;
            }

            public async ValueTask DisposeAsync()
            {
                _client.Dispose();

                try
                {
                    if (!_process.HasExited)
                    {
                        try
                        {
                            _process.Kill(entireProcessTree: true);
                        }
                        catch (InvalidOperationException) when (_process.HasExited)
                        {
                        }
                    }

                    await _process
                        .WaitForExitAsync()
                        .WaitAsync(TimeSpan.FromSeconds(5))
                        .ConfigureAwait(false);
                }
                finally
                {
                    _process.Dispose();
                }
            }

            private static Process StartProcess(string sampleDllPath, string workingDirectory, string listeningUrl)
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{sampleDllPath}\" --urls \"{listeningUrl}\"",
                    WorkingDirectory = workingDirectory,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = processStartInfo };
                process.Start();
                return process;
            }

            private static async Task WaitForHealthyHomePageAsync(HttpClient client)
            {
                var timeoutAt = DateTime.UtcNow.AddSeconds(15);

                while (DateTime.UtcNow < timeoutAt)
                {
                    try
                    {
                        using var response = await client.GetAsync("/").ConfigureAwait(false);
                        if (response.StatusCode == HttpStatusCode.OK)
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                Assert.Fail("TplQueue.Sample.BlazorSignalR did not become reachable within the timeout.");
            }

            private static string ResolveRepositoryRoot()
            {
                var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "samples", "TplQueue.Sample.BlazorSignalR")))
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

            private static bool ContainsCompletedQueueCards(string dashboardPage)
            {
                return new[] { "parallel", "fifo", "cache" }.All(queue =>
                {
                    var card = Regex.Match(dashboardPage,
                        $"data-queue=\"{queue}\" data-completed=\"(\\d+)\" data-total=\"(\\d+)\"");
                    return card.Success && int.Parse(card.Groups[1].Value) >= 8;
                });
            }

            private static bool HasSummaryValue(
                string queueCard,
                string label,
                int expectedValue)
            {
                return Regex.IsMatch(
                    queueCard,
                    $"<dt[^>]*>{label}</dt>\\s*<dd[^>]*>{expectedValue}</dd>",
                    RegexOptions.CultureInvariant);
            }
        }
    }
}
