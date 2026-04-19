using UnityEngine;

/// <summary>
/// Six seats on the long sides of a rectangle (three per long edge). Seat 0 = south-west corner when
/// looking from above with +Z as north.
/// </summary>
public class SocialTableLayout : MonoBehaviour
{
    public Transform tableRoot;
    public float tableHalfExtentX = 4f;
    public float tableHalfExtentZ = 2f;
    public float seatInset = 0.35f;
    public Transform[] seats = new Transform[6];

    /// <summary>
    /// Picks a seat diagonally across from the seated guest so newcomers face into the dyad (example: guest at 0 → seat 4).
    /// </summary>
    public static int DiagonalSeatAcrossFrom(int seatedGuestSeatIndex)
    {
        switch (seatedGuestSeatIndex)
        {
            case 0: return 4;
            case 1: return 4;
            case 2: return 3;
            case 3: return 2;
            case 4: return 0;
            case 5: return 0;
            default: return 4;
        }
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        var t = tableRoot != null ? tableRoot : transform;
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.35f);
        var c = t.position;
        var size = new Vector3(tableHalfExtentX * 2f, 0.02f, tableHalfExtentZ * 2f);
        Gizmos.matrix = Matrix4x4.TRS(c, t.rotation, Vector3.one);
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
