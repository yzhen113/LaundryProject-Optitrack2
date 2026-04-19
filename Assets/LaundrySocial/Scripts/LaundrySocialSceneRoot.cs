using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Table + creatures. Two flows: (1) standing footprints + bump, or (2) both users stand in floor footprints
/// (host + guest activation) → creatures appear and guest leads to diagonal seat → morph to ring + countdown.
/// </summary>
public class LaundrySocialSceneRoot : MonoBehaviour
{
    public enum LaundrySocialSceneFlowKind
    {
        [Tooltip("Both users in floor rectangles → creature bump only.")]
        StandingZonesBumpOnly,
        [Tooltip("Host and guest must stand in their floor footprints; then both creatures appear and guest follows hand, jumps to diagonal seat, morphs to timer ring.")]
        CreatureAppearLeadIn
    }

    [Header("Scene flow")]
    [Tooltip("CreatureAppear.unity uses Creature Appear Lead In (both users in floor zones). Bump scenes use Standing Zones Bump Only.")]
    public LaundrySocialSceneFlowKind sceneFlow = LaundrySocialSceneFlowKind.StandingZonesBumpOnly;

    [Header("Prefabs")]
    [Tooltip("Assign Assets/Mini First Person Controller/First Person Controller.prefab for the intended look; otherwise capsules are spawned.")]
    public GameObject firstPersonPrefab;

    [Header("OptiTrack (optional)")]
    [Tooltip("Motive rigid body ID for the host (playerIndex 0). Used if the player prefab root has OptitrackRigidBody.")]
    public int optitrackRigidBodyIdHost = 1;
    [Tooltip("Motive rigid body ID for the guest (playerIndex 1). Used if the player prefab root has OptitrackRigidBody.")]
    public int optitrackRigidBodyIdGuest = 2;
    [Tooltip("If off, FloorSimulatedMover does not overwrite Rigidbody velocity (use with OptiTrack on the player root).")]
    public bool enableKeyboardFloorSimulation = true;

    [Header("Table (meters)")]
    public float tableHalfExtentX = 4f;
    public float tableHalfExtentZ = 2f;
    public float tableTopY = 0f;
    public float seatInset = 0.35f;

    [Header("User zone highlights (floor)")]
    [Tooltip("LineRenderer width for all user footprint / activation outlines.")]
    public float userZoneOutlineWidth = 0.1f;
    [Tooltip("Creature Appear flow: floor outline for the guest laundry activation trigger (XZ matches trigger box).")]
    public Color leadInGuestActivationZoneColor = new Color(1f, 0.42f, 0.1f, 1f);

    [Header("Standing zones (standing flow + lead-in host wait)")]
    [Tooltip("Host seat index (0–5). Guest is placed diagonally across the table.")]
    public int seatedHostSeatIndex = 0;
    [Tooltip("Half-width of each standing rectangle on X (meters).")]
    public float standingZoneHalfExtentX = 0.55f;
    [Tooltip("Half-depth of each standing rectangle on Z (meters).")]
    public float standingZoneHalfExtentZ = 0.55f;
    [Tooltip("Color for the host footprint outline.")]
    public Color hostZoneColor = new Color(0.28f, 0.72f, 0.95f, 0.95f);
    [Tooltip("Color for the guest footprint outline.")]
    public Color guestZoneColor = new Color(0.95f, 0.62f, 0.28f, 0.95f);

    [Header("Creature appear lead-in (Creature Appear flow only)")]
    [Tooltip("Seconds the creature follows the guest’s hand before jumping toward the diagonal seat.")]
    public float handPreviewSeconds = 2.1f;
    [Tooltip("Seconds for the arc jump from hand area to the seat.")]
    public float jumpToSeatSeconds = 1.15f;

    [Header("Creatures")]
    public float demoCountdownSeconds = 16f;
    [Tooltip("Slides creature + timer on the table away from the standing player toward the table center (meters).")]
    public float creatureTableOffsetFromPlayerMeters = 0.75f;
    [Tooltip("Host and guest use the same value: blends creature anchor between seat and player.")]
    [Range(0f, 1f)]
    public float creatureFollowPlayerBlend = 0.06f;
    public float guestWasherNumber = 3f;
    public float guestDryerNumber = 4f;

