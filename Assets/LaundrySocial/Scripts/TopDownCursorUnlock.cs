using UnityEngine;

/// <summary>
/// Keeps the cursor free when using top-down keyboard simulation (Mini FPC otherwise locks the cursor).
/// </summary>
public class TopDownCursorUnlock : MonoBehaviour
{
    void LateUpdate()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
