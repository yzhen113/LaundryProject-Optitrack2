using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public int optitrackRigidBodyIdHost = 601;
    [Tooltip("Motive rigid body ID for the guest (playerIndex 1). Used if the player prefab root has OptitrackRigidBody.")]
    public int optitrackRigidBodyIdGuest = 602;
    [Tooltip("If off, FloorSimulatedMover does not overwrite Rigidbody velocity (use with OptiTrack on the player root).")]
    public bool enableKeyboardFloorSimulation = true;
    [Tooltip("Creature Appear lead-in: when keyboard sim is off, zone checks use Motive rigid-body poses (IDs above). Leave unset to auto-find the scene streaming client.")]
    public OptitrackStreamingClient optitrackStreamingClient;
    [Tooltip("Each spawned player is parented under an empty named e.g. Host_Player_TrackedAlignment. Move/rotate that empty to align Motive coordinates with this scene (prefab root stays at local origin).")]
    public bool spawnPlayersUnderTrackedAlignmentParent = true;

    [Header("Table (meters)")]
    [Tooltip("Half extent along local X for a 60\" × 30\" tabletop (full length along X = 2 × this). Example: 60\" → ~0.762 m half extent.")]
    public float tableHalfExtentX = 0.762f;
    [Tooltip("Half extent along local Z (30\" edge). Ignored unless Allow Independent Table Half Extents is enabled; otherwise Z = X × (30/60).")]
    public float tableHalfExtentZ = 0.381f;
    [Tooltip("When disabled (default), half extent Z is always X × (30/60) for a 60\"×30\" plan. Enable only for non-standard tables.")]
    public bool allowIndependentTableHalfExtents;
    public float tableTopY = 0f;
    public float seatInset = 0.35f;

    [Header("User zone highlights (floor)")]
    [Tooltip("LineRenderer width for all user footprint / activation outlines.")]
    public float userZoneOutlineWidth = 0.1f;
    [Tooltip("Creature Appear lead-in: ORANGE outline — drawn at host seat footprint; host must stand here.")]
    public Color leadInGuestActivationZoneColor = new Color(1f, 0.42f, 0.1f, 1f);

    [Header("Standing zones (standing flow + lead-in host wait)")]
    [Tooltip("Host seat index (0–5). Guest is placed diagonally across the table.")]
    public int seatedHostSeatIndex = 0;
    [Tooltip("Half-width of each standing rectangle on X (meters).")]
    public float standingZoneHalfExtentX = 0.55f;
    [Tooltip("Half-depth of each standing rectangle on Z (meters).")]
    public float standingZoneHalfExtentZ = 0.55f;
    [Tooltip("Caps standing footprints so they stay proportional to the table (each axis ≤ this fraction of that axis half-extent).")]
    [Range(0.15f, 0.55f)]
    public float standingZoneMaxFractionOfTableHalf = 0.38f;
    [Tooltip("How far past the seat edge (along ±Z) the host/guest stand footprint center sits, as a fraction of table half-depth.")]
    [Range(0.15f, 0.55f)]
    public float seatStandOffsetFractionOfTableHalfZ = 0.42f;
    [Tooltip("Creature Appear lead-in: BLUE outline — drawn at laundry activation; guest must stand here (XZ matches trigger). Standing bump flow: host footprint color.")]
    public Color hostZoneColor = new Color(0.28f, 0.72f, 0.95f, 0.95f);
    [Tooltip("Color for the guest footprint outline.")]
    public Color guestZoneColor = new Color(0.95f, 0.62f, 0.28f, 0.95f);

    [Header("Creature appear lead-in (Creature Appear flow only)")]
    [Tooltip("Guest activation footprint half-size uses min(short table axis × this, table caps).")]
    [Range(0.2f, 0.55f)]
    public float guestActivationFootprintFractionOfShortAxis = 0.38f;
    [Tooltip("Gap outside the table edge before the guest activation box, as a fraction of the short table half-axis.")]
    [Range(0.05f, 0.35f)]
    public float guestActivationGapFractionOfShortAxis = 0.12f;
    [Tooltip("Lead-in only: enlarge the laundry activation footprint so both players can fit (trigger, outline, and gate).")]
    public float leadInActivationFootprintScale = 1.28f;
    [Tooltip("Lead-in only: LineRenderer width for the shared activation rectangle.")]
    public float leadInActivationZoneOutlineWidth = 0.014f;
    [Tooltip("Lead-in orange zone + outline: extra meters along ±Z (standing direction) away from the table center so the footprint sits on the floor past the table edge.")]
    public float leadInOrangeOutlineAwayFromTableMeters = 0.1f;
    [Tooltip("Lead-in orange outline only: LineRenderer world Y = tableTopY + this (negative draws below the tabletop on the floor).")]
    public float leadInOrangeOutlineLineYOffsetFromTableTop = -0.055f;
    [Tooltip("Lead-in: after host + guest are in orange/blue, wait this many seconds before creatures appear.")]
    public float leadInSecondsBeforeCreatureAppears = 2f;
    [Tooltip("Lead-in: after creatures appear, seconds the guest creature follows the hand before jumping toward the diagonal seat.")]
    public float handPreviewSeconds = 8f;
    [Tooltip("Seconds for the arc jump from hand area to the seat.")]
    public float jumpToSeatSeconds = 1.15f;

    [Header("Creatures")]
    public float demoCountdownSeconds = 16f;
    [Tooltip("Slides creature + timer on the table away from the standing player toward the table center (meters). Host uses this value as-is.")]
    public float creatureTableOffsetFromPlayerMeters = 0.75f;
    [Tooltip("Guest creature only: same idea as Creature Table Offset — meters toward table center from the blended seat/player anchor. Lower keeps the timer HUD closer to the outer table edge.")]
    public float guestCreatureOffsetTowardCenterMeters = 0.11f;
    [Tooltip("Host and guest use the same value: blends creature anchor between seat and player.")]
    [Range(0f, 1f)]
    public float creatureFollowPlayerBlend = 0.06f;
    public float guestWasherNumber = 3f;
    public float guestDryerNumber = 4f;
    [Tooltip("Guest creature timer only: scales how fast the displayed countdown decreases. 1 = real-time; 0.5 ≈ half speed (takes ~2× wall time to finish). Host is unchanged.")]
    [Range(0.05f, 2f)]
    public float guestCountdownElapsedScale = 0.5f;

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
    [Tooltip("Extra meters around the table + standing/activation footprints so the projector shows surrounding zones (not only the tabletop).")]
    public float projectionViewPaddingMeters = 0.38f;
    [Tooltip("After padding, scales the capture rectangle around its center (>1 = zoom out / capture more floor around the table).")]
    public float projectionViewMarginMultiplier = 1.28f;
    [Tooltip("Orthographic camera height above the table plane (world Y).")]
    public float topDownCameraHeight = 14f;

    [Header("Top-down scale (players + creature/timer HUD)")]
    [Tooltip("Uniform scale applied to spawned player prefab roots. If <= 0, uses ShortTableHalfAxis × playerPrefabAutoScaleFactor.")]
    public float playerPrefabUniformScale = -1f;
    public float playerPrefabAutoScaleFactor = 0.82f;
    [Tooltip("Uniform scale applied to CreatureWorldHud roots after Build (core + ring + timer text). If <= 0, uses ShortTableHalfAxis × creatureHudAutoScaleFactor.")]
    public float creatureHudUniformScale = -1f;
    public float creatureHudAutoScaleFactor = 0.58f;

    [Header("Play mode")]
    [Tooltip("Press during Play to reload the active scene and restart the interaction from scratch (zones, creatures, timers). Scene must be in Build Settings.")]
    public KeyCode restartInteractionHotkey = KeyCode.F5;
    public bool enableRestartInteractionHotkey = true;
    [Tooltip("Creature Appear lead-in only: reset creatures and lead-in state without reloading the scene (keeps TrackedAlignment empty transforms).")]
    public KeyCode resetLeadInCreaturesHotkey = KeyCode.F6;
    public bool enableResetLeadInCreaturesHotkey = true;

    [Header("Debug (standing flow)")]
    public KeyCode restartBumpInteractionKey = KeyCode.R;
    public bool enableRestartBumpHotkey = true;
    public bool restartBumpSkipsDelay = true;

    [Header("Debug (Creature Appear lead-in)")]
    [Tooltip("Log when the lead-in sequence starts and when host/guest timers become active.")]
    public bool logLeadInTimerEvents = true;
    [Tooltip("While waiting: log orange-zone checks every N seconds (0 = off). Shows world positions vs Motive/Unity mismatch.")]
    public float logLeadInZoneDiagnosticsInterval = 2f;

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
    float m_lastLeadInZoneDiagLogTime;
    OptitrackStreamingClient m_cachedOptitrackStreamingClient;
    Coroutine m_guestArrivalRoutine;
    Coroutine m_leadInActivationDelayRoutine;

    /// <summary>Standing flow: ready after spawn. Lead-in flow: true after guest arrival + morph completes.</summary>
    public bool StandingInteractionReady { get; private set; }

    [Obsolete("Use StandingInteractionReady.")]
    public bool GuestArrivalSequenceComplete => StandingInteractionReady;

    float EffectiveTableHalfExtentX => tableHalfExtentX;

    float EffectiveTableHalfExtentZ =>
        allowIndependentTableHalfExtents ? tableHalfExtentZ : tableHalfExtentX * (30f / 60f);

    float ShortTableHalfAxis => Mathf.Min(EffectiveTableHalfExtentX, EffectiveTableHalfExtentZ);

    float EffectiveStandingZoneHalfExtentX =>
        Mathf.Min(standingZoneHalfExtentX, EffectiveTableHalfExtentX * standingZoneMaxFractionOfTableHalf);

    float EffectiveStandingZoneHalfExtentZ =>
        Mathf.Min(standingZoneHalfExtentZ, EffectiveTableHalfExtentZ * standingZoneMaxFractionOfTableHalf);

    float SeatStandForwardOffset =>
        Mathf.Clamp(EffectiveTableHalfExtentZ * seatStandOffsetFractionOfTableHalfZ, 0.06f, 0.95f);

    void OnValidate()
    {
        if (!allowIndependentTableHalfExtents)
            tableHalfExtentZ = EffectiveTableHalfExtentZ;
    }

    float EffectiveUserZoneOutlineWidth =>
        Mathf.Clamp(Mathf.Min(userZoneOutlineWidth, ShortTableHalfAxis * 0.14f), 0.012f, 0.25f);

    float EffectiveLeadInActivationOutlineWidth =>
        Mathf.Clamp(Mathf.Min(leadInActivationZoneOutlineWidth, ShortTableHalfAxis * 0.048f), 0.004f, 0.12f);

    float EffectivePlayerPrefabScale =>
        playerPrefabUniformScale > 0f
            ? Mathf.Clamp(playerPrefabUniformScale, 0.14f, 1.6f)
            : Mathf.Clamp(ShortTableHalfAxis * playerPrefabAutoScaleFactor, 0.16f, 1f);

    float EffectiveCreatureHudScale =>
        creatureHudUniformScale > 0f
            ? Mathf.Clamp(creatureHudUniformScale, 0.08f, 1f)
            : Mathf.Clamp(ShortTableHalfAxis * creatureHudAutoScaleFactor, 0.1f, 0.72f);

    OptitrackStreamingClient ActiveOptitrackStreamingClient
    {
        get
        {
            if (optitrackStreamingClient != null)
                return optitrackStreamingClient;
            if (m_cachedOptitrackStreamingClient != null)
                return m_cachedOptitrackStreamingClient;
            var found = FindObjectsOfType<OptitrackStreamingClient>();
            if (found.Length > 0)
                m_cachedOptitrackStreamingClient = found[0];
            return m_cachedOptitrackStreamingClient;
        }
    }

    /// <summary>
    /// Lead-in presence in world XZ: keyboard sim uses the spawned root; OptiTrack uses streamed RB pose when tracked (avoids false triggers while avatars sit on spawn poses inside zones).
    /// Streamed pose is mapped through <c>TrackedAlignment</c> parent when present so it matches <see cref="OptitrackRigidBody"/> local pose + calibration.
    /// </summary>
    bool TryGetLeadInPresenceWorldXZ(int motiveRigidBodyId, LaundryTrackedPerson person, out Vector3 worldPos)
    {
        worldPos = default;
        if (enableKeyboardFloorSimulation)
        {
            worldPos = person.transform.position;
            return true;
        }

        var client = ActiveOptitrackStreamingClient;
        if (client == null)
            return false;

        var state = client.GetLatestRigidBodyState(motiveRigidBodyId, true);
        if (state == null || !state.IsTracked)
            return false;

        Transform align = person.transform.parent;
        worldPos = align != null ? align.TransformPoint(state.Pose.Position) : state.Pose.Position;
        return true;
    }

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

        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
            LogLeadIn("Lead-in ready: HOST in ORANGE zone + GUEST in BLUE zone starts the interaction. Check console for periodic zone diagnostics.");
    }

    void LogLeadIn(string message)
    {
        if (!logLeadInTimerEvents)
            return;
        Debug.Log("[LaundrySocial] " + message, this);
    }

    IEnumerator GuestArrivalRoutineRunner()
    {
        yield return GuestArrivalRoutine();
        m_guestArrivalRoutine = null;
    }

    /// <summary>
    /// Stops guest arrival, hides creatures, and restores lead-in wait state without destroying players or TrackedAlignment parents.
    /// </summary>
    public void ResetCreatureAppearLeadInPreserveAlignments()
    {
        if (sceneFlow != LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
            return;

        if (m_guestArrivalRoutine != null)
        {
            StopCoroutine(m_guestArrivalRoutine);
            m_guestArrivalRoutine = null;
        }

        CancelLeadInActivationDelay();

        if (m_guestCreature != null)
            m_guestCreature.StopAllCoroutines();
        if (m_hostCreature != null)
            m_hostCreature.StopAllCoroutines();

        m_guestSequenceStarted = false;
        m_leadInBothZonesArmed = true;
        StandingInteractionReady = false;

        var tableCenter = new Vector3(0f, tableTopY, 0f);

        if (m_hostCreature != null)
        {
            var seatWorld = m_layout.seats[seatedHostSeatIndex].position;
            m_hostCreature.ResetToLeadInIdleState(m_host.transform, tableCenter, tableTopY, seatWorld);
            m_hostCreature.ConfigureTableFollow(m_host.transform, seatWorld, tableTopY);
            m_hostCreature.SetTimerModeImmediate(demoCountdownSeconds + 120f, 2f, 1f);
            m_hostCreature.SetCountdownPaused(true);
            m_hostCreature.gameObject.SetActive(false);
        }

        if (m_guestCreature != null)
        {
            Vector3 nominal = m_guest.transform.position;
            m_guestCreature.ResetToLeadInIdleState(m_guest.transform, tableCenter, tableTopY, nominal);
            m_guestCreature.gameObject.SetActive(false);
        }

        LogLeadIn("Lead-in reset (alignment preserved). Leave both zones briefly, then re-enter orange + blue to start again.");
    }

    void CancelLeadInActivationDelay()
    {
        if (m_leadInActivationDelayRoutine != null)
        {
            StopCoroutine(m_leadInActivationDelayRoutine);
            m_leadInActivationDelayRoutine = null;
        }
    }

    bool LeadInZonesCurrentlySatisfied()
    {
        if (m_host == null || m_guest == null || m_hostCreature == null || m_guestCreature == null)
            return false;

        Vector3 orangeCenter = LeadInOrangeZoneCenterWorld();
        GetLeadInActivationFootprint(out Vector3 blueCenter, out float blueHx, out float blueHz);

        bool hostTracked = TryGetLeadInPresenceWorldXZ(optitrackRigidBodyIdHost, m_host, out Vector3 hostPresence);
        bool guestTracked = TryGetLeadInPresenceWorldXZ(optitrackRigidBodyIdGuest, m_guest, out Vector3 guestPresence);
        bool hostInOrange = hostTracked && StandingZoneOutline.ContainsXZ(hostPresence, orangeCenter, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
        bool guestInBlue = guestTracked && StandingZoneOutline.ContainsXZ(guestPresence, blueCenter, blueHx, blueHz);
        return hostInOrange && guestInBlue;
    }

    IEnumerator LeadInActivationDelayThenGuestArrival()
    {
        float wait = Mathf.Max(0f, leadInSecondsBeforeCreatureAppears);
        if (wait > 0f)
            yield return new WaitForSecondsRealtime(wait);

        if (!LeadInZonesCurrentlySatisfied())
        {
            m_leadInBothZonesArmed = true;
            m_leadInActivationDelayRoutine = null;
            yield break;
        }

        m_guestSequenceStarted = true;
        m_leadInActivationDelayRoutine = null;
        m_guestArrivalRoutine = StartCoroutine(GuestArrivalRoutineRunner());
    }

    IEnumerator GuestArrivalRoutine()
    {
        LogLeadIn("Lead-in triggered — enabling creatures and starting guest arrival sequence.");
        if (m_hostCreature != null)
            m_hostCreature.gameObject.SetActive(true);

        var tableCenter = new Vector3(0f, tableTopY, 0f);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = guestCreatureOffsetTowardCenterMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);

        m_guestCreature.gameObject.SetActive(true);
        yield return m_guestCreature.StartCoroutine(m_guestCreature.PlayArrivalSequence(
            m_guest.handAnchor != null ? m_guest.handAnchor : m_guest.transform,
            handPreviewSeconds,
            m_guestSeatWorld,
            jumpToSeatSeconds,
            tableTopY,
            demoCountdownSeconds,
            guestWasherNumber,
            guestDryerNumber));

        m_guestCreature.ConfigureTableFollow(m_guest.transform, m_guestSeatWorld, tableTopY);
        if (m_hostCreature != null)
            m_hostCreature.SetCountdownPaused(false);
        LogLeadIn("Timer activated — guest morph/arrival complete; host and guest countdown timers are running.");
        StandingInteractionReady = true;
    }

    void Update()
    {
        if (enableRestartInteractionHotkey && Input.GetKeyDown(restartInteractionHotkey))
        {
            var s = SceneManager.GetActiveScene();
            if (s.IsValid() && !string.IsNullOrEmpty(s.name))
                SceneManager.LoadScene(s.name);
            return;
        }

        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn &&
            enableResetLeadInCreaturesHotkey &&
            Input.GetKeyDown(resetLeadInCreaturesHotkey))
        {
            ResetCreatureAppearLeadInPreserveAlignments();
            return;
        }

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

            Vector3 orangeCenter = LeadInOrangeZoneCenterWorld();
            GetLeadInActivationFootprint(out Vector3 blueCenter, out float blueHx, out float blueHz);

            bool hostTracked = TryGetLeadInPresenceWorldXZ(optitrackRigidBodyIdHost, m_host, out Vector3 hostPresence);
            bool guestTracked = TryGetLeadInPresenceWorldXZ(optitrackRigidBodyIdGuest, m_guest, out Vector3 guestPresence);
            bool hostInOrange = hostTracked && StandingZoneOutline.ContainsXZ(hostPresence, orangeCenter, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
            bool guestInBlue = guestTracked && StandingZoneOutline.ContainsXZ(guestPresence, blueCenter, blueHx, blueHz);
            bool zonesOk = hostInOrange && guestInBlue;

            if (logLeadInZoneDiagnosticsInterval > 0f &&
                Time.unscaledTime - m_lastLeadInZoneDiagLogTime >= logLeadInZoneDiagnosticsInterval)
            {
                m_lastLeadInZoneDiagLogTime = Time.unscaledTime;
                string src = enableKeyboardFloorSimulation ? "avatar" : "motive";
                Debug.Log(
                    "[LaundrySocial] Lead-in (" + src + "): hostTracked=" + hostTracked +
                    " guestTracked=" + guestTracked +
                    " hostInOrange(seat)=" + hostInOrange +
                    " guestInBlue(laundry)=" + guestInBlue +
                    " | CheckHost@" + hostPresence.ToString("F3") +
                    " CheckGuest@" + guestPresence.ToString("F3") +
                    " | AvatarHost@" + m_host.transform.position.ToString("F3") +
                    " AvatarGuest@" + m_guest.transform.position.ToString("F3") +
                    " | Orange(seat)@" + orangeCenter.ToString("F3") + " Blue(laundry)@" + blueCenter.ToString("F3") + ".",
                    this);
            }

            if (!zonesOk)
            {
                m_leadInBothZonesArmed = true;
                CancelLeadInActivationDelay();
                return;
            }

            if (m_leadInActivationDelayRoutine != null)
                return;

            if (!m_leadInBothZonesArmed)
                return;

            m_leadInBothZonesArmed = false;
            m_leadInActivationDelayRoutine = StartCoroutine(LeadInActivationDelayThenGuestArrival());
            if (leadInSecondsBeforeCreatureAppears > 0.001f)
                LogLeadIn("Zones satisfied — waiting " + leadInSecondsBeforeCreatureAppears.ToString("F1") + " s before creatures appear.");
            return;
        }

        if (sceneFlow != LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
            return;
        if (!enableCreatureBumpInteraction || m_host == null || m_guest == null)
            return;

        bool hostIn = StandingZoneOutline.ContainsXZ(m_host.transform.position, m_hostStandCenter, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
        bool guestIn = StandingZoneOutline.ContainsXZ(m_guest.transform.position, m_guestStandCenter, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
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
        tableGo.transform.localScale = new Vector3(EffectiveTableHalfExtentX * 2f, 0.1f, EffectiveTableHalfExtentZ * 2f);
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
        m_layout.tableHalfExtentX = EffectiveTableHalfExtentX;
        m_layout.tableHalfExtentZ = EffectiveTableHalfExtentZ;
        m_layout.seatInset = seatInset;
        m_layout.seats = new Transform[6];

        float southZ = -EffectiveTableHalfExtentZ + seatInset;
        float northZ = EffectiveTableHalfExtentZ - seatInset;
        float[] xs = { -EffectiveTableHalfExtentX + seatInset, 0f, EffectiveTableHalfExtentX - seatInset };
        for (int i = 0; i < 3; i++)
        {
            m_layout.seats[i] = CreateSeatMarker("Seat_South_" + i, new Vector3(xs[i], tableTopY + 0.01f, southZ));
            m_layout.seats[i + 3] = CreateSeatMarker("Seat_North_" + i, new Vector3(xs[i], tableTopY + 0.01f, northZ));
        }
    }

    void BuildLaundryZone()
    {
        ComputeGuestActivationGeometry(out _, out Vector3 triggerCenterLocal, out float hx, out float hz);

        var z = GameObject.CreatePrimitive(PrimitiveType.Cube);
        z.name = "LaundryActivationZone";
        z.transform.SetParent(transform, false);
        z.transform.localPosition = triggerCenterLocal;
        z.transform.localScale = new Vector3(hx * 2f, 1f, hz * 2f);
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
        float zOff = (seatIndex < 3 ? -1f : 1f) * SeatStandForwardOffset;
        var p = s.position;
        return new Vector3(p.x, tableTopY + 0.02f, p.z + zOff);
    }

    /// <summary>Lead-in orange footprint center: standing spot shifted slightly off the table edge (±Z).</summary>
    Vector3 LeadInOrangeZoneCenterWorld()
    {
        Vector3 c = StandCenterForSeatIndex(seatedHostSeatIndex);
        float dir = seatedHostSeatIndex < 3 ? -1f : 1f;
        c.z += dir * leadInOrangeOutlineAwayFromTableMeters;
        return c;
    }

    void BuildStandingZoneOutlines()
    {
        m_guestSeatIndex = SocialTableLayout.DiagonalSeatAcrossFrom(seatedHostSeatIndex);

        m_hostStandCenter = StandCenterForSeatIndex(seatedHostSeatIndex);
        m_guestStandCenter = StandCenterForSeatIndex(m_guestSeatIndex);

        var root = new GameObject("StandingZones").transform;
        root.SetParent(transform, false);

        CreateZoneOutline(root, "HostStandingZone", m_hostStandCenter, hostZoneColor, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
        CreateZoneOutline(root, "GuestStandingZone", m_guestStandCenter, guestZoneColor, EffectiveStandingZoneHalfExtentX, EffectiveStandingZoneHalfExtentZ);
    }

    void ComputeGuestActivationGeometry(
        out Vector3 footprintCenterLocal,
        out Vector3 laundryTriggerCenterLocal,
        out float halfExtentX,
        out float halfExtentZ)
    {
        float s = ShortTableHalfAxis;
        float actScale = sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn
            ? Mathf.Max(1f, leadInActivationFootprintScale)
            : 1f;
        halfExtentX = Mathf.Max(0.05f, s * guestActivationFootprintFractionOfShortAxis * actScale);
        halfExtentZ = Mathf.Max(0.05f, s * guestActivationFootprintFractionOfShortAxis * actScale);
        float gap = Mathf.Max(0.03f, s * guestActivationGapFractionOfShortAxis);

        float cx = -EffectiveTableHalfExtentX - gap - halfExtentX;
        float cz = EffectiveTableHalfExtentZ + gap + halfExtentZ;

        footprintCenterLocal = new Vector3(cx, tableTopY + 0.03f, cz);
        laundryTriggerCenterLocal = new Vector3(cx, tableTopY + 0.5f, cz);
    }

    /// <summary>Laundry activation floor rect (blue outline); used by trigger mesh and guest-in-blue check.</summary>
    void GetLeadInActivationFootprint(out Vector3 centerWorld, out float halfExtentX, out float halfExtentZ)
    {
        ComputeGuestActivationGeometry(out Vector3 footprintLocal, out _, out halfExtentX, out halfExtentZ);
        centerWorld = transform.TransformPoint(footprintLocal);
    }

    void BuildLeadInUserZoneHighlights()
    {
        var root = new GameObject("UserZoneHighlights").transform;
        root.SetParent(transform, false);

        float lw = EffectiveLeadInActivationOutlineWidth;

        Vector3 orangeCenter = LeadInOrangeZoneCenterWorld();
        float orangeLineY = tableTopY + leadInOrangeOutlineLineYOffsetFromTableTop;
        CreateZoneOutline(
            root,
            "OrangeHostZone",
            orangeCenter,
            leadInGuestActivationZoneColor,
            EffectiveStandingZoneHalfExtentX,
            EffectiveStandingZoneHalfExtentZ,
            lw,
            orangeLineY);

        GetLeadInActivationFootprint(out Vector3 blueCenter, out float blueHx, out float blueHz);
        CreateZoneOutline(
            root,
            "BlueGuestZone",
            blueCenter,
            hostZoneColor,
            blueHx,
            blueHz,
            lw,
            null);
    }

    void CreateZoneOutline(Transform parent, string name, Vector3 center, Color col, float halfExtentX, float halfExtentZ, float? lineWidth = null, float? standingZoneLineWorldY = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var outline = go.AddComponent<StandingZoneOutline>();
        outline.halfExtentX = halfExtentX;
        outline.halfExtentZ = halfExtentZ;
        outline.lineColor = col;
        outline.lineWidth = lineWidth ?? EffectiveUserZoneOutlineWidth;
        outline.heightY = standingZoneLineWorldY ?? (tableTopY + 0.03f);
        outline.Rebuild();
    }

    static void EncapsulateXZRect(ref float minX, ref float maxX, ref float minZ, ref float maxZ,
        float cx, float cz, float halfX, float halfZ)
    {
        minX = Mathf.Min(minX, cx - halfX);
        maxX = Mathf.Max(maxX, cx + halfX);
        minZ = Mathf.Min(minZ, cz - halfZ);
        maxZ = Mathf.Max(maxZ, cz + halfZ);
    }

    /// <summary>
    /// Orthographic top-down framing: includes table plus seat footprint + laundry activation (lead-in),
    /// or both standing footprints (bump flow). Padding widens past those regions for projection onto the floor.
    /// </summary>
    void ComputeTopDownProjectionFrustum(out float centerX, out float centerZ, out float orthoHalfExtentZ, out float aspectRatio)
    {
        float minX = -EffectiveTableHalfExtentX;
        float maxX = EffectiveTableHalfExtentX;
        float minZ = -EffectiveTableHalfExtentZ;
        float maxZ = EffectiveTableHalfExtentZ;

        float sx = EffectiveStandingZoneHalfExtentX;
        float sz = EffectiveStandingZoneHalfExtentZ;

        if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
        {
            ComputeGuestActivationGeometry(out Vector3 fpLocal, out _, out float ax, out float az);
            var fpWorld = transform.TransformPoint(new Vector3(fpLocal.x, 0f, fpLocal.z));
            EncapsulateXZRect(ref minX, ref maxX, ref minZ, ref maxZ, fpWorld.x, fpWorld.z, ax, az);

            Vector3 hostStand = LeadInOrangeZoneCenterWorld();
            EncapsulateXZRect(ref minX, ref maxX, ref minZ, ref maxZ, hostStand.x, hostStand.z, sx, sz);
        }
        else if (sceneFlow == LaundrySocialSceneFlowKind.StandingZonesBumpOnly)
        {
            int guestSeat = SocialTableLayout.DiagonalSeatAcrossFrom(seatedHostSeatIndex);
            Vector3 h = StandCenterForSeatIndex(seatedHostSeatIndex);
            Vector3 g = StandCenterForSeatIndex(guestSeat);
            EncapsulateXZRect(ref minX, ref maxX, ref minZ, ref maxZ, h.x, h.z, sx, sz);
            EncapsulateXZRect(ref minX, ref maxX, ref minZ, ref maxZ, g.x, g.z, sx, sz);
        }

        float pad = Mathf.Max(0f, projectionViewPaddingMeters);
        minX -= pad;
        maxX += pad;
        minZ -= pad;
        maxZ += pad;

        centerX = (minX + maxX) * 0.5f;
        centerZ = (minZ + maxZ) * 0.5f;
        float width = Mathf.Max(0.02f, maxX - minX);
        float depth = Mathf.Max(0.02f, maxZ - minZ);
        float mul = Mathf.Max(1f, projectionViewMarginMultiplier);
        width *= mul;
        depth *= mul;
        minX = centerX - width * 0.5f;
        maxX = centerX + width * 0.5f;
        minZ = centerZ - depth * 0.5f;
        maxZ = centerZ + depth * 0.5f;

        // Camera orientation (90,0,0): viewport vertical maps to world +Z; orthographicSize is half of that extent.
        orthoHalfExtentZ = depth * 0.5f;
        aspectRatio = width / depth;
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

        ComputeTopDownProjectionFrustum(out float cx, out float cz, out float halfZ, out float aspect);
        cam.orthographicSize = halfZ;
        cam.aspect = aspect;

        cam.transform.position = new Vector3(cx, tableTopY + topDownCameraHeight, cz);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 20f;
    }

    void ApplyCreatureHudWorldScale(CreatureWorldHud hud)
    {
        if (hud == null)
            return;
        hud.transform.localScale = Vector3.one * EffectiveCreatureHudScale;
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
        ApplyCreatureHudWorldScale(m_hostCreature);
        m_hostCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_hostCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_hostCreature.SetTablePresentationContext(m_host.transform, tableCenter, tableTopY);
        m_hostCreature.ConfigureTableFollow(m_host.transform, m_layout.seats[seatedHostSeatIndex].position, tableTopY);
        m_hostCreature.SnapCreatureToSeatOnTable(m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.SetTableBoundsForTimerClamp(EffectiveTableHalfExtentX, EffectiveTableHalfExtentZ);
        m_hostCreature.SetTimerModeImmediate(demoCountdownSeconds + 120f, 2f, 1f);
        m_hostCreature.SetCountdownPaused(true);
        m_hostCreature.gameObject.SetActive(false);

        m_guestCreature = CreateCreature("Creature_Guest", m_guest.transform.position);
        m_guestCreature.Build(ctex, ttex);
        ApplyCreatureHudWorldScale(m_guestCreature);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = guestCreatureOffsetTowardCenterMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);
        m_guestCreature.SetTableBoundsForTimerClamp(EffectiveTableHalfExtentX, EffectiveTableHalfExtentZ);
        m_guestCreature.countdownElapsedScale = guestCountdownElapsedScale;
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
        ApplyCreatureHudWorldScale(m_hostCreature);
        m_hostCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_hostCreature.offsetFromPlayerTowardTableCenterMeters = creatureTableOffsetFromPlayerMeters;
        m_hostCreature.SetTablePresentationContext(m_host.transform, tableCenter, tableTopY);
        m_hostCreature.ConfigureTableFollow(m_host.transform, m_layout.seats[seatedHostSeatIndex].position, tableTopY);
        m_hostCreature.SnapCreatureToSeatOnTable(m_layout.seats[seatedHostSeatIndex].position);
        m_hostCreature.SetTableBoundsForTimerClamp(EffectiveTableHalfExtentX, EffectiveTableHalfExtentZ);
        m_hostCreature.SetTimerModeImmediate(demoCountdownSeconds + 120f, 2f, 1f);

        m_guestCreature = CreateCreature("Creature_Guest", m_layout.seats[m_guestSeatIndex].position);
        m_guestCreature.Build(ctex, ttex);
        ApplyCreatureHudWorldScale(m_guestCreature);
        m_guestCreature.followSeatVersusPlayerBlend = creatureFollowPlayerBlend;
        m_guestCreature.offsetFromPlayerTowardTableCenterMeters = guestCreatureOffsetTowardCenterMeters;
        m_guestCreature.SetTablePresentationContext(m_guest.transform, tableCenter, tableTopY);
        m_guestCreature.ConfigureTableFollow(m_guest.transform, m_layout.seats[m_guestSeatIndex].position, tableTopY);
        m_guestCreature.SnapCreatureToSeatOnTable(m_layout.seats[m_guestSeatIndex].position);
        m_guestCreature.SetTableBoundsForTimerClamp(EffectiveTableHalfExtentX, EffectiveTableHalfExtentZ);
        m_guestCreature.SetTimerModeImmediate(demoCountdownSeconds, guestWasherNumber, guestDryerNumber);
        m_guestCreature.countdownElapsedScale = guestCountdownElapsedScale;
    }

    LaundryTrackedPerson SpawnPerson(string objectName, int seatIndexOrNeg, int playerIndex, bool atSeat)
    {
        Vector3 pos;
        Quaternion rot = Quaternion.identity;
        if (atSeat && seatIndexOrNeg >= 0 && seatIndexOrNeg < m_layout.seats.Length)
        {
            if (sceneFlow == LaundrySocialSceneFlowKind.CreatureAppearLeadIn)
            {
                // Same XZ as orange outline / lead-in ContainsXZ (Y differs; gate is XZ-only).
                pos = LeadInOrangeZoneCenterWorld();
            }
            else
            {
                var s = m_layout.seats[seatIndexOrNeg];
                float alongZ = seatIndexOrNeg < 3 ? -SeatStandForwardOffset : SeatStandForwardOffset;
                pos = s.position + new Vector3(0f, 0f, alongZ);
            }

            rot = Quaternion.LookRotation(Vector3.forward * (seatIndexOrNeg < 3 ? 1f : -1f), Vector3.up);
        }
        else
        {
            ComputeGuestActivationGeometry(out Vector3 footprintLocal, out _, out _, out _);
            // Same world center as blue outline / GetLeadInActivationFootprint — guest root starts inside activation XZ.
            pos = transform.TransformPoint(footprintLocal);
            rot = Quaternion.LookRotation(Vector3.left, Vector3.up);
        }

        Transform alignmentParent = null;
        if (spawnPlayersUnderTrackedAlignmentParent)
        {
            var alignmentGo = new GameObject(objectName + "_TrackedAlignment");
            alignmentParent = alignmentGo.transform;
            alignmentParent.SetParent(transform, false);
            alignmentParent.SetPositionAndRotation(pos, rot);
        }

        GameObject root;
        float playerScale = EffectivePlayerPrefabScale;
        if (firstPersonPrefab != null)
        {
            root = Instantiate(firstPersonPrefab);
            root.name = objectName;
            if (alignmentParent != null)
            {
                root.transform.SetParent(alignmentParent, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
            }
            else
                root.transform.SetPositionAndRotation(pos, rot);

            root.transform.localScale = Vector3.Scale(root.transform.localScale, Vector3.one * playerScale);
            foreach (var c in root.GetComponentsInChildren<Camera>(true))
                c.enabled = false;
            foreach (var listener in root.GetComponentsInChildren<AudioListener>(true))
                Destroy(listener);
        }
        else
        {
            root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = objectName;
            if (alignmentParent != null)
            {
                root.transform.SetParent(alignmentParent, false);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.identity;
            }
            else
                root.transform.SetPositionAndRotation(pos, rot);

            root.transform.localScale = Vector3.Scale(new Vector3(0.45f, 0.9f, 0.45f), Vector3.one * playerScale);
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
            float r = ShortTableHalfAxis;
            float xz = Mathf.Clamp(r * 0.92f, 0.22f, 0.42f);
            h.localPosition = new Vector3(xz, Mathf.Clamp(r * 3.85f, 1.15f, 1.55f), xz);
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
