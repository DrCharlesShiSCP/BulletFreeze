using UnityEngine;
using System.Collections.Generic;

// Handles lobby character selection input and spawning visuals
public class CharacterSelectManager : MonoBehaviour
{
    [Header("Character Options")]
    [SerializeField] private List<GameObject> characterPrefabs = new List<GameObject>();

    [Header("Spawn Points")]
    [SerializeField] private Transform[] spawnPoints;

    private void Update()
    {
        if (GameManager.Instance.gameStarted)
            return;

        var players = GameManager.Instance.players;

        for (int i = 0; i < players.Count; i++)
        {
            HandleInput(i);
        }
    }

    // Reads controller input for lobby selection
    private void HandleInput(int index)
    {
        PlayerSlot player = GameManager.Instance.players[index];

        if (player == null || player.gamepad == null)
            return;

        if (player.isLocked)
            return;

        // Cycle character selection
        if (player.gamepad.dpad.left.wasPressedThisFrame)
        {
            CycleCharacter(index, -1);
        }

        if (player.gamepad.dpad.right.wasPressedThisFrame)
        {
            CycleCharacter(index, 1);
        }

        // Lock character selection and ready up
        if (player.gamepad.startButton.wasPressedThisFrame)
        {
            LockPlayer(index);
        }
    }

    // Advances character selection index
    private void CycleCharacter(int playerIndex, int direction)
    {
        var players = GameManager.Instance.players;

        if (playerIndex < 0 || playerIndex >= players.Count)
            return;

        PlayerSlot player = players[playerIndex];

        if (player.isLocked)
            return;

        player.selectedIndex += direction;

        if (player.selectedIndex < 0)
            player.selectedIndex = characterPrefabs.Count - 1;

        if (player.selectedIndex >= characterPrefabs.Count)
            player.selectedIndex = 0;

        SpawnPlayerLobbyVisuals(playerIndex);
    }

    // Locks player selection
    private void LockPlayer(int playerIndex)
    {
        PlayerSlot player = GameManager.Instance.players[playerIndex];

        if (player == null)
            return;

        player.isLocked = true;

        if (player.playerCharacterController != null)
        {
            player.playerCharacterController.SetLine2("Ready!");
        }

        CheckAllLocked();
    }

    // Spawns or respawns lobby character preview
    public void SpawnPlayerLobbyVisuals(int playerIndex)
    {
        var players = GameManager.Instance.players;

        if (playerIndex < 0 || playerIndex >= players.Count)
            return;

        PlayerSlot player = players[playerIndex];

        if (playerIndex >= spawnPoints.Length)
        {
            Debug.LogWarning("Not enough spawn points assigned.");
            return;
        }

        if (characterPrefabs.Count == 0)
            return;

        if (player.selectedIndex < 0 ||
            player.selectedIndex >= characterPrefabs.Count)
        {
            player.selectedIndex = 0;
        }

        // Destroy old avatar if it exists
        if (player.currentAvatar != null)
        {
            Destroy(player.currentAvatar);
            player.currentAvatar = null;
        }

        GameObject prefab = characterPrefabs[player.selectedIndex];

        GameObject instance = Instantiate(
            prefab,
            spawnPoints[playerIndex].position,
            spawnPoints[playerIndex].rotation
        );

        PlayerCharacterController controller =
            instance.GetComponent<PlayerCharacterController>();

        if (controller == null)
        {
            Debug.LogError("Prefab missing PlayerCharacterController");
            return;
        }

        player.playerCharacterController = controller;
        player.currentAvatar = instance;
        
        controller.playerSlot = player;
        controller.SetLine1("Player " + (playerIndex + 1));
        controller.SetLine2(player.isLocked ? "Ready!" : "Selecting...");
    }

    // Checks if all players are locked
    private void CheckAllLocked()
    {
        var players = GameManager.Instance.players;

        if (players.Count == 0)
            return;

        foreach (var player in players)
        {
            if (!player.isLocked)
                return;
        }

        StartGame();
    }

    // Starts gameplay session
    private void StartGame()
    {
        Debug.Log("All players locked. Start game here.");

        foreach (var player in GameManager.Instance.players)
        {
            if (player.playerCharacterController != null)
            {
                player.playerCharacterController.SetLine1("");
                player.playerCharacterController.SetLine2("");
            }
        }

        GameManager.Instance.gameStarted = true;
    }
}