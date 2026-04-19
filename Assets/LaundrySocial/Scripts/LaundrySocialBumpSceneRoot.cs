using UnityEngine;

/// <summary>
/// Same behaviour as <see cref="LaundrySocialSceneRoot"/> but enables the dual-standing-zone creature bump
/// by default.
/// </summary>
public class LaundrySocialBumpSceneRoot : LaundrySocialSceneRoot
{
    protected override void Awake()
    {
        sceneFlow = LaundrySocialSceneFlowKind.StandingZonesBumpOnly;
        enableCreatureBumpInteraction = true;
        base.Awake();
    }
}
