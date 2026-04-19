using UnityEngine;

/// <summary>
/// Draws a flat rectangle on the floor (world XZ) for a standing footprint.
/// </summary>
public class StandingZoneOutline : MonoBehaviour
{
    public float halfExtentX = 0.55f;
    public float halfExtentZ = 0.55f;
    public Color lineColor = new Color(0.25f, 0.75f, 0.45f, 0.95f);
    public float lineWidth = 0.06f;
    public float heightY = 0.03f;

    LineRenderer m_lr;

    void OnEnable()
    {
        EnsureLine();
        Rebuild();
    }

    void OnValidate()
    {
        EnsureLine();
        Rebuild();
    }

    void EnsureLine()
    {
        if (m_lr != null) return;
        m_lr = gameObject.GetComponent<LineRenderer>();
        if (m_lr == null) m_lr = gameObject.AddComponent<LineRenderer>();
        m_lr.loop = true;
        m_lr.numCornerVertices = 1;
        m_lr.numCapVertices = 1;
        m_lr.widthMultiplier = lineWidth;
        m_lr.useWorldSpace = true;
        m_lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        m_lr.receiveShadows = false;
        var sh = Shader.Find("Sprites/Default");
        if (sh != null) m_lr.material = new Material(sh);
        m_lr.sortingOrder = 5;
    }

    public void Rebuild()
    {
        if (m_lr == null) EnsureLine();
        var c = transform.position;
        c.y = heightY;
        float hx = halfExtentX;
        float hz = halfExtentZ;
        m_lr.positionCount = 5;
        m_lr.SetPosition(0, c + new Vector3(-hx, 0f, -hz));
        m_lr.SetPosition(1, c + new Vector3(hx, 0f, -hz));
        m_lr.SetPosition(2, c + new Vector3(hx, 0f, hz));
        m_lr.SetPosition(3, c + new Vector3(-hx, 0f, hz));
        m_lr.SetPosition(4, c + new Vector3(-hx, 0f, -hz));
        m_lr.widthMultiplier = lineWidth;
        m_lr.startColor = m_lr.endColor = lineColor;
    }

    /// <summary>True if world XZ lies in the axis-aligned rectangle; Y is ignored.</summary>
    public static bool ContainsXZ(Vector3 worldPos, Vector3 zoneCenter, float hx, float hz)
    {
        return Mathf.Abs(worldPos.x - zoneCenter.x) <= hx && Mathf.Abs(worldPos.z - zoneCenter.z) <= hz;
    }
}
