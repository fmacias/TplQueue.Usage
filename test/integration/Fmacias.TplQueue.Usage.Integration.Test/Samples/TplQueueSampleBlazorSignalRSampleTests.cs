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
        public async Task PassiveTimelineDashboard_IsTheOnlyApplicationSurface()
        {
            await using var harness = await BlazorSignalRSampleHarness.StartAsync();

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
                Assert.That(dashboardPage, Does.Contain("Fit timeline"));
                Assert.That(dashboardPage, Does.Contain("passive"));
                Assert.That(
                    dashboardPage,
                    Does.Contain("lib/vis-timeline/8.5.2/vis-timeline-graph2d.min.css"));
                Assert.That(
                    dashboardPage,
                    Does.Contain("lib/vis-timeline/8.5.2/vis-timeline-graph2d.min.js"));
                Assert.That(dashboardPage, Does.Not.Contain("Select queue"));
                Assert.That(dashboardPage, Does.Not.Contain("Start workload"));
                Assert.That(dashboardPage, Does.Not.Contain("<nav"));
                Assert.That(dashboardPage, Does.Not.Contain(">About</a>"));
                Assert.That(formerDashboardStatus, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(apiStatus, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That(openApiStatus, Is.EqualTo(HttpStatusCode.NotFound));
            });
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

            public static async Task<BlazorSignalRSampleHarness> StartAsync()
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
                var process = StartProcess(sampleDllPath, sampleOutputDirectory, listeningUrl);
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
                    "The hosted workload did not complete two three-job roots on every queue within the timeout.");
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
                var completedQueues = new HashSet<string>(StringComparer.Ordinal);
                var queueCards = Regex.Matches(
                    dashboardPage,
                    "<article class=\"card queue-summary-card\".*?</article>",
                    RegexOptions.Singleline | RegexOptions.CultureInvariant);

                foreach (Match queueCard in queueCards)
                {
                    var queueName = Regex.Match(
                        queueCard.Value,
                        "<h2[^>]*>(?<queue>ParallelQ|FifoQ|CacheQ)</h2>",
                        RegexOptions.CultureInvariant);

                    if (!queueName.Success ||
                        !HasSummaryValue(queueCard.Value, "Total", 6) ||
                        !HasSummaryValue(queueCard.Value, "Running", 0) ||
                        !HasSummaryValue(queueCard.Value, "Completed", 6) ||
                        !HasSummaryValue(queueCard.Value, "Failed", 0))
                    {
                        continue;
                    }

                    completedQueues.Add(queueName.Groups["queue"].Value);
                }

                return completedQueues.Count == 3;
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
