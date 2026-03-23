using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Owns the round loop and phase transitions for the battle royale prototype.
public class GamePhaseManager : MonoBehaviour
{
    public static GamePhaseManager Instance { get; private set; }

    [Header("References")]
    [Tooltip("Player roster and spawned avatar manager.")]
    [SerializeField] private PlayerManager playerManager;
    [Tooltip("Resolves projectile travel and strike impacts.")]
    [SerializeField] private ProjectileStrikeSystem projectileStrikeSystem;
    [Tooltip("Optional scene UI controller.")]
    [SerializeField] private UIManager uiManager;

    [Header("Lobby")]
    [Tooltip("Automatically enters the join lobby when the scene loads.")]
    [SerializeField] private bool autoStartOnPlay = true;
    [Tooltip("Small delay so scene objects finish initializing before the lobby appears.")]
    [SerializeField] private float matchStartDelay = 0.5f;
    [Tooltip("Minimum joined players required before the match can begin.")]
    [SerializeField] private int minimumPlayersToStart = 2;

    [Header("Phase Durations")]
    [Tooltip("How long players can run around before countdown starts.")]
    [SerializeField] private float runningPhaseDuration = 10f;
    [Tooltip("Invisible movement window before players freeze.")]
    [SerializeField] private float countdownDuration = 3f;
    [Tooltip("How long players remain locked before aiming begins.")]
    [SerializeField] private float freezeDuration = 1.5f;
    [Tooltip("Optional cap on aim time. Set to 0 for unlimited wait.")]
    [SerializeField] private float aimPhaseMaxDuration = 12f;
    [Tooltip("Delay after shoot resolution before the next round begins.")]
    [SerializeField] private float interRoundDelay = 1.25f;

    [Header("Debug")]
    [Tooltip("Logs lobby changes, phase changes, confirmations, and round resets.")]
    [SerializeField] private bool debugLogs = true;

    private Coroutine matchLoopRoutine;
    private int roundNumber;
    private bool lobbyActive;
    private int cachedLobbyPlayerCount = -1;
    private bool cachedLobbyCanStart;

    public GamePhaseType CurrentPhase { get; private set; } = GamePhaseType.None;

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

    private IEnumerator Start()
    {
        ResolveReferences();

        if (projectileStrikeSystem != null && playerManager != null)
            projectileStrikeSystem.Initialize(playerManager);

        if (!autoStartOnPlay)
            yield break;

        yield return new WaitForSeconds(matchStartDelay);
        EnterLobby();
    }

    private void Update()
    {
        if (!lobbyActive || matchLoopRoutine != null)
            return;

        UpdateLobbyState();
    }

    [ContextMenu("Enter Lobby")]
    public void EnterLobby()
    {
        ResolveReferences();

        if (playerManager == null)
        {
            Debug.LogError("[GamePhaseManager] Cannot enter lobby without a PlayerManager.");
            return;
        }

        if (projectileStrikeSystem != null)
            projectileStrikeSystem.Initialize(playerManager);

        lobbyActive = true;
        CurrentPhase = GamePhaseType.None;
        roundNumber = 0;

        playerManager.UnlockRoster();
        uiManager?.HideWinner();

        cachedLobbyPlayerCount = -1;
        cachedLobbyCanStart = false;

        UpdateLobbyState(true);

        if (debugLogs)
            Debug.Log("[GamePhaseManager] Lobby opened. Waiting for players to join.");
    }

