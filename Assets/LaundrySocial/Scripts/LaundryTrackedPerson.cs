using UnityEngine;

/// <summary>
/// Marks a character root that laundry zones and the creature director should track.
/// </summary>
public class LaundryTrackedPerson : MonoBehaviour
{
    public int playerIndex;
    /// <summary>World-space point used for hand projection before the creature lands on the table.</summary>
    public Transform handAnchor;

    void Reset()
    {
        if (handAnchor == null)
        {
            var t = new GameObject("HandAnchor").transform;
            t.SetParent(transform, false);
            t.localPosition = new Vector3(0.35f, 1.45f, 0.35f);
            handAnchor = t;
        }
    }
}
