using System;

namespace LilithTextInjector;

internal static class CognitiveSemanticShadowReplay
{
    internal static CognitiveSemanticShadowReplayResult Evaluate(
        CognitiveSemanticBatch batch)
    {
        return CognitiveSemanticShadowReplayResult.EvaluateExact(batch);
    }
}

internal static class CognitiveSemanticPairedComparator
{
    internal static CognitiveSemanticPairedComparisonRecord Compare(
        CognitiveDesignOracleProjection oracle,
        CognitiveSemanticShadowReplayResult semantic)
    {
        if (oracle == null)
            throw new ArgumentNullException(nameof(oracle));
        if (semantic == null)
            throw new ArgumentNullException(nameof(semantic));
        return new CognitiveSemanticPairedComparisonRecord(
            oracle,
            semantic);
    }
}
