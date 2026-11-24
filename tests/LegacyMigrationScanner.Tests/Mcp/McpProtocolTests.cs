using System.Text.Json;
using LegacyMigrationScanner.Tests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LegacyMigrationScanner.Tests.Mcp;

/// <summary>Starts the real <c>lms mcp</c> process and talks to it with the official MCP client over stdio.</summary>
public sealed class McpProtocolTests
{
    private static async Task<McpClient> StartAsync(CancellationToken ct)
    {
        var lms = Path.Combine(AppContext.BaseDirectory, "lms.dll");
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "lms",
            Command = "dotnet",
            Arguments = [lms, "mcp", "--offline", "--no-cache", "--root", Path.GetDirectoryName(TestHelpers.FixtureDir)!],
            WorkingDirectory = TestHelpers.RepoRoot,
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    [Fact]
    public async Task Server_ExposesToolsAndPrompt_AndAnswersOverStdio()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var client = await StartAsync(timeout.Token);

        Assert.Equal("legacy-migration-scanner", client.ServerInfo?.Name);
        Assert.False(string.IsNullOrEmpty(client.ServerInstructions));

        var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        Assert.Equal(
            ["explain_finding", "get_findings", "get_packages", "scan_solution"],
            tools.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, t => Assert.True(t.ProtocolTool.Annotations?.ReadOnlyHint));

        var prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
        Assert.Equal("plan_migration", Assert.Single(prompts).Name);

        var scan = await client.CallToolAsync("scan_solution", new Dictionary<string, object?> { ["path"] = "LegacyShop" }, cancellationToken: timeout.Token);
        Assert.NotEqual(true, scan.IsError);
        var structured = Assert.IsType<JsonElement>(scan.StructuredContent);
        Assert.Equal("LegacyShop.sln", structured.GetProperty("solution").GetString());
        Assert.Equal(4, structured.GetProperty("projects").GetArrayLength());

        var outside = await client.CallToolAsync("scan_solution", new Dictionary<string, object?> { ["path"] = TestHelpers.RepoRoot }, cancellationToken: timeout.Token);
        Assert.True(outside.IsError);
        Assert.Contains("outside", Assert.IsType<TextContentBlock>(outside.Content[0]).Text, StringComparison.Ordinal);
    }
}
