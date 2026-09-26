namespace Waylonia.Accessibility;

internal sealed record A11yWalkResult(IReadOnlyList<A11yVisit> Nodes, bool NodeCapHit, int DepthCut);
