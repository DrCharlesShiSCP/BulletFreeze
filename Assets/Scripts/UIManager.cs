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
    [Tooltip("Shows which alive players have confirmed targets.")]
    [SerializeField] private TMP_Text aimStatusText;
    [Tooltip("Shows remaining alive players.")]
    [SerializeField] private TMP_Text alivePlayersText;

    [Header("Winner UI")]
    [Tooltip("Panel enabled when the match ends.")]
    [SerializeField] private GameObject winnerPanel;
    [Tooltip("Winner or draw text inside the winner panel.")]
    [SerializeField] private TMP_Text winnerText;

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
            builder.Append(player.HasConfirmedTarget ? " OK" : " ...");

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
            winnerText.text = $"{winnerName} wins";
    }

    public void ShowDraw()
    {
        if (winnerPanel != null)
            winnerPanel.SetActive(true);

        if (winnerText != null)
            winnerText.text = "Draw";
    }

    public void HideWinner()
    {
        if (winnerPanel != null)
            winnerPanel.SetActive(false);
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
}
