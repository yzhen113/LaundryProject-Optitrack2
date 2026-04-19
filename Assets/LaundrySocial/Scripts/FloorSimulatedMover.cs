using UnityEngine;

/// <summary>
/// Drives the Mini FPC root in XZ using separate keys per player so two users can coexist (host: arrows, guest: WASD).
/// Disables built-in first-person movement/look while active. Pair with OptitrackRigidBody when not simulating.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class FloorSimulatedMover : MonoBehaviour
{
    public int playerIndex;
    public float moveSpeed = 4.5f;
    public bool simulateOnAwake = true;

    Rigidbody m_body;
    FirstPersonMovement m_move;
    FirstPersonLook[] m_looks;

    void Awake()
    {
        m_body = GetComponent<Rigidbody>();
        m_move = GetComponent<FirstPersonMovement>();
        m_looks = GetComponentsInChildren<FirstPersonLook>(true);
        ApplyMode(simulateOnAwake);
    }

    public void SetSimulated(bool simulated)
    {
        simulateOnAwake = simulated;
        ApplyMode(simulated);
    }

    /// <summary>
    /// Stops <see cref="FixedUpdate"/> from writing horizontal velocity (for OptiTrack / external motion).
    /// Unlike <see cref="SetSimulated"/>(false), does not re-enable first-person keyboard/mouse scripts.
    /// </summary>
    public void DisableKeyboardFloorDrive()
    {
        simulateOnAwake = false;
        if (m_move != null) m_move.enabled = false;
        foreach (var l in m_looks)
            if (l != null) l.enabled = false;
    }

    void ApplyMode(bool simulated)
    {
        if (m_move != null) m_move.enabled = !simulated;
        foreach (var l in m_looks)
            if (l != null) l.enabled = !simulated;

        if (simulated)
        {
            m_body.constraints = RigidbodyConstraints.FreezeRotation;
            m_body.useGravity = true;
        }
    }

    void FixedUpdate()
    {
        if (!simulateOnAwake) return;

        float h, v;
        // Player 0 = host: arrows. Player 1 = guest (user 2): WASD.
        if (playerIndex == 0)
        {
            h = Axis(KeyCode.LeftArrow, KeyCode.RightArrow);
            v = Axis(KeyCode.DownArrow, KeyCode.UpArrow);
        }
        else
        {
            h = Axis(KeyCode.A, KeyCode.D);
            v = Axis(KeyCode.S, KeyCode.W);
        }

        var dir = new Vector3(h, 0f, v);
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        var vel = dir * moveSpeed;
        m_body.linearVelocity = new Vector3(vel.x, m_body.linearVelocity.y, vel.z);
    }

    static float Axis(KeyCode neg, KeyCode pos)
    {
        float v = 0f;
        if (Input.GetKey(neg)) v -= 1f;
        if (Input.GetKey(pos)) v += 1f;
        return v;
    }
}
