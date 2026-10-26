using System;

namespace LilithTextInjector;

internal static class D022SliceELiveStatusFormatter
{
    internal static string Format(
        D022SliceEAssistSnapshot snapshot,
        D022SliceDStatusLanguage language)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        var title = language switch
        {
            D022SliceDStatusLanguage.TraditionalChinese =>
                "D-022 窄域動作 Assist",
            D022SliceDStatusLanguage.SimplifiedChinese =>
                "D-022 窄域动作 Assist",
            D022SliceDStatusLanguage.Japanese =>
                "D-022 限定モーション Assist",
            _ => "D-022 Narrow Motion Assist"
        };
        var arm = language switch
        {
            D022SliceDStatusLanguage.TraditionalChinese =>
                "每次啟動需 Ctrl+F12 明確 Arm",
            D022SliceDStatusLanguage.SimplifiedChinese =>
                "每次启动需 Ctrl+F12 明确 Arm",
            D022SliceDStatusLanguage.Japanese =>
                "起動ごとに Ctrl+F12 で明示 Arm",
            _ => "Explicit Ctrl+F12 Arm required per process"
        };

        return string.Join(
            "\n",
            $"{title}  {snapshot.Status}",
            arm,
            $"enabled={snapshot.AssistEnabled} source={snapshot.SourceEnabled} armed={snapshot.Armed}",
            $"candidate={snapshot.CandidateCount}/1 queue={snapshot.QueueSubmissionCount} started={snapshot.StartedCount}",
            $"completed={snapshot.CompletedCount} interrupted={snapshot.PlayerInterruptedCount} failed={snapshot.FailedCount}",
            $"runtime-generation={snapshot.ConfigurationGeneration} situation={snapshot.SituationRevision}",
            $"latest={Short(snapshot.LatestTerminal)} proposal={Short(snapshot.LatestProposalId)}",
            "Memory/Evidence/Reasoning/provider = 0/0/0/0");
    }

    private static string Short(string value)
        => string.IsNullOrEmpty(value)
            ? "-"
            : value[..Math.Min(12, value.Length)];
}
