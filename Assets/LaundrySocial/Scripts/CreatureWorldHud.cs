using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Table-top creature: circle body, elastic travel, then ring + countdown with a lively morph into timer view.
/// </summary>
public class CreatureWorldHud : MonoBehaviour
{
    [Header("Visual tuning")]
    public float coreDiameterMeters = 0.42f;
    [Tooltip("Outer radius of the progress ring (world meters), outside the core sprite.")]
    public float ringRadiusMeters = 0.38f;
    public float ringLineWidth = 0.045f;
    public int ringSegments = 72;
    [Tooltip("Gap between core edge and ring at full emergence.")]
    public float ringGapBeyondCoreMeters = 0.03f;

    [Header("Table presentation")]
    [Tooltip("Shifts the creature/timer on the table plane away from the player toward the table center so it does not sit under the avatar.")]
    public float offsetFromPlayerTowardTableCenterMeters = 0.75f;
    [Tooltip("Blends the on-table anchor between the seat and the player (XZ). 0 = seat only (matches guest after arrival); higher = closer to the avatar.")]
    [Range(0f, 1f)]
    public float followSeatVersusPlayerBlend = 0.06f;

    [Header("Bump motion")]
    [Tooltip("How fast the core follows the elastic path (lower = snappier recoil when they bounce apart).")]
    public float bumpFollowSmoothTime = 0.085f;
    [Tooltip("How gently the core eases back to ring center after the bump.")]
    public float bumpSettleSmoothTime = 0.2f;
    [Tooltip("Side-to-side + slight along-approach jitter (meters); kept low so cores can meet cleanly like Scene2 spring bump.")]
    public float bumpNoiseMeters = 0.01f;
    [Tooltip("How quickly positional noise evolves over the bump.")]
    public float bumpNoiseTimeScale = 1.1f;
    [Tooltip("Approximate number of contact peaks (collisions) along the bump timeline.")]
    public float bumpImpactCount = 6f;
    [Tooltip("Fraction of bump duration spent at full reach-in with zero noise (visible edge contact / overlap).")]
    [Range(0f, 0.35f)]
    public float bumpContactHoldNormalized = 0.1f;
    [Tooltip("Scales the 1st |cos| lobe after the hold (keeps 1st bump shy of full contact). 2nd/3rd lobes use full touch.")]
    [Range(0.15f, 1f)]
    public float bumpFirstLobeTouchScale = 0.42f;
    [Tooltip("Reach multiplier for the 2nd and 3rd bounce lobes (full disk contact / overlap).")]
    [Range(0.5f, 1f)]
    public float bumpSecondThirdLobeTouchScale = 1f;
    [Tooltip("Reach multiplier for lobes after the 3rd bounce.")]
    [Range(0.3f, 1f)]
    public float bumpLaterLobeTouchScale = 1f;
    [Tooltip("Minimum envelope during the bounce phase when |cos| is low (higher = cores stay closer between hits).")]
    [Range(0.2f, 0.85f)]
    public float bumpOscillationFloor = 0.52f;
    [Tooltip("How fast overall amplitude fades over the bounce phase (lower = more repeated full-strength collisions).")]
    [Range(0.1f, 2.5f)]
    public float bumpEnvelopeDecay = 0.48f;

    Canvas m_canvas;
    RectTransform m_canvasRt;
    RectTransform m_coreSlideRoot;
    Image m_coreImage;
    RectTransform m_textBlockRt;
    Text m_timeText;
    Text m_statusText;
    RectTransform m_ringVisualRt;
    Transform m_ringLinesRoot;
    LineRenderer m_elapsedArc;
    LineRenderer m_remainingArc;

    Transform m_follow;
    Transform m_presentationAnchor;
    Vector3 m_tableCenterXZ;
    float m_tableSurfaceY;
    Vector3 m_seatWorldSpot;

    bool m_timerMode;
    bool m_countdownActive;
    float m_totalCountdownSeconds = 60f;
    float m_remainingSeconds;
    float m_ringEmergenceT;

    bool m_coreBumpInProgress;

    static Sprite SpriteFromTexture(Texture2D tex, float worldDiameter)
    {
        if (tex == null) return null;
        float ppu = Mathf.Max(tex.width, tex.height) / Mathf.Max(0.01f, worldDiameter);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
    }

