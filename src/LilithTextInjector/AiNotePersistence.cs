using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LilithTextInjector;

internal sealed class AiNoteState
{
    public List<AiNoteEvent> Pending { get; set; } = new();
    public List<DateTimeOffset> DeliveredAt { get; set; } = new();
    public List<string> DeliveredPaths { get; set; } = new();
}

internal sealed class AiNoteEvent
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string LatestContext { get; set; } = string.Empty;
    public string Emotion { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DeliverAfter { get; set; }
    public string Status { get; set; } = "pending";

    // Only a successfully published current Hermes player turn opts in.
    // Missing in legacy files: never replay old pending letters on migration.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HermesPublishedCandidate { get; set; }

    // Legacy/off-mode scheduler state must keep its previous JSON shape.
    // Only an actually owned D-025 occurrence persists this opt-in field.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool LivingContinuityOwned { get; set; }
}

internal sealed record AiNoteStartupProjectionResult(
    int RecoveredStatusCount,
    int ExpiredDeliveryCount);

internal static class AiNoteStartupProjection
{
    internal static AiNoteStartupProjectionResult ApplyInMemory(
        AiNoteState state,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var recovered = 0;
        foreach (var item in state.Pending)
        {
            var projected = LivingProactiveNoteDeliveryPolicy.RecoverStatus(
                item.LivingContinuityOwned,
                item.Status);
            if (!string.Equals(projected, item.Status, StringComparison.Ordinal))
            {
                item.Status = projected;
                recovered++;
            }
        }

        var expired = state.DeliveredAt.RemoveAll(value => value < now.AddDays(-30));
        return new AiNoteStartupProjectionResult(recovered, expired);
    }
}