    [Header("Creature bump (standing flow only)")]
    [Tooltip("If true, cores bump when both users stand in their floor zones.")]
    public bool enableCreatureBumpInteraction = true;
    [Tooltip("Delay after both are in zone before the bump starts.")]
    public float bumpDelayAfterBothStandingSeconds = 0.35f;
    [Tooltip("If true, travel distance is computed so cores exit the ring and meet at the midpoint.")]
    public bool bumpAutoDistanceToTouch = true;
    public float bumpRingExitPaddingMeters = 0.06f;
    public float bumpTravelScale = 1f;
    public float bumpCoreTravelMeters = 0.35f;
    public float bumpDurationSeconds = 8f;

    [Header("Projection mapping")]
    [Tooltip("If enabled, Main Camera only renders creature HUDs (world canvas + ring arcs). Table mesh, avatars, and floor zone outlines stay on Default and are hidden.")]
    public bool projectionMainCameraCreaturesOnly;
    [Tooltip("Layer name assigned to both creature roots (must exist in Tags & Layers).")]
    public string creatureProjectionLayerName = "CreatureProjection";
    [Tooltip("If true, clear color alpha is 0 (needs transparent framebuffer / window). If false, opaque black (typical for projector).")]
    public bool projectionTransparentClearColor;

    [Header("Debug (standing flow)")]
    public KeyCode restartBumpInteractionKey = KeyCode.R;
    public bool enableRestartBumpHotkey = true;
    public bool restartBumpSkipsDelay = true;

    SocialTableLayout m_layout;
    LaundryActivationZone m_zone;
    LaundryTrackedPerson m_guest;
    LaundryTrackedPerson m_host;
    CreatureWorldHud m_hostCreature;
    CreatureWorldHud m_guestCreature;

    Vector3 m_hostStandCenter;
    Vector3 m_guestStandCenter;
    int m_guestSeatIndex;
    Vector3 m_guestSeatWorld;

    Coroutine m_bumpCoordinatorRoutine;
    Coroutine m_bumpHostRoutine;
    Coroutine m_bumpGuestRoutine;

    bool m_exitSinceLastBump = true;
    bool m_bumpRunning;
    bool m_guestSequenceStarted;
    bool m_leadInBothZonesArmed = true;

    /// <summary>Standing flow: ready after spawn. Lead-in flow: true after guest arrival + morph completes.</summary>
    public bool StandingInteractionReady { get; private set; }

    [Obsolete("Use StandingInteractionReady.")]
    public bool GuestArrivalSequenceComplete => StandingInteractionReady;

