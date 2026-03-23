using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

// Minimal scene UI for phase, timer, confirmation, and winner state.
public class UIManager : MonoBehaviour
{
    [Header("Labels")]
    [Tooltip("Main phase label shown during play.")]
    [SerializeField] private TMP_Text phaseText;
    [Tooltip("Countdown or phase timer text.")]
    [SerializeField] private TMP_Text timerText;
    [Tooltip("Shows which alive players are still aiming or have their target locked.")]
    [SerializeField] private TMP_Text aimStatusText;
    [Tooltip("Shows remaining alive players.")]
    [SerializeField] private TMP_Text alivePlayersText;

    [Header("Winner UI")]
    [Tooltip("Panel enabled when the match ends.")]
    [SerializeField] private GameObject winnerPanel;
    [Tooltip("Winner or draw text inside the winner panel.")]
    [SerializeField] private TMP_Text winnerText;

    [Header("Kill Feed")]
    [Tooltip("Parent container for rolling kill messages.")]
    [SerializeField] private RectTransform killFeedRoot;
    [Tooltip("Template TMP text used for each kill feed entry.")]
    [SerializeField] private TMP_Text killFeedLinePrefab;
    [Tooltip("Maximum number of kill feed lines shown at once.")]
    [SerializeField] private int killFeedMaxEntries = 5;
    [Tooltip("How long each kill message remains visible before fading.")]
    [SerializeField] private float killFeedMessageLifetime = 3f;
    [Tooltip("How long each kill message spends fading out.")]
    [SerializeField] private float killFeedFadeDuration = 0.75f;
    [Tooltip("Vertical spacing between kill feed entries.")]
    [SerializeField] private float killFeedLineSpacing = 28f;

    private readonly List<KillFeedEntry> killFeedEntries = new List<KillFeedEntry>();
    private PlayerManager subscribedPlayerManager;

    private void Awake()
    {
        WarnAboutMissingReferences();
    }

    private void Start()
    {
        SubscribeToPlayerManager();
    }

    private void OnEnable()
    {
        SubscribeToPlayerManager();
    }

    private void OnDisable()
    {
        UnsubscribeFromPlayerManager();
    }

    public void SetPhase(GamePhaseType phase, int roundNumber, int alivePlayers)
    {
        if (phaseText == null)
            return;

        string phaseLabel = phase switch
        {
            GamePhaseType.RunningAround => "Running Around",
            GamePhaseType.Countdown => "Countdown",
            GamePhaseType.Freeze => "Freeze",
            GamePhaseType.Aim => "Aim",
            GamePhaseType.Shoot => "Shoot",
            GamePhaseType.MatchOver => "Match Over",
            _ => "Waiting"
        };

        if (phase == GamePhaseType.MatchOver)
        {
            phaseText.text = phaseLabel;
        }
        else
        {
            phaseText.text = $"Round {Mathf.Max(1, roundNumber)} - {phaseLabel}";
        }
    }

    public void UpdateCountdown(float remainingSeconds)
    {
        if (timerText == null)
            return;

        if (remainingSeconds < 0f)
        {
            timerText.text = string.Empty;
            return;
        }

        timerText.text = remainingSeconds.ToString("0.0");
    }

    public void UpdateAimStatus(IReadOnlyList<PlayerSlot> alivePlayers)
    {
        if (aimStatusText == null)
            return;

        if (alivePlayers == null || alivePlayers.Count == 0)
        {
            aimStatusText.text = string.Empty;
            return;
        }

        StringBuilder builder = new StringBuilder("Aim: ");

        for (int i = 0; i < alivePlayers.Count; i++)
        {
            PlayerSlot player = alivePlayers[i];
            builder.Append(player.DisplayName);
            builder.Append(player.HasConfirmedTarget ? " Locked" : " Targeting");

            if (i < alivePlayers.Count - 1)
                builder.Append(" | ");
        }

        aimStatusText.text = builder.ToString();
    }

    public void UpdateAlivePlayers(int alivePlayers, int totalPlayers)
    {
        if (alivePlayersText == null)
            return;

        alivePlayersText.text = $"Alive: {alivePlayers}/{Mathf.Max(alivePlayers, totalPlayers)}";
    }

    public void ShowLobby(IReadOnlyList<PlayerSlot> joinedPlayers, int minimumPlayersToStart)
    {
        if (phaseText != null)
            phaseText.text = "Waiting For Players";

        if (timerText != null)
            timerText.text = $"Need {minimumPlayersToStart}+ to start";

        if (alivePlayersText != null)
        {
            int joinedCount = joinedPlayers != null ? joinedPlayers.Count : 0;
            alivePlayersText.text = $"Joined: {joinedCount}/{Mathf.Max(minimumPlayersToStart, joinedCount)}";
        }

        if (aimStatusText == null)
            return;

        if (joinedPlayers == null || joinedPlayers.Count == 0)
        {
            aimStatusText.text = "Press A / Start on a controller, or Space / Enter on keyboard, to join.";
            return;
        }

        StringBuilder builder = new StringBuilder();
        builder.Append("Joined: ");

        for (int i = 0; i < joinedPlayers.Count; i++)
        {
            PlayerSlot player = joinedPlayers[i];
            builder.Append(player.DisplayName);
            builder.Append(" ");
            builder.Append("(");
            builder.Append(player.InputLabel);
            builder.Append(")");

            if (i < joinedPlayers.Count - 1)
                builder.Append(" | ");
        }

        if (joinedPlayers.Count >= minimumPlayersToStart)
            builder.Append("\nPress Start / Enter to begin");
        else
            builder.Append("\nWaiting for more players");

        aimStatusText.text = builder.ToString();
    }

