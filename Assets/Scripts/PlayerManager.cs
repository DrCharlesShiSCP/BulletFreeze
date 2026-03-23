using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Spawns, registers, and tracks all local players for the match.
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

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
    [Tooltip("Optional arena collider used to clamp player aim markers.")]
    [SerializeField] private Collider arenaBounds;
    [Tooltip("Optional prefab spawned as each player's world-space target marker.")]
    [SerializeField] private GameObject crosshairPrefab;

    [Header("Debug")]
    [Tooltip("Logs player joins, eliminations, and reset events.")]
    [SerializeField] private bool debugLogs = true;

    private readonly List<PlayerSlot> players = new List<PlayerSlot>();
    private readonly HashSet<Gamepad> assignedGamepads = new HashSet<Gamepad>();
    private bool rosterLocked;
    private int lastJoinFrame = -1;

    public IReadOnlyList<PlayerSlot> Players => players;
    public Collider ArenaBounds => arenaBounds;
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
            player.Controller.SetEliminated(false);
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
            player.Controller.SetEliminated(false);
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

    public void AutoConfirmAlivePlayers()
    {
        foreach (PlayerSlot player in players)
        {
            if (player == null || !player.IsAlive || player.AimController == null)
                continue;

            player.AimController.AutoConfirmIfNeeded();
        }
    }

    public void EliminatePlayer(PlayerSlot player, string causeSummary, Vector3 impactPoint)
    {
        if (player == null || !player.IsAlive)
            return;

        player.SetAlive(false);
        player.ClearConfirmedTarget();

        if (player.AimController != null)
            player.AimController.SetAimActive(false);

        if (player.Controller != null)
        {
            player.Controller.SetEliminated(true);
            player.Controller.SetLine2("Eliminated");
        }

        if (debugLogs)
        {
            Debug.Log(
                $"[PlayerManager] {player.DisplayName} eliminated at {impactPoint}. " +
                $"Cause: {causeSummary}");
        }
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
            SpawnPlayer(null, true);
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

        SpawnPlayer(null, true);
    }

    private void RegisterJoiningGamepads()
    {
        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad == null || assignedGamepads.Contains(pad) || players.Count >= maxPlayers)
                continue;

            if (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)
                SpawnPlayer(pad, false);
        }
    }

    private void RegisterConnectedGamepads()
    {
        foreach (Gamepad pad in Gamepad.all)
        {
            if (pad == null || assignedGamepads.Contains(pad) || players.Count >= maxPlayers)
                continue;

            SpawnPlayer(pad, false);
        }
    }

    private void SpawnPlayer(Gamepad pad, bool usesKeyboard)
    {
        if (characterPrefabs.Count == 0)
        {
            Debug.LogError("[PlayerManager] No character prefabs assigned.");
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

        PlayerSlot slot = new PlayerSlot(playerId, pad, usesKeyboard);
        controller.Initialize(slot, SharedCamera);
        aimController.Initialize(slot, controller, arenaBounds, crosshairPrefab);
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
}
