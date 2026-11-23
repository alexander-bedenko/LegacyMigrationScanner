using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Reporting;

/// <summary>Canonical machine-readable report; other formats are rendered from the same <see cref="ScanResult"/>.</summary>
public sealed class JsonReportWriter : IReportWriter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Format => "json";
    public string FileExtension => ".json";

    public void Write(ScanResult result, TextWriter writer) =>
        writer.Write(JsonSerializer.Serialize(result, Options));
}
