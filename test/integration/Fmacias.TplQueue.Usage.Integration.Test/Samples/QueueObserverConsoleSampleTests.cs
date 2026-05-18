using System.Diagnostics;

namespace Fmacias.TplQueue.Integration.Test.Samples
{
    [TestFixture]
    public sealed class QueueObserverConsoleSampleTests
    {
        [Test]
        public async Task Wait_Mode_CompletesPipeline_AndWritesEntityLogs()
        {
            var harness = QueueObserverConsoleHarness.Create();
            harness.ResetLogs();

            var result = await harness.RunAsync("wait");
            var appLog = await harness.WaitForLogContainsAsync(
                "app.log",
                "Standalone helper operation executed outside the job graph.");
            var queueLog = await harness.WaitForLogContainsAsync(
                "queue.log",
                "Queue 'greetings-pipeline' created with max parallelism 1.");
            var observerLog = await harness.WaitForLogContainsAsync(
                "logging-observer.log",
                "Finalized 'Standalone helper task'");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.ToAssertionMessage());
            StringAssert.Contains("Serialized JSON output:", result.StandardOutput);
            StringAssert.Contains("Standalone helper operation executed outside the job graph.", appLog);
            StringAssert.Contains("Queue 'greetings-pipeline' created with max parallelism 1.", queueLog);
            StringAssert.Contains("Root Finalized 'Load'", observerLog);
            StringAssert.Contains("Finalized 'Standalone helper task'", observerLog);
            Assert.That(harness.GetLogLength("profiling-observer.log"), Is.GreaterThan(0L));
        }

        [Test]
        public async Task Cancel_Mode_CancelsExtract_AndStillFinalizesStandaloneTask()
        {
            var harness = QueueObserverConsoleHarness.Create();
            harness.ResetLogs();

            var result = await harness.RunAsync("cancel");
            var appLog = await harness.WaitForLogContainsAsync(
                "app.log",
                "Queue finalized gracefully.");
            var observerLog = await harness.WaitForLogContainsAsync(
                "logging-observer.log",
                "Finalized 'Standalone helper task'");

            Assert.That(result.ExitCode, Is.EqualTo(0), result.ToAssertionMessage());
            StringAssert.DoesNotContain("Serialized JSON output:", result.StandardOutput);
            StringAssert.Contains("Canceling the workflow during Extract.", appLog);
            StringAssert.Contains("Standalone helper operation executed outside the job graph.", appLog);
            StringAssert.Contains("Canceled 'Extract'", observerLog);
            StringAssert.DoesNotContain("Root Finalized 'Load'", observerLog);
            StringAssert.Contains("Queue finalized gracefully.", appLog);
        }

        private sealed class QueueObserverConsoleHarness
        {
            private readonly string _sampleRoot;
            private readonly string _sampleDllPath;
            private readonly string _sampleOutputDirectory;

            private QueueObserverConsoleHarness(
                string sampleRoot,
                string sampleDllPath,
                string sampleOutputDirectory)
            {
                _sampleRoot = sampleRoot;
                _sampleDllPath = sampleDllPath;
                _sampleOutputDirectory = sampleOutputDirectory;
            }

            public static QueueObserverConsoleHarness Create()
            {
                var repoRoot = ResolveRepositoryRoot();
                var sampleRoot = Path.Combine(repoRoot, "samples", "QueueObserverConsole");
                var testOutputDirectory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                var targetFramework = testOutputDirectory.Name;
                var configuration = testOutputDirectory.Parent?.Name
                    ?? throw new InvalidOperationException("Unable to resolve the test configuration folder.");
                var sampleOutputDirectory = Path.Combine(sampleRoot, "bin", configuration, targetFramework);
                var sampleDllPath = Path.Combine(sampleOutputDirectory, "QueueObserverConsole.dll");

                if (!File.Exists(sampleDllPath))
                {
                    throw new FileNotFoundException(
                        "QueueObserverConsole must be built before the sample integration tests run.",
                        sampleDllPath);
                }

                return new QueueObserverConsoleHarness(sampleRoot, sampleDllPath, sampleOutputDirectory);
            }

            public void ResetLogs()
            {
                var logDirectory = GetLogDirectory();
                if (Directory.Exists(logDirectory))
                {
                    Directory.Delete(logDirectory, recursive: true);
                }
            }

            public async Task<ProcessResult> RunAsync(string mode)
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"\"{_sampleDllPath}\" {mode}",
                    WorkingDirectory = _sampleOutputDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));

                var standardOutput = await standardOutputTask.ConfigureAwait(false);
                var standardError = await standardErrorTask.ConfigureAwait(false);

                return new ProcessResult(process.ExitCode, standardOutput, standardError);
            }

            public string ReadLog(string fileName)
            {
                var path = Path.Combine(GetLogDirectory(), fileName);
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }

            public async Task<string> WaitForLogContainsAsync(
                string fileName,
                string expectedText,
                TimeSpan? timeout = null)
            {
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    throw new ArgumentException("A log file name is required.", nameof(fileName));
                }

                if (string.IsNullOrWhiteSpace(expectedText))
                {
                    throw new ArgumentException("An expected log message is required.", nameof(expectedText));
                }

                var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

                while (DateTime.UtcNow <= deadline)
                {
                    var log = ReadLog(fileName);
                    if (log.Contains(expectedText, StringComparison.Ordinal))
                    {
                        return log;
                    }

                    await Task.Delay(100).ConfigureAwait(false);
                }

                return ReadLog(fileName);
            }

            public long GetLogLength(string fileName)
            {
                var path = Path.Combine(GetLogDirectory(), fileName);
                return File.Exists(path) ? new FileInfo(path).Length : 0L;
            }

            private string GetLogDirectory()
            {
                return Path.Combine(_sampleRoot, "Logs");
            }

            private static string ResolveRepositoryRoot()
            {
                var current = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

                while (current != null)
                {
                    if (Directory.Exists(Path.Combine(current.FullName, "samples", "QueueObserverConsole")))
                    {
                        return current.FullName;
                    }

                    current = current.Parent;
                }

                throw new InvalidOperationException("Unable to resolve the TplQueue.Usage repository root.");
            }
        }

        private sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }

            public string ToAssertionMessage()
            {
                return
                    $"ExitCode: {ExitCode}{Environment.NewLine}" +
                    $"STDOUT:{Environment.NewLine}{StandardOutput}{Environment.NewLine}" +
                    $"STDERR:{Environment.NewLine}{StandardError}";
            }
        }
    }
}
