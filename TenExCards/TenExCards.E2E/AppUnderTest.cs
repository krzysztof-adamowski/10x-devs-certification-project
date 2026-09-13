using System.Diagnostics;
using System.Text;

namespace TenExCards.E2E;

/// <summary>
/// Starts the application on a fixed loopback address for the lifetime of a test class and stops it
/// afterwards. Captures stdout and stderr so a failed start reports why rather than timing out bare.
/// </summary>
public sealed class AppUnderTest : IAsyncLifetime
{
    public const string BaseUrl = "http://127.0.0.1:5199";

    private static readonly TimeSpan StartBudget = TimeSpan.FromSeconds(90);

    private readonly StringBuilder _output = new();
    private Process? _process;

    public async Task InitializeAsync()
    {
        await RefuseIfAlreadyServingAsync();

        var repoRoot = FindRepoRoot();

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine("TenExCards", "TenExCards", "TenExCards.csproj"));
        startInfo.ArgumentList.Add("--no-launch-profile");

        // ASPNETCORE_ENVIRONMENT is NOT optional: --no-launch-profile alone defaults to Production,
        // where the Key Vault guard runs and every framework asset 500s — the page then renders
        // unstyled rather than failing outright, which is far harder to diagnose.
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["Testing__E2E"] = "true";

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the application under test.");

        _process.OutputDataReceived += (_, e) => Capture(e.Data);
        _process.ErrorDataReceived += (_, e) => Capture(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilServingAsync();
    }

    private void Capture(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    private string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    /// <summary>
    /// BaseUrl is a fixed port, and WaitUntilServingAsync below accepts whatever answers it. A
    /// leftover process from an earlier run therefore makes the whole suite drive an OLD build:
    /// observed 2026-09-13 as six failures against correct code, then a pass that proved nothing.
    /// Refuse loudly instead — a wrong-build run must never look like a result.
    /// </summary>
    private static async Task RefuseIfAlreadyServingAsync()
    {
        // A TCP connect, not an HTTP GET. An HTTP probe cannot tell "nothing is there" from "a
        // leftover process is alive but slow to answer" — both present as a timeout — so it must
        // either miss the case it exists for or refuse to run when the port is free. Binding is
        // the thing that actually collides, and a successful connect is the unambiguous signal.
        var uri = new Uri(BaseUrl);
        using var probe = new System.Net.Sockets.TcpClient();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await probe.ConnectAsync(uri.Host, uri.Port, timeout.Token);
        }
        catch (Exception)
        {
            // Refused, unreachable or no answer: nothing holds the port.
            return;
        }

        throw new InvalidOperationException(
            $"Something is already serving {BaseUrl}. This suite would drive that process instead of "
            + "the build under test. Stop it and re-run.");
    }

    private async Task WaitUntilServingAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTimeOffset.UtcNow + StartBudget;

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"The application exited with code {_process.ExitCode} before serving.{Environment.NewLine}{Output}");
            }

            try
            {
                var response = await client.GetAsync(BaseUrl);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"The application did not serve {BaseUrl} within {StartBudget.TotalSeconds:N0}s.{Environment.NewLine}{Output}");
    }

    /// <summary>Walks up from the test binary until the solution file appears, so the launcher does
    /// not depend on where the runner was invoked from.</summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TenExCards", "TenExCards.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            // Kills the tree: `dotnet run` spawns the application as a child, and killing only the
            // parent leaves the port held and the next run failing to bind.
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }

        _process?.Dispose();
        return Task.CompletedTask;
    }
}