    public void ShowWinner(string winnerName)
    {
        if (winnerPanel != null)
            winnerPanel.SetActive(true);

        if (winnerText != null)
        {
            winnerText.gameObject.SetActive(true);
            winnerText.text = $"{winnerName} wins";
        }
    }

    public void ShowDraw()
    {
        if (winnerPanel != null)
            winnerPanel.SetActive(true);

        if (winnerText != null)
        {
            winnerText.gameObject.SetActive(true);
            winnerText.text = "Draw";
        }
    }

    public void HideWinner()
    {
        if (winnerPanel != null)
            winnerPanel.SetActive(false);

        if (winnerText != null)
        {
            winnerText.text = string.Empty;
            winnerText.gameObject.SetActive(false);
        }
    }

    public void AddKillFeedMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (killFeedLinePrefab == null)
        {
            Debug.LogWarning("[UIManager] Kill feed line prefab reference is missing.");
            return;
        }

        RectTransform root = ResolveKillFeedRoot();
        if (root == null)
        {
            Debug.LogWarning("[UIManager] Kill feed root reference is missing.");
            return;
        }

        TMP_Text lineInstance = Instantiate(killFeedLinePrefab, root);
        lineInstance.gameObject.SetActive(true);
        lineInstance.text = message;
        SetTextAlpha(lineInstance, 1f);

        KillFeedEntry entry = new KillFeedEntry
        {
            text = lineInstance
        };

        killFeedEntries.Add(entry);
        RefreshKillFeedLayout();

        if (killFeedEntries.Count > Mathf.Max(1, killFeedMaxEntries))
            RemoveKillFeedEntry(killFeedEntries[0]);

        entry.fadeCoroutine = StartCoroutine(FadeKillFeedEntry(entry));
    }

    public void ClearKillFeed()
    {
        while (killFeedEntries.Count > 0)
            RemoveKillFeedEntry(killFeedEntries[0]);
    }

    public void ShowWaitingForPlayers()
    {
        if (phaseText != null)
            phaseText.text = "Waiting For Players";

        if (timerText != null)
            timerText.text = string.Empty;

        if (aimStatusText != null)
            aimStatusText.text = "Press join on an available device.";
    }

    private void WarnAboutMissingReferences()
    {
        if (phaseText == null)
            Debug.LogWarning("[UIManager] Phase text reference is missing.");

        if (timerText == null)
            Debug.LogWarning("[UIManager] Timer text reference is missing.");

        if (aimStatusText == null)
            Debug.LogWarning("[UIManager] Aim status text reference is missing.");

        if (alivePlayersText == null)
            Debug.LogWarning("[UIManager] Alive players text reference is missing.");

        if (winnerPanel == null)
            Debug.LogWarning("[UIManager] Winner panel reference is missing.");

        if (winnerText == null)
            Debug.LogWarning("[UIManager] Winner text reference is missing.");

        if (killFeedLinePrefab == null)
            Debug.LogWarning("[UIManager] Kill feed line prefab reference is missing.");
    }

    private void SubscribeToPlayerManager()
    {
        if (subscribedPlayerManager != null)
            return;

        if (PlayerManager.Instance == null)
            return;

        subscribedPlayerManager = PlayerManager.Instance;
        subscribedPlayerManager.KillFeedMessageAdded += AddKillFeedMessage;
    }

    private void UnsubscribeFromPlayerManager()
    {
        if (subscribedPlayerManager == null)
            return;

        subscribedPlayerManager.KillFeedMessageAdded -= AddKillFeedMessage;
        subscribedPlayerManager = null;
    }

    private RectTransform ResolveKillFeedRoot()
    {
        if (killFeedRoot != null)
            return killFeedRoot;

        if (killFeedLinePrefab != null)
            killFeedRoot = killFeedLinePrefab.transform.parent as RectTransform;

        return killFeedRoot;
    }

    private void RefreshKillFeedLayout()
    {
        RectTransform root = ResolveKillFeedRoot();
        if (root == null)
            return;

        for (int i = 0; i < killFeedEntries.Count; i++)
        {
            TMP_Text entryText = killFeedEntries[i].text;
            if (entryText == null)
                continue;

            RectTransform lineRect = entryText.rectTransform;
            lineRect.SetParent(root, false);
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.anchoredPosition = new Vector2(0f, -i * killFeedLineSpacing);
        }
    }

    private System.Collections.IEnumerator FadeKillFeedEntry(KillFeedEntry entry)
    {
        float holdTime = Mathf.Max(0f, killFeedMessageLifetime - killFeedFadeDuration);
        if (holdTime > 0f)
            yield return new WaitForSeconds(holdTime);

        float fadeTime = Mathf.Max(0.01f, killFeedFadeDuration);
        float elapsed = 0f;

        while (elapsed < fadeTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Lerp(1f, 0f, elapsed / fadeTime);

            if (entry.text != null)
                SetTextAlpha(entry.text, alpha);

            yield return null;
        }

        RemoveKillFeedEntry(entry);
    }

    private void RemoveKillFeedEntry(KillFeedEntry entry)
    {
        if (entry == null)
            return;

        if (entry.fadeCoroutine != null)
            StopCoroutine(entry.fadeCoroutine);

        killFeedEntries.Remove(entry);

        if (entry.text != null)
            Destroy(entry.text.gameObject);

        RefreshKillFeedLayout();
    }

    private static void SetTextAlpha(TMP_Text text, float alpha)
    {
        if (text == null)
            return;

        Color color = text.color;
        color.a = Mathf.Clamp01(alpha);
        text.color = color;
    }

    private class KillFeedEntry
    {
        public TMP_Text text;
        public Coroutine fadeCoroutine;
    }
}
