using UnityEngine;

/// <summary>
/// Switches the same character root between keyboard floor simulation and OptiTrack rigid body streaming.
/// Add OptitrackRigidBody on the same object and assign it here; disable keyboard mode when you want live tracking.
/// </summary>
public class MotionTrackModeSwitch : MonoBehaviour
{
    public bool useKeyboardSimulation = true;
    public FloorSimulatedMover keyboardMover;
    public OptitrackRigidBody optitrackBody;

    void Reset()
    {
        keyboardMover = GetComponent<FloorSimulatedMover>();
        optitrackBody = GetComponent<OptitrackRigidBody>();
    }

    void OnValidate()
    {
        Apply();
    }

    void Awake()
    {
        Apply();
    }

    public void Apply()
    {
        if (keyboardMover != null)
            keyboardMover.SetSimulated(useKeyboardSimulation);
        if (optitrackBody != null)
            optitrackBody.enabled = !useKeyboardSimulation;
    }
}
