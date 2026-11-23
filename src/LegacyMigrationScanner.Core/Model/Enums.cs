using System.Text.Json.Serialization;

namespace LegacyMigrationScanner.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
public enum Severity
{
    Info = 0,
    Warning = 1,
    Critical = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter<FindingCategory>))]
public enum FindingCategory
{
    ProjectFormat,
    SystemWeb,
    Wcf,
    Package,
    Configuration,
    Platform,
    DataAccess,
}

[JsonConverter(typeof(JsonStringEnumConverter<ProjectKind>))]
public enum ProjectKind
{
    Unknown,
    ClassLibrary,
    Console,
    AspNetWebApplication,
    WcfService,
    WinForms,
    Wpf,
    Test,
}

[JsonConverter(typeof(JsonStringEnumConverter<PackageSource>))]
public enum PackageSource
{
    PackagesConfig,
    PackageReference,
}
