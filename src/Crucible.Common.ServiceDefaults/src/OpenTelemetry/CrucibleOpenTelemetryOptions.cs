namespace Crucible.Common.ServiceDefaults.OpenTelemetry;

public sealed class CrucibleOpenTelemetryOptions
{
    public bool AddConsoleExporter { get; set; } = false;
    public bool AddPrometheusExporter { get; set; } = false;
    public IEnumerable<string> CustomActivitySources { get; set; } = [];
    public bool IncludeDefaultActivitySources { get; set; } = true;
    public IEnumerable<string> CustomMeters { get; set; } = [];
    public bool IncludeDefaultMeters { get; set; } = true;
}
