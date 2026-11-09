using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace Agoda.DevExTelemetry.IntegrationTests;

[TestFixture(DatabaseProvider.Sqlite)]
[TestFixture(DatabaseProvider.PostgreSql)]
public class CommandIngestTests
{
    private readonly DatabaseProvider _provider;
    private CustomWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public CommandIngestTests(DatabaseProvider provider) => _provider = provider;

    [SetUp]
    public void SetUp()
    {
        _factory = new CustomWebApplicationFactory(_provider);
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Command_WhenDevServerAborted_StoresSuccessFalseAndSignal()
    {
        var payload = CreatePayload(phase: "dev-server", command: "pnpm run dev", outcome: "aborted",
            success: false, signal: "SIGTERM", exitCode: 143, timeTaken: "61234");

        var response = await TestFixtures.PostJsonAsync(_client, "/command", payload);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.DrainBackgroundQueuesAsync();

        using var db = _factory.CreateDbContext();
        var metric = await db.BuildMetrics.SingleAsync();
        metric.MetricType.ShouldBe("command");
        metric.SourceEndpoint.ShouldBe("/command");
        metric.Platform.ShouldBe(nameof(PlatformID.Unix));
        metric.TimeTakenMs.ShouldBe(61234);

        using var extra = JsonDocument.Parse(metric.ExtraData!);
        var root = extra.RootElement;
        root.GetProperty("Phase").GetString().ShouldBe("dev-server");
        root.GetProperty("Command").GetString().ShouldBe("pnpm run dev");
        root.GetProperty("Outcome").GetString().ShouldBe("aborted");
        root.GetProperty("Success").GetBoolean().ShouldBeFalse();
        root.GetProperty("Signal").GetString().ShouldBe("SIGTERM");
        root.GetProperty("ExitCode").GetInt32().ShouldBe(143);
    }

    [Test]
    public async Task Command_WhenTimeTakenNotANumber_Returns400()
    {
        var payload = CreatePayload(phase: "install", command: "pnpm install", outcome: "success",
            success: true, signal: null, exitCode: 0, timeTaken: "soon");

        var response = await TestFixtures.PostJsonAsync(_client, "/command", payload);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Command_Install_WithNpmTimers_PersistsInExtraData()
    {
        var sessionId = Guid.NewGuid().ToString();
        var payload = new
        {
            id = Guid.NewGuid().ToString(),
            sessionId,
            userName = "dev",
            cpuCount = 8,
            hostname = "dev-box",
            platform = "Linux",
            os = "6.18.0",
            timeTaken = "48000",
            branch = "main",
            projectName = "demo",
            repository = "https://github.com/example/demo.git",
            repositoryName = "demo",
            timestamp = 1793577600000,
            type = "command",
            phase = "install",
            command = "npm install",
            exitCode = 0,
            success = true,
            measurementSource = "npm-timing",
            packageManager = "npm",
            packageManagerVersion = "10.8.2",
            coldInstall = true,
            lockfileChanged = false,
            npmTimers = new Dictionary<string, double>
            {
                ["idealTree"] = 8120,
                ["reify"] = 39880
            }
        };

        var response = await TestFixtures.PostJsonAsync(_client, "/command", payload);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _factory.DrainBackgroundQueuesAsync();

        using var db = _factory.CreateDbContext();
        var metric = await db.BuildMetrics.SingleAsync();
        metric.SessionId.ShouldBe(sessionId);
        metric.MetricType.ShouldBe("command");

        using var extra = JsonDocument.Parse(metric.ExtraData!);
        var root = extra.RootElement;
        root.GetProperty("MeasurementSource").GetString().ShouldBe("npm-timing");
        root.GetProperty("PackageManager").GetString().ShouldBe("npm");
        root.GetProperty("ColdInstall").GetBoolean().ShouldBeTrue();
        var timers = root.GetProperty("NpmTimers");
        timers.GetProperty("idealTree").GetDouble().ShouldBe(8120);
        timers.GetProperty("reify").GetDouble().ShouldBe(39880);
    }

    [Test]
    public async Task Command_Devserver_WithPrebundledFalse_PersistsFalse()
    {
        var payload = CreateDevserverPayload(prebundled: false, includePrebundled: true);
        await TestFixtures.PostJsonAsync(_client, "/command", payload);
        await _factory.DrainBackgroundQueuesAsync();

        using var db = _factory.CreateDbContext();
        var metric = await db.BuildMetrics.SingleAsync();
        using var extra = JsonDocument.Parse(metric.ExtraData!);
        extra.RootElement.GetProperty("Prebundled").GetBoolean().ShouldBeFalse();
    }

    [Test]
    public async Task Command_Devserver_WithoutPrebundled_PersistsNull()
    {
        var payload = CreateDevserverPayload(prebundled: null, includePrebundled: false);
        await TestFixtures.PostJsonAsync(_client, "/command", payload);
        await _factory.DrainBackgroundQueuesAsync();

        using var db = _factory.CreateDbContext();
        var metric = await db.BuildMetrics.SingleAsync();
        using var extra = JsonDocument.Parse(metric.ExtraData!);
        extra.RootElement.GetProperty("Prebundled").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task Command_WithSpooledAt_PersistsAsColumn()
    {
        const long spooledAt = 1785900160500;
        var payload = new
        {
            id = Guid.NewGuid().ToString(),
            sessionId = Guid.NewGuid().ToString(),
            userName = "dev",
            cpuCount = 8,
            hostname = "dev-box",
            platform = "Linux",
            os = "6.18.0",
            timeTaken = "100",
            branch = "main",
            projectName = "demo",
            repository = "https://github.com/example/demo.git",
            repositoryName = "demo",
            type = "command",
            phase = "devserver",
            command = "vite dev",
            exitCode = 0,
            success = true,
            spooledAt
        };

        await TestFixtures.PostJsonAsync(_client, "/command", payload);
        await _factory.DrainBackgroundQueuesAsync();

        using var db = _factory.CreateDbContext();
        var metric = await db.BuildMetrics.SingleAsync();
        metric.SpooledAt.ShouldBe(spooledAt);
    }

    private static object CreatePayload(string phase, string command, string outcome, bool success,
        string? signal, int exitCode, string timeTaken) =>
        new
        {
            id = Guid.NewGuid().ToString(),
            sessionId = Guid.NewGuid().ToString(),
            userName = "dev",
            cpuCount = 8,
            hostname = "dev-box",
            platform = "Linux",
            os = "6.18.0",
            timeTaken,
            branch = "main",
            projectName = "demo",
            repository = "https://github.com/example/demo.git",
            repositoryName = "demo",
            timestamp = 1793577600000,
            builtAt = "2026-11-02T00:00:00.000Z",
            type = "command",
            phase,
            command,
            exitCode,
            success,
            outcome,
            signal
        };

    private static object CreateDevserverPayload(bool? prebundled, bool includePrebundled)
    {
        if (includePrebundled)
        {
            return new
            {
                id = Guid.NewGuid().ToString(),
                sessionId = Guid.NewGuid().ToString(),
                userName = "dev",
                cpuCount = 8,
                hostname = "dev-box",
                platform = "Linux",
                os = "6.18.0",
                timeTaken = "250",
                branch = "main",
                projectName = "demo",
                repository = "https://github.com/example/demo.git",
                repositoryName = "demo",
                type = "command",
                phase = "devserver",
                command = "vite dev",
                exitCode = 0,
                success = true,
                prebundled
            };
        }

        return new
        {
            id = Guid.NewGuid().ToString(),
            sessionId = Guid.NewGuid().ToString(),
            userName = "dev",
            cpuCount = 8,
            hostname = "dev-box",
            platform = "Linux",
            os = "6.18.0",
            timeTaken = "250",
            branch = "main",
            projectName = "demo",
            repository = "https://github.com/example/demo.git",
            repositoryName = "demo",
            type = "command",
            phase = "devserver",
            command = "vite dev",
            exitCode = 0,
            success = true
        };
    }
}
