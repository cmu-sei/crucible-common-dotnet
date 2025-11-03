// Copyright 2025 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

namespace Crucible.Common.ServiceDefaults.OpenTelemetry;

public sealed class OpenTelemetryOptions
{
    public bool AddAlwaysOnTracingSampler { get; set; } = false;
    public bool AddConsoleExporter { get; set; } = false;
    public bool AddPrometheusExporter { get; set; } = false;
    public IEnumerable<string> CustomActivitySources { get; set; } = [];
    public bool IncludeDefaultActivitySources { get; set; } = true;
    public IEnumerable<string> CustomMeters { get; set; } = [];
    public bool IncludeDefaultMeters { get; set; } = true;
}
