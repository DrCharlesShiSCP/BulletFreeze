using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Spawns, registers, and tracks all local players for the match.
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    public event Action<string> KillFeedMessageAdded;

    [Header("Player Spawning")]
    [Tooltip("Character prefabs used when spawning players. Prefabs should include CharacterController and PlayerCharacterController.")]
    [SerializeField] private List<GameObject> characterPrefabs = new List<GameObject>();
    [Tooltip("Optional fixed spawn points. Extra players fall back to a circle if this list is shorter than the roster.")]
    [SerializeField] private Transform[] spawnPoints;
    [Tooltip("Maximum number of local players supported by this prototype.")]
    [SerializeField] private int maxPlayers = 8;
    [Tooltip("Require unjoined devices to press a join input before they enter the match lobby.")]
    [SerializeField] private bool requireJoinInput = true;
    [Tooltip("Allow one keyboard player to join for fast local testing.")]
    [SerializeField] private bool includeKeyboardPlayer = true;
    [Tooltip("Continue watching for additional gamepads while the lobby is open.")]
    [SerializeField] private bool pollForGamepadsWhileUnlocked = true;
    [Tooltip("Fallback circle radius used when there are more players than spawn points.")]
    [SerializeField] private float fallbackSpawnRadius = 7f;

    [Header("Shared References")]
    [Tooltip("Optional shared camera passed to player controllers for camera-relative movement.")]
    [SerializeField] private Camera sharedCamera;
    [Tooltip("Arena bound objects used to clamp aim. Assign one parent object or multiple individual collider objects.")]
    [SerializeField] private Transform[] arenaBoundTargets;
    [Tooltip("Optional prefab spawned as each player's world-space target marker.")]
    [SerializeField] private GameObject crosshairPrefab;

    [Header("Debug")]
    [Tooltip("Logs player joins, eliminations, and reset events.")]
    [SerializeField] private bool debugLogs = true;
    [Tooltip("Allow debug fake players to be added without a physical controller.")]
    [SerializeField] private bool allowDebugFakePlayers = true;
    [Tooltip("Press F6 to add one fake player or F7 to fill up to the start minimum while the lobby is open.")]
    [SerializeField] private bool enableDebugFakePlayerHotkeys = true;

    private readonly List<PlayerSlot> players = new List<PlayerSlot>();
    private readonly HashSet<Gamepad> assignedGamepads = new HashSet<Gamepad>();
    private bool rosterLocked;
    private int lastJoinFrame = -1;

    public IReadOnlyList<PlayerSlot> Players => players;
    public int JoinedPlayerCount => players.Count;
    public Camera SharedCamera => sharedCamera != null ? sharedCamera : Camera.main;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        WarnAboutMissingReferences();

        if (!requireJoinInput)
            RegisterAvailablePlayers();
    }

    private void Update()
    {
        if (rosterLocked)
            return;

        if (requireJoinInput)
        {
            ProcessLobbyJoinRequests();
            return;
        }

        ProcessDebugHotkeys();

        if (pollForGamepadsWhileUnlocked && players.Count < maxPlayers)
            RegisterConnectedGamepads();
    }

    [ContextMenu("Register Available Players")]
    public void RegisterAvailablePlayers()
    {
        if (includeKeyboardPlayer)
        {
            if (requireJoinInput)
                TryJoinKeyboardPlayer();
            else
                TryRegisterKeyboardPlayer();
        }

        if (requireJoinInput)
            RegisterJoiningGamepads();
        else
            RegisterConnectedGamepads();
    }

    [ContextMenu("Reset Match Players")]
    public void ResetForNewMatch()
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerSlot player = players[i];
            if (player == null || player.Controller == null)
                continue;

            ResolveSpawnTransform(i, out Vector3 spawnPosition, out Quaternion spawnRotation);

            player.SetAlive(true);
            player.ClearConfirmedTarget();
            player.Controller.ResetToSpawn(spawnPosition, spawnRotation);
            player.Controller.SetLine1(player.DisplayName);
            player.Controller.SetLine2(player.InputLabel);
            player.Controller.SetEliminated(false, Vector3.zero);
            player.Controller.SetPhaseVisible(true);
            player.Controller.SetMovementAllowed(false);

            if (player.AimController != null)
            {
                player.AimController.ResetForNextRound();
                player.AimController.SetAimActive(false);
            }
        }

        if (debugLogs)
            Debug.Log("[PlayerManager] Match players reset.");
    }

    [ContextMenu("Debug/Add Fake Player")]
    public void DebugAddFakePlayer()
    {
        if (!allowDebugFakePlayers)
        {
            Debug.LogWarning("[PlayerManager] Debug fake players are disabled.");
            return;
        }

        if (rosterLocked)
        {
            Debug.LogWarning("[PlayerManager] Cannot add a fake player while the roster is locked.");
            return;
        }

        SpawnPlayer(null, false, true);
    }

    [ContextMenu("Debug/Add 2 Fake Players")]
    public void DebugAddTwoFakePlayers()
    {
        DebugAddFakePlayers(2);
    }

    [ContextMenu("Debug/Add 4 Fake Players")]
    public void DebugAddFourFakePlayers()
    {
        DebugAddFakePlayers(4);
    }

    public void DebugAddFakePlayers(int count)
    {
        if (!allowDebugFakePlayers)
        {
            Debug.LogWarning("[PlayerManager] Debug fake players are disabled.");
            return;
        }

        if (rosterLocked)
        {
            Debug.LogWarning("[PlayerManager] Cannot add fake players while the roster is locked.");
            return;
        }

        int spawnCount = Mathf.Max(0, count);
        for (int i = 0; i < spawnCount && players.Count < maxPlayers; i++)
            SpawnPlayer(null, false, true);
    }

    public void ApplyLobbyPresentation(bool canStartMatch)
    {
        for (int i = 0; i < players.Count; i++)
        {
            PlayerSlot player = players[i];
            if (player == null || player.Controller == null)
                continue;

            ResolveSpawnTransform(i, out Vector3 spawnPosition, out Quaternion spawnRotation);

            player.SetAlive(true);
            player.ClearConfirmedTarget();
            player.Controller.ResetToSpawn(spawnPosition, spawnRotation);
            player.Controller.SetEliminated(false, Vector3.zero);
            player.Controller.SetPhaseVisible(true);
            player.Controller.SetMovementAllowed(false);
            player.Controller.SetLine1(player.DisplayName);
            player.Controller.SetLine2(canStartMatch ? "Press Start" : "Joined");

            if (player.AimController != null)
                player.AimController.SetAimActive(false);
        }
    }

    public void ResetRoundSelections()
    {
        foreach (PlayerSlot player in players)
        {
            if (player == null)
                continue;

            player.ClearConfirmedTarget();

            if (player.IsAlive && player.AimController != null)
                player.AimController.ResetForNextRound();
        }

        if (debugLogs)
            Debug.Log("[PlayerManager] Cleared round confirmations.");
    }

    public void PrepareAimForAlivePlayers()
    {
        foreach (PlayerSlot player in players)
        {
            if (player == null || !player.IsAlive)
                continue;

            if (player.AimController != null)
                player.AimController.ResetForNextRound();
        }
    }

    public List<PlayerSlot> GetAlivePlayers()
    {
        List<PlayerSlot> alivePlayers = new List<PlayerSlot>();

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.IsAlive)
                alivePlayers.Add(player);
        }

        return alivePlayers;
    }

    public int GetAlivePlayerCount()
    {
        int aliveCount = 0;

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.IsAlive)
                aliveCount++;
        }

        return aliveCount;
    }

    public PlayerSlot GetLastAlivePlayer()
    {
        foreach (PlayerSlot player in players)
        {
            if (player != null && player.IsAlive)
                return player;
        }

        return null;
    }

    public bool AreAllAlivePlayersConfirmed()
    {
        int aliveCount = 0;

        foreach (PlayerSlot player in players)
        {
            if (player == null || !player.IsAlive)
                continue;

            aliveCount++;

            if (!player.HasConfirmedTarget)
                return false;
        }

        return aliveCount > 0;
    }

    public bool HasEnoughJoinedPlayers(int minimumPlayersToStart)
    {
        return players.Count >= minimumPlayersToStart;
    }

    public bool HasJoinedPlayerStartRequest(int minimumPlayersToStart)
    {
        if (!HasEnoughJoinedPlayers(minimumPlayersToStart))
            return false;

        if (Time.frameCount == lastJoinFrame)
            return false;

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.WasStartPressedThisFrame())
                return true;
        }

        return false;
    }

    public void FinalizeAimTargets()
    {
        foreach (PlayerSlot player in players)
        {
            if (player == null || !player.IsAlive || player.AimController == null)
                continue;

            player.AimController.FinalizeCurrentTarget();
        }
    }

    public void EliminatePlayer(PlayerSlot player, string causeSummary, Vector3 impactPoint)
    {
        if (player == null || !player.IsAlive)
            return;

        string killFeedMessage = BuildKillFeedMessage(player, causeSummary);

        player.SetAlive(false);
        player.ClearConfirmedTarget();

        if (player.AimController != null)
            player.AimController.SetAimActive(false);

        if (player.Controller != null)
        {
            player.Controller.SetEliminated(true, impactPoint);
            player.Controller.SetLine2("Eliminated");
        }

        GamePhaseManager.Instance?.PlayDeathSoundAt(
            player.Controller != null ? player.Controller.transform.position : impactPoint);

        if (debugLogs)
        {
            Debug.Log(
                $"[PlayerManager] {player.DisplayName} eliminated at {impactPoint}. " +
                $"Cause: {causeSummary}");
        }

        KillFeedMessageAdded?.Invoke(killFeedMessage);
    }

    public void LockRoster()
    {
        rosterLocked = true;
    }

    public void UnlockRoster()
    {
        rosterLocked = false;
    }

    private void ProcessLobbyJoinRequests()
    {
        ProcessDebugHotkeys();

        if (players.Count >= maxPlayers)
            return;

        if (includeKeyboardPlayer)
            TryJoinKeyboardPlayer();

        if (pollForGamepadsWhileUnlocked)
            RegisterJoiningGamepads();
    }

    private void TryJoinKeyboardPlayer()
    {
        if (Keyboard.current == null || players.Count >= maxPlayers)
            return;

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.UsesKeyboard)
                return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)
            SpawnPlayer(null, true, false);
    }

    private void TryRegisterKeyboardPlayer()
    {
        if (Keyboard.current == null || players.Count >= maxPlayers)
            return;

        foreach (PlayerSlot player in players)
        {
            if (player != null && player.UsesKeyboard)
                return;
        }

        SpawnPlayer(null, true, false);
    }

    private void RegisterJoiningGamepads()
    {
        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad == null || assignedGamepads.Contains(pad) || players.Count >= maxPlayers)
                continue;

            if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)
                SpawnPlayer(pad, false, false);
        }
    }

    private void RegisterConnectedGamepads()
    {
        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad == null || assignedGamepads.Contains(pad) || players.Count >= maxPlayers)
                continue;

            SpawnPlayer(pad, false, false);
        }
    }

    private void ProcessDebugHotkeys()
    {
        if (!allowDebugFakePlayers || !enableDebugFakePlayerHotkeys)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.f6Key.wasPressedThisFrame)
            DebugAddFakePlayer();

        if (keyboard.f7Key.wasPressedThisFrame)
            DebugAddFakePlayers(4);
    }

    private void SpawnPlayer(Gamepad pad, bool usesKeyboard, bool isDebugPlayer)
    {
        WarnAboutMissingReferences();

        if (characterPrefabs.Count == 0)
        {
            Debug.LogError("[PlayerManager] No character prefabs assigned.");
            return;
        }

        if (players.Count >= maxPlayers)
        {
            Debug.LogWarning("[PlayerManager] Cannot add more players. Max player count reached.");
            return;
        }

        int playerIndex = players.Count;
        int playerId = playerIndex + 1;

        ResolveSpawnTransform(playerIndex, out Vector3 spawnPosition, out Quaternion spawnRotation);

        GameObject prefab = characterPrefabs[playerIndex % characterPrefabs.Count];
        GameObject instance = Instantiate(prefab, spawnPosition, spawnRotation);
        instance.name = $"Player_{playerId}_{prefab.name}";

        PlayerController controller = instance.GetComponent<PlayerController>();
        if (controller == null)
            controller = instance.AddComponent<PlayerCharacterController>();

        PlayerAimController aimController = instance.GetComponent<PlayerAimController>();
        if (aimController == null)
            aimController = instance.AddComponent<PlayerAimController>();

        PlayerSlot slot = new PlayerSlot(playerId, pad, usesKeyboard, isDebugPlayer);
        controller.Initialize(slot, SharedCamera);
        aimController.Initialize(slot, controller, arenaBoundTargets, crosshairPrefab);
        slot.Bind(controller, aimController);

        players.Add(slot);
        lastJoinFrame = Time.frameCount;

        if (pad != null)
            assignedGamepads.Add(pad);

        controller.ResetToSpawn(spawnPosition, spawnRotation);
        controller.SetLine1(slot.DisplayName);
        controller.SetLine2("Joined");

        if (debugLogs)
        {
            Debug.Log(
                $"[PlayerManager] {slot.DisplayName} joined the lobby using {slot.InputLabel}.");
        }
    }

    private void ResolveSpawnTransform(int playerIndex, out Vector3 position, out Quaternion rotation)
    {
        if (spawnPoints != null && playerIndex < spawnPoints.Length && spawnPoints[playerIndex] != null)
        {
            position = spawnPoints[playerIndex].position;
            rotation = spawnPoints[playerIndex].rotation;
            return;
        }

        float angle = Mathf.Deg2Rad * (360f / Mathf.Max(1, maxPlayers)) * playerIndex;
        position = transform.position + new Vector3(
            Mathf.Cos(angle) * fallbackSpawnRadius,
            0f,
            Mathf.Sin(angle) * fallbackSpawnRadius);

        Vector3 lookDirection = (transform.position - position).normalized;
        if (lookDirection.sqrMagnitude < 0.001f)
            lookDirection = Vector3.forward;

        rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
    }

    private void WarnAboutMissingReferences()
    {
        if (characterPrefabs == null || characterPrefabs.Count == 0)
        {
            Debug.LogWarning(
                "[PlayerManager] Character prefabs list is empty. Players cannot be spawned.");
        }

        if (sharedCamera == null && Camera.main == null)
        {
            Debug.LogWarning(
                "[PlayerManager] No shared camera assigned and no MainCamera found. " +
                "Movement and mouse aiming will not work correctly.");
        }

        if (arenaBoundTargets == null || arenaBoundTargets.Length == 0)
        {
            Debug.LogWarning(
                "[PlayerManager] Arena bound targets are missing. " +
                "Aim markers will use the fallback ground plane instead of arena clamping.");
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning(
                "[PlayerManager] No spawn points assigned. Players will use fallback circle spawning.");
        }
    }

    private static string BuildKillFeedMessage(PlayerSlot victim, string causeSummary)
    {
        string victimName = victim != null ? victim.DisplayName : "Unknown";

        if (string.IsNullOrWhiteSpace(causeSummary))
            return $"{victimName} was eliminated";

        if (string.Equals(causeSummary, victimName, StringComparison.OrdinalIgnoreCase))
            return $"{victimName} eliminated themselves";

        return $"{causeSummary} eliminated {victimName}";
    }
}