    /// <summary>Full timer1 composite is not drawn here (it duplicates the core circle). Use _timerTex later for a ring-only sprite if needed.</summary>
    public void Build(Texture2D creatureTex, Texture2D _timerTex)
    {
        var canvasGo = new GameObject("CreatureCanvas");
        canvasGo.transform.SetParent(transform, false);
        canvasGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        m_canvas = canvasGo.AddComponent<Canvas>();
        m_canvas.renderMode = RenderMode.WorldSpace;
        m_canvas.sortingOrder = 10;
        canvasGo.AddComponent<GraphicRaycaster>();
        m_canvasRt = canvasGo.GetComponent<RectTransform>();
        m_canvasRt.sizeDelta = new Vector2(1.4f, 1.4f);
        m_canvasRt.localScale = Vector3.one;

        m_ringVisualRt = new GameObject("RingVisual").AddComponent<RectTransform>();
        m_ringVisualRt.SetParent(canvasGo.transform, false);
        m_ringVisualRt.anchorMin = m_ringVisualRt.anchorMax = new Vector2(0.5f, 0.5f);
        m_ringVisualRt.sizeDelta = Vector2.one * (ringRadiusMeters * 2.2f);
        m_ringVisualRt.anchoredPosition = Vector2.zero;
        m_ringVisualRt.localScale = Vector3.one * 0.08f;

        m_textBlockRt = new GameObject("TextBlock").AddComponent<RectTransform>();
        m_textBlockRt.SetParent(canvasGo.transform, false);
        m_textBlockRt.anchorMin = new Vector2(0f, 1f);
        m_textBlockRt.anchorMax = new Vector2(0f, 1f);
        m_textBlockRt.pivot = new Vector2(0f, 1f);
        m_textBlockRt.sizeDelta = new Vector2(0.55f, 0.35f);
        m_textBlockRt.anchoredPosition = new Vector2(-0.55f, 0.48f);
        m_textBlockRt.localScale = Vector3.one;

        m_timeText = CreateText("Time", m_textBlockRt, 0.11f, FontStyle.Bold, TextAnchor.UpperLeft, new Vector2(0f, 0f));
        m_statusText = CreateText("Status", m_textBlockRt, 0.045f, FontStyle.Normal, TextAnchor.UpperLeft, new Vector2(0f, -0.12f));
        m_timeText.canvasRenderer.SetAlpha(0f);
        m_statusText.canvasRenderer.SetAlpha(0f);

        var coreSlideGo = new GameObject("CoreSlide", typeof(RectTransform));
        coreSlideGo.transform.SetParent(canvasGo.transform, false);
        m_coreSlideRoot = coreSlideGo.GetComponent<RectTransform>();
        m_coreSlideRoot.anchorMin = m_coreSlideRoot.anchorMax = new Vector2(0.5f, 0.5f);
        m_coreSlideRoot.pivot = new Vector2(0.5f, 0.5f);
        m_coreSlideRoot.sizeDelta = Vector2.zero;
        m_coreSlideRoot.anchoredPosition = Vector2.zero;

        m_coreImage = CreateImage("Core", m_coreSlideRoot.transform, new Color(0.28f, 0.28f, 0.28f, 1f));
        var coreRt = m_coreImage.rectTransform;
        coreRt.anchorMin = coreRt.anchorMax = new Vector2(0.5f, 0.5f);
        coreRt.sizeDelta = new Vector2(coreDiameterMeters, coreDiameterMeters);
        coreRt.anchoredPosition = Vector2.zero;
        if (creatureTex != null)
        {
            m_coreImage.sprite = SpriteFromTexture(creatureTex, coreDiameterMeters);
            m_coreImage.color = Color.white;
        }

        m_ringLinesRoot = new GameObject("RingLines").transform;
        m_ringLinesRoot.SetParent(transform, false);
        m_ringLinesRoot.localPosition = Vector3.zero;
        m_ringLinesRoot.localRotation = Quaternion.identity;
        m_elapsedArc = CreateArcLine(m_ringLinesRoot, "Elapsed", new Color(0.22f, 0.22f, 0.22f, 1f));
        m_remainingArc = CreateArcLine(m_ringLinesRoot, "Remaining", new Color(0.82f, 0.82f, 0.82f, 1f));
        m_elapsedArc.sortingOrder = 5;
        m_remainingArc.sortingOrder = 5;
        SetArcsVisible(false);
    }

