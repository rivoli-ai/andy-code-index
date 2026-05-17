// Copyright (c) Rivoli AI 2026. All rights reserved.
// Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Andy.CodeIndex.Infrastructure.Telemetry;
using FluentAssertions;
using Xunit;

namespace Andy.CodeIndex.Tests.Unit.Telemetry;

/// <summary>
/// OT4 (rivoli-ai/conductor#1262) — fences the activity source +
/// QueryDuration histogram so silent drift between the source name
/// registered in Program.cs and the name spans/metrics are emitted
/// under can't ship.
/// </summary>
public class CodeIndexTelemetryTests
{
    [Fact]
    public void ActivitySource_StartsAnActivity_WhenListenerSubscribes()
    {
        var captured = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CodeIndexTelemetry.ServiceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = captured.Add,
        };
        ActivitySource.AddActivityListener(listener);

        using (var activity = CodeIndexTelemetry.ActivitySource.StartActivity("IndexQuery"))
        {
            activity.Should().NotBeNull();
            activity!.SetTag("code_index.query.mode", "hybrid");
        }

        captured.Should().ContainSingle();
        captured[0].OperationName.Should().Be("IndexQuery");
        captured[0].GetTagItem("code_index.query.mode").Should().Be("hybrid");
    }

    [Fact]
    public void QueryDurationHistogram_IsOnTheCanonicalMeter()
    {
        CodeIndexTelemetry.QueryDuration.Name.Should().Be("code_index.query.duration");
        CodeIndexTelemetry.QueryDuration.Meter.Name.Should().Be(CodeIndexTelemetry.ServiceName);
    }
}