    [ContextMenu("Start Match")]
    public void StartMatch()
    {
        ResolveReferences();

        if (playerManager == null || projectileStrikeSystem == null)
        {
            Debug.LogError("[GamePhaseManager] Missing required references.");
            return;
        }

        if (!playerManager.HasEnoughJoinedPlayers(minimumPlayersToStart))
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[GamePhaseManager] Waiting for more players. Joined: " +
                    $"{playerManager.JoinedPlayerCount}/{minimumPlayersToStart}");
            }

            UpdateLobbyState(true);
            return;
        }

        if (matchLoopRoutine != null)
            StopCoroutine(matchLoopRoutine);

        lobbyActive = false;
        uiManager?.HideWinner();
        playerManager.LockRoster();
        playerManager.ResetForNewMatch();

        roundNumber = 0;
        matchLoopRoutine = StartCoroutine(RunMatchLoop());

        if (debugLogs)
        {
            Debug.Log(
                $"[GamePhaseManager] Match starting with {playerManager.JoinedPlayerCount} joined players.");
        }
    }

    private void UpdateLobbyState(bool forceRefresh = false)
    {
        if (playerManager == null)
            return;

        int joinedPlayers = playerManager.JoinedPlayerCount;
        bool canStartMatch = playerManager.HasEnoughJoinedPlayers(minimumPlayersToStart);

        if (forceRefresh ||
            joinedPlayers != cachedLobbyPlayerCount ||
            canStartMatch != cachedLobbyCanStart)
        {
            playerManager.ApplyLobbyPresentation(canStartMatch);
            cachedLobbyPlayerCount = joinedPlayers;
            cachedLobbyCanStart = canStartMatch;

            if (debugLogs)
            {
                Debug.Log(
                    $"[GamePhaseManager] Lobby status updated. Joined players: {joinedPlayers}. " +
                    $"{(canStartMatch ? "Ready to start." : "Waiting for more players.")}");
            }
        }

        uiManager?.ShowLobby(playerManager.Players, minimumPlayersToStart);

        if (canStartMatch && playerManager.HasJoinedPlayerStartRequest(minimumPlayersToStart))
        {
            if (debugLogs)
                Debug.Log("[GamePhaseManager] Start requested from a joined controller.");

            StartMatch();
        }
    }

    private IEnumerator RunMatchLoop()
    {
        while (true)
        {
            if (TryHandleMatchEnd())
                yield break;

            roundNumber++;

            if (debugLogs)
            {
                Debug.Log(
                    $"[GamePhaseManager] Starting round {roundNumber} with " +
                    $"{playerManager.GetAlivePlayerCount()} alive players.");
            }

            playerManager.ResetRoundSelections();

            yield return RunTimedPhase(GamePhaseType.RunningAround, runningPhaseDuration);
            if (TryHandleMatchEnd())
                yield break;

            yield return RunTimedPhase(GamePhaseType.Countdown, countdownDuration);
            if (TryHandleMatchEnd())
                yield break;

            yield return RunTimedPhase(GamePhaseType.Freeze, freezeDuration);
            if (TryHandleMatchEnd())
                yield break;

            yield return RunAimPhase();
            if (TryHandleMatchEnd())
                yield break;

            yield return RunShootPhase();
            if (TryHandleMatchEnd())
                yield break;

            if (debugLogs)
                Debug.Log("[GamePhaseManager] Shoot phase resolved. Resetting for next round.");

            if (interRoundDelay > 0f)
                yield return new WaitForSeconds(interRoundDelay);
        }
    }

    private IEnumerator RunTimedPhase(GamePhaseType phase, float duration)
    {
        SetPhase(phase);

        float remaining = duration;
        while (remaining > 0f)
        {
            uiManager?.UpdateCountdown(remaining);
            remaining -= Time.deltaTime;
            yield return null;
        }

        uiManager?.UpdateCountdown(0f);
    }

    private IEnumerator RunAimPhase()
    {
        playerManager.PrepareAimForAlivePlayers();
        SetPhase(GamePhaseType.Aim);

        float elapsed = 0f;

        while (true)
        {
            List<PlayerSlot> alivePlayers = playerManager.GetAlivePlayers();
            uiManager?.UpdateAimStatus(alivePlayers);
            uiManager?.UpdateAlivePlayers(playerManager.GetAlivePlayerCount(), playerManager.Players.Count);

            if (alivePlayers.Count <= 1)
                yield break;

            if (playerManager.AreAllAlivePlayersConfirmed())
            {
                if (debugLogs)
                    Debug.Log("[GamePhaseManager] All alive players confirmed aim targets.");

                break;
            }

            if (aimPhaseMaxDuration > 0f)
            {
                float remaining = Mathf.Max(0f, aimPhaseMaxDuration - elapsed);
                uiManager?.UpdateCountdown(remaining);

                if (elapsed >= aimPhaseMaxDuration)
                {
                    if (debugLogs)
                    {
                        Debug.Log(
                            "[GamePhaseManager] Aim timer expired. Auto-confirming remaining players.");
                    }

                    playerManager.AutoConfirmAlivePlayers();
                    uiManager?.UpdateAimStatus(alivePlayers);
                    break;
                }
            }
            else
            {
                uiManager?.UpdateCountdown(-1f);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private IEnumerator RunShootPhase()
    {
        SetPhase(GamePhaseType.Shoot);
        uiManager?.UpdateCountdown(0f);
        uiManager?.UpdateAimStatus(playerManager.GetAlivePlayers());

        List<PlayerSlot> shooters = playerManager.GetAlivePlayers();
        yield return projectileStrikeSystem.ResolveStrikes(shooters);

        uiManager?.UpdateAimStatus(playerManager.GetAlivePlayers());
        uiManager?.UpdateAlivePlayers(playerManager.GetAlivePlayerCount(), playerManager.Players.Count);
    }

    private void SetPhase(GamePhaseType phase)
    {
        CurrentPhase = phase;

        if (debugLogs)
            Debug.Log($"[GamePhaseManager] Phase changed to {phase}.");

        ApplyPhaseStateToPlayers(phase);
        uiManager?.SetPhase(phase, roundNumber, playerManager != null ? playerManager.GetAlivePlayerCount() : 0);
        uiManager?.UpdateAlivePlayers(
            playerManager != null ? playerManager.GetAlivePlayerCount() : 0,
            playerManager != null ? playerManager.Players.Count : 0);
    }

    private void ApplyPhaseStateToPlayers(GamePhaseType phase)
    {
        if (playerManager == null)
            return;

        foreach (PlayerSlot player in playerManager.Players)
        {
            if (player == null)
                continue;

            bool alive = player.IsAlive;

            if (player.Controller != null)
            {
                player.Controller.SetMovementAllowed(alive && AllowsMovement(phase));
                player.Controller.SetPhaseVisible(ShouldBeVisible(phase, alive));

                if (!alive)
                    player.Controller.SetEliminated(true);
            }

            if (player.AimController != null)
                player.AimController.SetAimActive(alive && phase == GamePhaseType.Aim);

            if (alive && player.Controller != null)
            {
                switch (phase)
                {
                    case GamePhaseType.RunningAround:
                        player.Controller.SetLine2("Running");
                        break;
                    case GamePhaseType.Countdown:
                        player.Controller.SetLine2("Hidden");
                        break;
                    case GamePhaseType.Freeze:
                        player.Controller.SetLine2("Frozen");
                        break;
                    case GamePhaseType.Aim:
                        player.Controller.SetLine2(player.HasConfirmedTarget ? "Locked" : "Aiming...");
                        break;
                    case GamePhaseType.Shoot:
                        player.Controller.SetLine2("Strike!");
                        break;
                    case GamePhaseType.MatchOver:
                        player.Controller.SetLine2("Winner");
                        break;
                }
            }
        }
    }

    private bool TryHandleMatchEnd()
    {
        if (playerManager == null)
            return true;

        int aliveCount = playerManager.GetAlivePlayerCount();

        if (aliveCount > 1)
            return false;

        CurrentPhase = GamePhaseType.MatchOver;
        ApplyPhaseStateToPlayers(CurrentPhase);

        if (aliveCount == 1)
        {
            PlayerSlot winner = playerManager.GetLastAlivePlayer();
            string winnerName = winner != null ? winner.DisplayName : "Unknown";

            if (debugLogs)
                Debug.Log($"[GamePhaseManager] Match over. Winner: {winnerName}.");

            uiManager?.ShowWinner(winnerName);
        }
        else
        {
            if (debugLogs)
                Debug.Log("[GamePhaseManager] Match over. No players survived the shoot phase.");

            uiManager?.ShowDraw();
        }

        uiManager?.SetPhase(GamePhaseType.MatchOver, roundNumber, aliveCount);
        uiManager?.UpdateCountdown(0f);
        uiManager?.UpdateAimStatus(playerManager.GetAlivePlayers());
        uiManager?.UpdateAlivePlayers(aliveCount, playerManager.Players.Count);

        matchLoopRoutine = null;
        return true;
    }

    private bool AllowsMovement(GamePhaseType phase)
    {
        return phase == GamePhaseType.RunningAround || phase == GamePhaseType.Countdown;
    }

    private bool ShouldBeVisible(GamePhaseType phase, bool isAlive)
    {
        if (!isAlive)
            return false;

        if (phase == GamePhaseType.Countdown)
            return false;

        return true;
    }

    private void ResolveReferences()
    {
        if (playerManager == null)
            playerManager = FindFirstObjectByType<PlayerManager>();

        if (projectileStrikeSystem == null)
            projectileStrikeSystem = FindFirstObjectByType<ProjectileStrikeSystem>();

        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIManager>();
    }
}