    protected virtual void Awake()
    {
        EnsureEventSystem();
        BuildTableAndSeats();

        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
        {
            BuildLaundryZone();
            BuildLeadInUserZoneHighlights();
        }

        SetupTopDownCamera();

        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
            SpawnCreatureAppearLeadIn();
        else
            SpawnStandingBumpFlow();

        if (sceneFlow == LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            BuildStandingZoneOutlines();

        if (projectionMainCameraCreaturesOnly)
            ApplyProjectionCreaturesOnlyCamera();

        if (sceneFlow == LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            StandingInteractionReady = true;
    }

    IEnumerator GuestArrivalRoutine()
    {
        if (m_hostCreature != null)
            m_hostCreature.gameObject.SetActive(true);

        var tableCenter = new Vector3(0f, tableTopY, 0f);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);

        m_guestCreature.gameObject.SetActive(true);
        yield return StartCoroutine(m_guestCreature.PlayArrivalSequence(
            m_guest.handAnchor != null ? m_guest.handAnchor : m_guest.transform,
            handPreviewSeconds,
            m_guestSeatWorld,
            jumpToSeatSeconds,
            tableTopY,
            demoCountdownSeconds,
            guestWasherNumber,
            guestDryerNumber));

        m_guestCreature.ConfigureTableFollow(m_guest.transform, m_guestSeatWorld, tableTopY);
        StandingInteractionReady = true;
    }

    void Update()
    {
        if (sceneFlow != LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            return;
        if (!enableRestartBumpHotkey || !enableCreatureBumpInteraction || !StandingInteractionReady)
            return;
        if (!Input.GetKeyDown(restartBumpInteractionKey))
            return;
        RestartCreatureBumpInteraction();
    }

    void FixedUpdate()
    {
        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
        {
            if (m_guestSequenceStarted || m_host == null || m_guest == null || m_hostCreature == null || m_guestCreature == null)
                return;

            Vector3 hostFootprint = StandCenterForSeatIndex(seatedHostSeatIndex);
            bool leadHostIn = StandingZoneOutline.ContainsXZ(m_host.transform.position, hostFootprint, standingZoneHalfExtentX, standingZoneHalfExtentZ);
            GetLeadInActivationFootprint(out Vector3 actCenter, out float actHx, out float actHz);
            bool leadGuestIn = StandingZoneOutline.ContainsXZ(m_guest.transform.position, actCenter, actHx, actHz);

            if (!leadHostIn || !leadGuestIn)
            {
                m_leadInBothZonesArmed = true;
                return;
            }

            if (!m_leadInBothZonesArmed)
                return;

            m_leadInBothZonesArmed = false;
            m_guestSequenceStarted = true;
            StartCoroutine(GuestArrivalRoutine());
            return;
        }

        if (sceneFlow != LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            return;
        if (!enableCreatureBumpInteraction || m_host == null || m_guest == null)
            return;

        bool hostIn = StandingZoneOutline.ContainsXZ(m_host.transform.position, m_hostStandCenter, standingZoneHalfExtentX, standingZoneHalfExtentZ);
        bool guestIn = StandingZoneOutline.ContainsXZ(m_guest.transform.position, m_guestStandCenter, standingZoneHalfExtentX, standingZoneHalfExtentZ);
        bool bothIn = hostIn && guestIn;

        if (!bothIn)
        {
            m_exitSinceLastBump = true;
            return;
        }

        if (!m_exitSinceLastBump || m_bumpRunning || m_bumpCoordinatorRoutine != null)
            return;

        m_exitSinceLastBump = false;
        m_bumpCoordinatorRoutine = StartCoroutine(RunCreatureBumpInteraction(false));
    }

    public void RestartCreatureBumpInteraction()
    {
        if (sceneFlow != LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            return;
        if (!enableCreatureBumpInteraction || m_hostCreature == null || m_guestCreature == null)
            return;

        StopActiveBumpCoroutines();
        m_hostCreature.ResetCoreSlideImmediate();
        m_guestCreature.ResetCoreSlideImmediate();
        m_exitSinceLastBump = false;
        m_bumpCoordinatorRoutine = StartCoroutine(RunCreatureBumpInteraction(restartBumpSkipsDelay));
    }

    void StopActiveBumpCoroutines()
    {
        if (m_bumpCoordinatorRoutine != null)
        {
            StopCoroutine(m_bumpCoordinatorRoutine);
            m_bumpCoordinatorRoutine = null;
        }

        if (m_bumpHostRoutine != null)
        {
            StopCoroutine(m_bumpHostRoutine);
            m_bumpHostRoutine = null;
        }

        if (m_bumpGuestRoutine != null)
        {
            StopCoroutine(m_bumpGuestRoutine);
            m_bumpGuestRoutine = null;
        }

        m_bumpRunning = false;
    }

    IEnumerator RunCreatureBumpInteraction(bool skipInitialDelay)
    {
        m_bumpRunning = true;
        if (!skipInitialDelay)
            yield return new WaitForSeconds(bumpDelayAfterBothStandingSeconds);
        if (m_hostCreature == null || m_guestCreature == null)
        {
            m_bumpRunning = false;
            m_bumpCoordinatorRoutine = null;
            yield break;
        }

        int pending = 2;
        void Release()
        {
            pending--;
        }

        m_bumpHostRoutine = StartCoroutine(BumpOneCreature(m_hostCreature, m_guestCreature.transform.position, Release));
        m_bumpGuestRoutine = StartCoroutine(BumpOneCreature(m_guestCreature, m_hostCreature.transform.position, Release));
        yield return new WaitUntil(() => pending <= 0);
        m_bumpHostRoutine = null;
        m_bumpGuestRoutine = null;
        m_bumpCoordinatorRoutine = null;
        m_bumpRunning = false;
    }

    IEnumerator BumpOneCreature(CreatureWorldHud creature, Vector3 peerRootWorld, Action onDone)
    {
        try
        {
            float travel = bumpAutoDistanceToTouch
                ? creature.ComputeBumpTravelMetersTowardPeer(peerRootWorld, bumpRingExitPaddingMeters, bumpTravelScale)
                : bumpCoreTravelMeters;
            yield return StartCoroutine(creature.PlayCoreBumpTowardPeerAndReturn(
                peerRootWorld,
                travel,
                bumpDurationSeconds));
        }
        finally
        {
            onDone?.Invoke();
        }
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
        var es = new GameObject("EventSystem");
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
    }

    void BuildTableAndSeats()
    {
        var tableGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tableGo.name = "ProjectionTable";
        tableGo.transform.SetParent(transform, false);
        tableGo.transform.localPosition = new Vector3(0f, tableTopY - 0.05f, 0f);
        tableGo.transform.localScale = new Vector3(tableHalfExtentX * 2f, 0.1f, tableHalfExtentZ * 2f);
        var tb = tableGo.GetComponent<Renderer>();
        if (tb != null)
        {
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.92f, 0.9f, 0.86f);
            tb.material = mat;
        }

        var layoutGo = new GameObject("TableLayout");
        layoutGo.transform.SetParent(transform, false);
        m_layout = layoutGo.AddComponent<SocialTableLayout>();
        m_layout.tableRoot = tableGo.transform;
        m_layout.tableHalfExtentX = tableHalfExtentX;
        m_layout.tableHalfExtentZ = tableHalfExtentZ;
        m_layout.seatInset = seatInset;
        m_layout.seats = new Transform[6];

        float southZ = -tableHalfExtentZ + seatInset;
        float northZ = tableHalfExtentZ - seatInset;
        float[] xs = { -tableHalfExtentX + seatInset, 0f, tableHalfExtentX - seatInset };
        for (int i = 0; i < 3; i++)
        {
            m_layout.seats[i] = CreateSeatMarker("Seat_South_" + i, new Vector3(xs[i], tableTopY + 0.01f, southZ));
            m_layout.seats[i + 3] = CreateSeatMarker("Seat_North_" + i, new Vector3(xs[i], tableTopY + 0.01f, northZ));
        }
    }

    void BuildLaundryZone()
    {
        var z = GameObject.CreatePrimitive(PrimitiveType.Cube);
        z.name = "LaundryActivationZone";
        z.transform.SetParent(transform, false);
        z.transform.localPosition = new Vector3(-tableHalfExtentX - 2.4f, 0.5f, tableHalfExtentZ + 0.6f);
        z.transform.localScale = new Vector3(2.2f, 1f, 2.2f);
        var col = z.GetComponent<BoxCollider>();
        col.isTrigger = true;
        var rend = z.GetComponent<Renderer>();
        if (rend != null) Destroy(rend);
        m_zone = z.AddComponent<LaundryActivationZone>();
    }

    Transform CreateSeatMarker(string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(m_layout.transform, false);
        t.position = pos;
        return t;
    }

    Vector3 StandCenterForSeatIndex(int seatIndex)
    {
        var s = m_layout.seats[seatIndex];
        float zOff = seatIndex < 3 ? -0.85f : 0.85f;
        var p = s.position;
        return new Vector3(p.x, tableTopY + 0.02f, p.z + zOff);
    }

    void BuildStandingZoneOutlines()
    {
        m_guestSeatIndex = SocialTableLayout.DiagonalSeatAcrossFrom(seatedHostSeatIndex);

        m_hostStandCenter = StandCenterForSeatIndex(seatedHostSeatIndex);
        m_guestStandCenter = StandCenterForSeatIndex(m_guestSeatIndex);

        var root = new GameObject("StandingZones").transform;
        root.SetParent(transform, false);

        CreateZoneOutline(root, "HostStandingZone", m_hostStandCenter, hostZoneColor, standingZoneHalfExtentX, standingZoneHalfExtentZ);
        CreateZoneOutline(root, "GuestStandingZone", m_guestStandCenter, guestZoneColor, standingZoneHalfExtentX, standingZoneHalfExtentZ);
    }

    /// <summary>Floor rectangles: guest must enter trigger footprint; host waits at seat-side footprint.</summary>
    void GetLeadInActivationFootprint(out Vector3 centerWorld, out float halfExtentX, out float halfExtentZ)
    {
        halfExtentX = 1.1f;
        halfExtentZ = 1.1f;
        var actLocal = new Vector3(-tableHalfExtentX - 2.4f, tableTopY + 0.03f, tableHalfExtentZ + 0.6f);
        centerWorld = transform.TransformPoint(actLocal);
    }

    void BuildLeadInUserZoneHighlights()
    {
        var root = new GameObject("UserZoneHighlights").transform;
        root.SetParent(transform, false);

        GetLeadInActivationFootprint(out Vector3 actCenter, out float activationTriggerHalfX, out float activationTriggerHalfZ);

        CreateZoneOutline(
            root,
            "GuestActivationHighlight",
            actCenter,
            leadInGuestActivationZoneColor,
            activationTriggerHalfX,
            activationTriggerHalfZ);

        Vector3 hostCenter = StandCenterForSeatIndex(seatedHostSeatIndex);
        CreateZoneOutline(
            root,
            "HostWaitHighlight",
            hostCenter,
            hostZoneColor,
            standingZoneHalfExtentX,
            standingZoneHalfExtentZ);
    }

    void CreateZoneOutline(Transform parent, string name, Vector3 center, Color col, float halfExtentX, float halfExtentZ)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var outline = go.AddComponent<StandingZoneOutline>();
        outline.halfExtentX = halfExtentX;
        outline.halfExtentZ = halfExtentZ;
        outline.lineColor = col;
        outline.lineWidth = userZoneOutlineWidth;
        outline.Rebuild();
    }

    void SetupTopDownCamera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var cgo = new GameObject("TopDownCamera");
            cam = cgo.AddComponent<Camera>();
            cgo.tag = "MainCamera";
        }

        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(tableHalfExtentX, tableHalfExtentZ) + 3.5f;
        cam.transform.position = new Vector3(0f, 14f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.Skybox;
    }

    void SpawnCreatureAppearLeadIn()
    {
        m_guestSeatIndex = SocialTableLayout.DiagonalSeatAcrossFrom(seatedHostSeatIndex);
        m_guestSeatWorld = m_layout.seats[m_guestSeatIndex].position;

        m_host = SpawnPerson("Host_Player", seatedHostSeatIndex, 0, true);
        m_guest = SpawnPerson("Guest_Player", -1, 1, false);

        var ctex = Resources.Load<Texture2D>("LaundrySocial/creature");
        var ttex = Resources.Load<Texture2D>("LaundrySocial/timer1");

        var tableCenter = new Vector3(0f, tableTopY, 0f);

        m_hostCreature = CreateCreature("Creature_Host", m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.Build(ctex, ttex);
        m_hostCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_hostCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_hostCreature.SetTablePresentationContext(m_host.transform, tableCenter, tableTopY);
        m_hostCreature.ConfigureTableFollow(m_host.transform, m_layout.seats[seatedHostSeatIndex].position, tableTopY);
        m_hostCreature.SnapCreatureToSeatOnTable(m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.SetTimerModeImmediate(demoCountdownSeconds + 120f, 2f, 1f);
        m_hostCreature.gameObject.SetActive(false);

        m_guestCreature = CreateCreature("Creature_Guest", m_guest.transform.position);
        m_guestCreature.Build(ctex, ttex);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);
        m_guestCreature.gameObject.SetActive(false);
    }

    void SpawnStandingBumpFlow()
    {
        m_guestSeatIndex = SocialTableLayout.DiagonalSeatAcrossFrom(seatedHostSeatIndex);

        m_host = SpawnPerson("Host_Player", seatedHostSeatIndex, 0, true);
        m_guest = SpawnPerson("Guest_Player", m_guestSeatIndex, 1, true);

        var ctex = Resources.Load<Texture2D>("LaundrySocial/creature");
        var ttex = Resources.Load<Texture2D>("LaundrySocial/timer1");

        var tableCenter = new Vector3(0f, tableTopY, 0f);

        m_hostCreature = CreateCreature("Creature_Host", m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.Build(ctex, ttex);
        m_hostCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_hostCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_hostCreature.SetTablePresentationContext(m_host.transform, tableCenter, tableTopY);
        m_hostCreature.ConfigureTableFollow(m_host.transform, m_layout.seats[seatedHostSeatIndex].position, tableTopY);
        m_hostCreature.SnapCreatureToSeatOnTable(m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.SetTimerModeImmediate(demoCountdownSeconds + 120f, 2f, 1f);

        m_guestCreature = CreateCreature("Creature_Guest", m_layout.seats[m_guestSeatIndex].position);
        m_guestCreature.Build(ctex, ttex);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);
        m_guestCreature.ConfigureTableFollow(m_guest.transform, m_layout.seats[m_guestSeatIndex].position, tableTopY);
        m_guestCreature.SnapCreatureToSeatOnTable(m_layout.seats[m_guestSeatIndex].position);
        m_guestCreature.SetTimerModeImmediate(demoCountdownSeconds, guestWasherNumber, guestDryerNumber);
    }

    LaundryTrackedPerson SpawnPerson(string objectName, int seatIndexOrNeg, int playerIndex, bool atSeat)
    {
        Vector3 pos;
        Quaternion rot = Quaternion.identity;
        if (atSeat && seatIndexOrNeg >= 0 && seatIndexOrNeg < m_layout.seats.Length)
        {
            var s = m_layout.seats[seatIndexOrNeg];
            pos = s.position + new Vector3(0f, 0f, seatIndexOrNeg < 3 ? -0.85f : 0.85f);
            rot = Quaternion.LookRotation(Vector3.forward * (seatIndexOrNeg < 3 ? 1f : -1f), Vector3.up);
        }
        else
        {
            var zoneCenter = new Vector3(-tableHalfExtentX - 2.4f, 0.2f, tableHalfExtentZ + 0.6f);
            pos = zoneCenter + new Vector3(1.45f, 0f, 0f);
            rot = Quaternion.LookRotation(Vector3.left, Vector3.up);
        }

        GameObject root;
        if (firstPersonPrefab != null)
        {
            root = Instantiate(firstPersonPrefab, pos, rot);
            root.name = objectName;
            foreach (var c in root.GetComponentsInChildren<Camera>(true))
                c.enabled = false;
        }
        else
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = objectName;
            root.transform.SetPositionAndRotation(pos, rot);
            root.transform.localScale = new Vector3(0.45f, 0.9f, 0.45f);
            var rb = root.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        root.tag = "Player";
        var tracked = root.GetComponent<LaundryTrackedPerson>();
        if (tracked == null) tracked = root.AddComponent<LaundryTrackedPerson>();
        tracked.playerIndex = playerIndex;
        if (tracked.handAnchor == null)
        {
            var h = new GameObject("HandAnchor").transform;
            h.SetParent(root.transform, false);
            h.localPosition = new Vector3(0.35f, 1.45f, 0.35f);
            tracked.handAnchor = h;
        }

        var mover = root.GetComponent<FloorSimulatedMover>();
        if (mover == null) mover = root.AddComponent<FloorSimulatedMover>();
        mover.playerIndex = playerIndex;
        if (enableKeyboardFloorSimulation)
            mover.SetSimulated(true);
        else
            mover.DisableKeyboardFloorDrive();

        if (root.GetComponent<TopDownCursorUnlock>() == null)
            root.AddComponent<TopDownCursorUnlock>();

        if (root.GetComponent<Rigidbody>() == null)
        {
            var rb = root.AddComponent<Rigidbody>();
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        var optiBody = root.GetComponent<OptitrackRigidBody>();
        if (optiBody != null)
            optiBody.RigidBodyId = playerIndex == 0 ? optitrackRigidBodyIdHost : optitrackRigidBodyIdGuest;

        return tracked;
    }

    CreatureWorldHud CreateCreature(string name, Vector3 at)
    {
        var go = new GameObject(name);
        go.transform.position = at + Vector3.up * 0.05f;
        return go.AddComponent<CreatureWorldHud>();
    }

    void ApplyProjectionCreaturesOnlyCamera()
    {
        int layer = LayerMask.NameToLayer(creatureProjectionLayerName);
        if (layer < 0)
        {
            Debug.LogWarning("[LaundrySocialSceneRoot] Layer '" + creatureProjectionLayerName + "' not found. Add it under Edit > Project Settings > Tags and Layers (User Layer), then re-run.");
            return;
        }

        if (m_hostCreature != null)
            SetLayerRecursively(m_hostCreature.gameObject, layer);
        if (m_guestCreature != null)
            SetLayerRecursively(m_guestCreature.gameObject, layer);

        var cam = Camera.main;
        if (cam == null)
            return;

        cam.cullingMask = 1 << layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        if (projectionTransparentClearColor)
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        else
            cam.backgroundColor = Color.black;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        var t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursively(t.GetChild(i).gameObject, layer);
    }
}