    static Image CreateImage(string name, Transform parent, Color c)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    static Text CreateText(string name, RectTransform parent, float size, FontStyle style, TextAnchor anchor, Vector2 anchored)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(0.52f, 0.14f);
        rt.anchoredPosition = anchored;
        var tx = go.GetComponent<Text>();
        tx.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        tx.fontSize = Mathf.RoundToInt(size * 640f);
        tx.resizeTextForBestFit = true;
        tx.resizeTextMinSize = 8;
        tx.resizeTextMaxSize = 200;
        tx.fontStyle = style;
        tx.alignment = anchor;
        tx.color = new Color(0.18f, 0.18f, 0.18f, 1f);
        tx.raycastTarget = false;
        return tx;
    }

    LineRenderer CreateArcLine(Transform parent, string name, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.loop = false;
        lr.useWorldSpace = true;
        lr.widthMultiplier = ringLineWidth;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = col;
        lr.positionCount = ringSegments + 1;
        return lr;
    }

    void SetArcsVisible(bool v)
    {
        m_elapsedArc.enabled = v;
        m_remainingArc.enabled = v;
    }

    public void SetTimerModeImmediate(float countdownSeconds, float washerId, float dryerId)
    {
        m_timerMode = true;
        m_countdownActive = true;
        m_totalCountdownSeconds = Mathf.Max(1f, countdownSeconds);
        m_remainingSeconds = m_totalCountdownSeconds;
        m_ringEmergenceT = 1f;
        m_ringVisualRt.localScale = Vector3.one;
        m_timeText.canvasRenderer.SetAlpha(1f);
        m_statusText.canvasRenderer.SetAlpha(1f);
        SetArcsVisible(true);
        RefreshTimerTexts(washerId, dryerId);
        RefreshTimeDigits();
        OrientRingGapTowardPresentationAnchor();
        RebuildArcMesh(m_remainingSeconds / m_totalCountdownSeconds);
    }

    /// <summary>Table center on XZ (Y ignored) and the player used to push the HUD toward the table center.</summary>
    public void SetTablePresentationContext(Transform anchorPlayer, Vector3 tableCenterWorld, float tableSurfaceY)
    {
        m_presentationAnchor = anchorPlayer;
        m_tableCenterXZ = new Vector3(tableCenterWorld.x, 0f, tableCenterWorld.z);
        m_tableSurfaceY = tableSurfaceY;
    }

    public void SnapCreatureToSeatOnTable(Vector3 seatWorldPosition)
    {
        var flat = new Vector3(seatWorldPosition.x, m_tableSurfaceY, seatWorldPosition.z);
        transform.position = PushCreatureTowardTableFromAnchor(flat);
    }

    public void ConfigureTableFollow(Transform follow, Vector3 seatWorld, float tableY)
    {
        m_follow = follow;
        m_seatWorldSpot = seatWorld;
        m_tableSurfaceY = tableY;
        if (m_presentationAnchor == null)
            m_presentationAnchor = follow;
    }

    Vector3 PushCreatureTowardTableFromAnchor(Vector3 nominalOnTablePlaneXZ)
    {
        var pos = new Vector3(nominalOnTablePlaneXZ.x, m_tableSurfaceY + 0.02f, nominalOnTablePlaneXZ.z);
        if (m_presentationAnchor == null || offsetFromPlayerTowardTableCenterMeters <= 0f)
            return pos;

        var flatP = new Vector3(m_presentationAnchor.position.x, m_tableSurfaceY, m_presentationAnchor.position.z);
        var center = new Vector3(m_tableCenterXZ.x, m_tableSurfaceY, m_tableCenterXZ.z);
        var dir = center - flatP;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
        {
            dir = new Vector3(pos.x, 0f, pos.z) - flatP;
            dir.y = 0f;
        }

        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.forward;
        dir.Normalize();
        return pos + dir * offsetFromPlayerTowardTableCenterMeters;
    }

    /// <summary>
    /// Slides only the creature disk (core) on the table; ring + text stay fixed. World offset is flattened to XZ.
    /// </summary>
    public void SetCoreSlideWorldOffset(Vector3 worldOffsetHorizontal)
    {
        if (m_coreSlideRoot == null || m_canvasRt == null)
            return;
        worldOffsetHorizontal.y = 0f;
        if (worldOffsetHorizontal.sqrMagnitude < 1e-10f)
        {
            m_coreSlideRoot.anchoredPosition = Vector2.zero;
            return;
        }

        Vector3 local = m_canvasRt.InverseTransformDirection(worldOffsetHorizontal);
        m_coreSlideRoot.anchoredPosition = new Vector2(local.x, local.y);
    }

    /// <summary>Instantly clears core slide (e.g. when interrupting a bump from outside).</summary>
    public void ResetCoreSlideImmediate()
    {
        m_coreBumpInProgress = false;
        SetCoreSlideWorldOffset(Vector3.zero);
    }

    /// <summary>
    /// World meters to slide the core toward the peer so it (1) clears the outer timer ring and (2) meets the other
    /// disk edge-to-edge or slightly overlapped at the midpoint between the two creature roots.
    /// </summary>
    public float ComputeBumpTravelMetersTowardPeer(
        Vector3 peerCreatureRootWorldPosition,
        float ringExitPaddingMeters,
        float travelScale)
    {
        var selfFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        var peerFlat = new Vector3(peerCreatureRootWorldPosition.x, 0f, peerCreatureRootWorldPosition.z);
        float D = Vector3.Distance(selfFlat, peerFlat);
        float r = coreDiameterMeters * 0.5f;

        // Edge-to-edge: s = (D - 2r) / 2; add a few mm so disks read as overlapping (Scene2 bumpDistance-style contact).
        const float touchOverlapMeters = 0.012f;
        float sTouch = Mathf.Max(0f, (D - 2f * r) * 0.5f + touchOverlapMeters);

        // Push far enough that the core leaves the ring silhouette (outer arc) toward the other player
        float sRing = Mathf.Max(0f, ringRadiusMeters - r + ringExitPaddingMeters);

        float s = Mathf.Max(sTouch, sRing) * travelScale;

        float sMax = Mathf.Max(0f, D * 0.5f - r * 0.02f);
        return Mathf.Clamp(s, 0f, sMax);
    }

    /// <summary>
    /// Cores move toward each other, hold at contact, then bounce around (decaying oscillation) before returning to ring center.
    /// </summary>
    public IEnumerator PlayCoreBumpTowardPeerAndReturn(Vector3 peerCreatureRootWorldPosition, float maxTravelMeters, float durationSeconds)
    {
        if (m_coreSlideRoot == null)
            yield break;

        m_coreBumpInProgress = true;
        var selfFlat = new Vector3(transform.position.x, 0f, transform.position.z);
        var peerFlat = new Vector3(peerCreatureRootWorldPosition.x, 0f, peerCreatureRootWorldPosition.z);
        var toPeer = peerFlat - selfFlat;
        toPeer.y = 0f;
        if (toPeer.sqrMagnitude < 1e-6f)
        {
            m_coreBumpInProgress = false;
            yield break;
        }

        toPeer.Normalize();
        Vector3 side = new Vector3(-toPeer.z, 0f, toPeer.x);
        if (side.sqrMagnitude < 1e-8f)
            side = Vector3.right;
        side.Normalize();

        float dur = Mathf.Max(0.15f, durationSeconds);
        float t = 0f;
        Vector3 current = Vector3.zero;
        Vector3 smoothVel = Vector3.zero;
        float smooth = Mathf.Max(0.02f, bumpFollowSmoothTime);
        float settleSmooth = Mathf.Max(0.02f, bumpSettleSmoothTime);
        float noiseSeed = (GetInstanceID() & 0x7fff) * 0.001f + 13.7f;
        float nt = bumpNoiseTimeScale;
        float approachEnd = GetBumpApproachEndNormalized();
        float contactEnd = Mathf.Clamp(approachEnd + bumpContactHoldNormalized, 0f, 0.98f);

        while (t < dur)
        {
            float u = t / dur;
            float env = EvaluateBumpEnvelope(u);
            float n1 = (Mathf.PerlinNoise(t * nt + noiseSeed, noiseSeed * 0.37f) - 0.5f) * 2f;
            float n2 = (Mathf.PerlinNoise(noiseSeed + 2.2f, t * nt * 1.17f) - 0.5f) * 2f;
            float jitterRamp = 0f;
            if (u > contactEnd)
                jitterRamp = Mathf.SmoothStep(0f, 1f, (u - contactEnd) / Mathf.Max(1e-4f, 1f - contactEnd));
            Vector3 jitter = (side * (n1 * bumpNoiseMeters) + toPeer * (n2 * bumpNoiseMeters * 0.25f)) * jitterRamp;
            Vector3 target = toPeer * (env * maxTravelMeters) + jitter;
            float maxVel = maxTravelMeters * 72f;
            current = Vector3.SmoothDamp(current, target, ref smoothVel, smooth, maxVel, Time.deltaTime);
            SetCoreSlideWorldOffset(current);
            t += Time.deltaTime;
            yield return null;
        }

        const float settleEpsilon = 0.00015f;
        int guard = 0;
        while (current.sqrMagnitude > settleEpsilon * settleEpsilon && guard++ < 480)
        {
            current = Vector3.SmoothDamp(current, Vector3.zero, ref smoothVel, settleSmooth, Mathf.Infinity, Time.deltaTime);
            SetCoreSlideWorldOffset(current);
            yield return null;
        }

        SetCoreSlideWorldOffset(Vector3.zero);
        m_coreBumpInProgress = false;
    }

    /// <summary>
    /// Approach, short hold at full reach (touch), then damped |cos| bounces (Scene2-style spring bump, without envelope Perlin).
    /// </summary>
    float GetBumpApproachEndNormalized()
    {
        return Mathf.Clamp(0.12f - bumpContactHoldNormalized * 0.35f, 0.045f, 0.14f);
    }

    float EvaluateBumpEnvelope(float u)
    {
        u = Mathf.Clamp01(u);
        float approachEnd = GetBumpApproachEndNormalized();
        float holdEnd = Mathf.Clamp(approachEnd + bumpContactHoldNormalized, approachEnd + 0.001f, 0.95f);

        if (u < approachEnd)
            return Mathf.SmoothStep(0f, 1f, u / approachEnd);

        if (u < holdEnd)
            return 1f;

        float ub = (u - holdEnd) / (1f - holdEnd);
        float decay = Mathf.Max(0.05f, bumpEnvelopeDecay);
        float damp = Mathf.Exp(-ub * decay);
        float freq = Mathf.Max(2f, bumpImpactCount);
        float impacts = Mathf.Abs(Mathf.Cos(ub * Mathf.PI * 2f * freq));
        // |cos| lobes along ub: peak k is near ub ≈ k/(2*freq). 2nd & 3rd bumps = lobes 1 and 2 (0-based).
        int lobeIndex = Mathf.FloorToInt(ub * 2f * freq + 1e-5f);
        lobeIndex = Mathf.Max(0, lobeIndex);
        float lobeTouch = BumpLobeTouchMultiplier(lobeIndex);
        float floor = Mathf.Clamp(bumpOscillationFloor, 0.05f, 0.92f);
        float peak = Mathf.Clamp01(impacts * lobeTouch);
        float restitution = floor + (1f - floor) * peak;
        float roll = 0.022f * damp * Mathf.Sin(ub * Mathf.PI * 2f * 2f + 0.45f);
        float v = damp * restitution + roll;
        return Mathf.Clamp(v, 0f, 1.12f);
    }

    /// <summary>0 = first bounce after hold, 1 = second bump, 2 = third bump, …</summary>
    float BumpLobeTouchMultiplier(int lobeIndex)
    {
        if (lobeIndex <= 0)
            return bumpFirstLobeTouchScale;
        if (lobeIndex <= 2)
            return bumpSecondThirdLobeTouchScale;
        return bumpLaterLobeTouchScale;
    }

    public IEnumerator PlayArrivalSequence(Transform handAnchor, float handSeconds, Vector3 seatWorld, float jumpDuration, float tableY, float countdownSeconds, float washerId, float dryerId)
    {
        m_tableSurfaceY = tableY;
        m_seatWorldSpot = seatWorld;
        m_timerMode = false;
        SetArcsVisible(false);
        m_timeText.canvasRenderer.SetAlpha(0f);
        m_statusText.canvasRenderer.SetAlpha(0f);
        m_ringVisualRt.localScale = Vector3.one * 0.06f;

        float t0 = Time.time;
        while (Time.time - t0 < handSeconds)
        {
            if (handAnchor != null)
            {
                var flat = new Vector3(handAnchor.position.x, m_tableSurfaceY, handAnchor.position.z);
                transform.position = PushCreatureTowardTableFromAnchor(flat);
            }

            yield return null;
        }

        var start = transform.position;
        var end = PushCreatureTowardTableFromAnchor(new Vector3(seatWorld.x, m_tableSurfaceY, seatWorld.z));
        float dur = Mathf.Max(0.35f, jumpDuration);
        t0 = Time.time;
        while (Time.time - t0 < dur)
        {
            float u = (Time.time - t0) / dur;
            transform.position = ParabolicLerp(start, end, u, 1.35f);
            float s = 1f + 0.35f * Mathf.Sin(u * Mathf.PI);
            m_canvasRt.localScale = Vector3.one * Mathf.Lerp(1f, s, Mathf.Sin(u * Mathf.PI));
            m_coreImage.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.12f, ElasticSquash(u));
            yield return null;
        }

        transform.position = end;
        m_canvasRt.localScale = Vector3.one;
        m_coreImage.rectTransform.localScale = Vector3.one;

        m_follow = null;
        yield return StartCoroutine(MorphToTimer(countdownSeconds, washerId, dryerId));
        m_countdownActive = true;
    }

    static float ElasticSquash(float u)
    {
        return Mathf.Sin(u * Mathf.PI * 2f) * (1f - u) * 0.25f;
    }

    static float OutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    static Vector3 ParabolicLerp(Vector3 a, Vector3 b, float u, float height)
    {
        var flat = Vector3.Lerp(a, b, u);
        float arc = 4f * u * (1f - u);
        flat.y = Mathf.Lerp(a.y, b.y, u) + arc * height;
        return flat;
    }

    IEnumerator MorphToTimer(float countdownSeconds, float washerId, float dryerId)
    {
        m_timerMode = true;
        m_countdownActive = false;
        m_totalCountdownSeconds = Mathf.Max(1f, countdownSeconds);
        m_remainingSeconds = m_totalCountdownSeconds;
        SetArcsVisible(true);
        RefreshTimerTexts(washerId, dryerId);
        RefreshTimeDigits();

        float morph = 0f;
        Vector2 textStart = m_textBlockRt.anchoredPosition;
        Vector2 textEnd = textStart + new Vector2(0.06f, -0.04f);
        while (morph < 1f)
        {
            morph += Time.deltaTime / 0.95f;
            float u = Mathf.Clamp01(morph);
            float e = OutBack(u);
            m_ringEmergenceT = e;
            m_ringVisualRt.localScale = Vector3.LerpUnclamped(Vector3.one * 0.06f, Vector3.one, e);
            float ta = SmoothStep(Mathf.Clamp01((u - 0.15f) / 0.85f));
            m_timeText.canvasRenderer.SetAlpha(ta);
            m_statusText.canvasRenderer.SetAlpha(ta);
            m_textBlockRt.anchoredPosition = Vector2.Lerp(textStart, textEnd, ta);
            OrientRingGapTowardPresentationAnchor();
            RebuildArcMesh(m_remainingSeconds / m_totalCountdownSeconds);
            yield return null;
        }

        m_ringVisualRt.localScale = Vector3.one;
        m_timeText.canvasRenderer.SetAlpha(1f);
        m_statusText.canvasRenderer.SetAlpha(1f);
        m_textBlockRt.anchoredPosition = textEnd;
        OrientRingGapTowardPresentationAnchor();
        RefreshTimeDigits();
    }

    /// <summary>
    /// Arc uses a 270° sweep with a 90° gap; gap midpoint lies on local -Z of the ring pivot. Rotates the pivot so that gap faces the tracked user on the table.
    /// </summary>
    void OrientRingGapTowardPresentationAnchor()
    {
        if (m_presentationAnchor == null || m_ringLinesRoot == null)
            return;

        var c = new Vector3(transform.position.x, 0f, transform.position.z);
        var u = new Vector3(m_presentationAnchor.position.x, 0f, m_presentationAnchor.position.z);
        var want = u - c;
        if (want.sqrMagnitude < 1e-6f)
            return;
        want.Normalize();

        var gapDefaultWorld = transform.TransformDirection(Vector3.back);
        gapDefaultWorld.y = 0f;
        if (gapDefaultWorld.sqrMagnitude < 1e-6f)
            return;
        gapDefaultWorld.Normalize();

        float yaw = Vector3.SignedAngle(gapDefaultWorld, want, Vector3.up);
        m_ringLinesRoot.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    void RefreshTimerTexts(float washerId, float dryerId)
    {
        int w = Mathf.RoundToInt(washerId);
        int d = Mathf.RoundToInt(dryerId);
        m_statusText.text = "Washer " + w.ToString("00") + "\nDryer " + d.ToString("00");
    }

    void RefreshTimeDigits()
    {
        int rem = Mathf.Max(0, Mathf.RoundToInt(m_remainingSeconds));
        int h = rem / 3600;
        int mi = (rem % 3600) / 60;
        int s = rem % 60;
        m_timeText.text = h.ToString("00") + ":" + mi.ToString("00") + ":" + s.ToString("00");
    }

    void LateUpdate()
    {
        if (m_timerMode)
        {
            OrientRingGapTowardPresentationAnchor();
            RebuildArcMesh(m_remainingSeconds / m_totalCountdownSeconds);
        }

        if (m_follow == null || !m_timerMode)
            return;

        var p = m_follow.position;
        var flatPlayer = new Vector3(p.x, m_tableSurfaceY, p.z);
        var nominal = Vector3.Lerp(
            new Vector3(m_seatWorldSpot.x, m_tableSurfaceY, m_seatWorldSpot.z),
            flatPlayer,
            followSeatVersusPlayerBlend);
        var target = PushCreatureTowardTableFromAnchor(nominal);
        transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 6f);
    }

    void Update()
    {
        if (!m_timerMode || !m_countdownActive) return;
        m_remainingSeconds -= Time.deltaTime;
        if (m_remainingSeconds < 0f) m_remainingSeconds = 0f;
        RefreshTimeDigits();
    }

    void RebuildArcMesh(float remaining01)
    {
        if (m_ringLinesRoot == null)
            return;

        remaining01 = Mathf.Clamp01(remaining01);
        float startDeg = -45f;
        float spanDeg = 270f;
        int seg = ringSegments;
        float elapsed01 = 1f - remaining01;
        int split = Mathf.Clamp(Mathf.RoundToInt(seg * elapsed01), 0, seg);

        float coreEdge = coreDiameterMeters * 0.5f + ringGapBeyondCoreMeters;
        float emergence = Mathf.Clamp01(m_ringEmergenceT);
        float arcRadius = Mathf.Lerp(coreEdge, ringRadiusMeters, emergence);

        FillArc(m_elapsedArc, startDeg, spanDeg, 0, split, arcRadius);
        FillArc(m_remainingArc, startDeg, spanDeg, split, seg, arcRadius);
    }

    void FillArc(LineRenderer lr, float startDeg, float spanDeg, int i0, int i1, float radius)
    {
        if (i1 <= i0)
        {
            lr.positionCount = 0;
            lr.enabled = false;
            return;
        }

        lr.enabled = true;
        lr.positionCount = i1 - i0 + 1;
        int w = 0;
        for (int i = i0; i <= i1; i++)
        {
            float t = i / (float)ringSegments;
            float deg = startDeg + t * spanDeg;
            float rad = deg * Mathf.Deg2Rad;
            var localOff = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            lr.SetPosition(w++, m_ringLinesRoot.TransformPoint(localOff));
        }
    }
}
