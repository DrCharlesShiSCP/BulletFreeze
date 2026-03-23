using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Central authority for player detection and session management
public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("References")]
    [SerializeField] private CharacterSelectManager characterSelectManager;

    [Header("Settings")]
    public int maxPlayers = 8;
    public bool gameStarted = false;
    public bool debugDisplay = false;

    // List of all active player slots
    public List<PlayerSlot> players = new List<PlayerSlot>();

    private void Awake()
    {
        // Singleton pattern for easy global access
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        // Ensure required reference exists
        if (characterSelectManager == null)
        {
            Debug.LogError("CharacterSelectManager is not assigned.");
            return;
        }

        // Detect controllers already connected
        DetectExistingGamepads();
    }

    private void Update()
    {
        // Continuously check for newly connected controllers
        PollForNewControllers();

        // Optional debug display for first player
        if (debugDisplay && players.Count > 0)
        {
            DebugControllerState(players[0]);
        }
    }

    // Detect controllers connected at startup
    private void DetectExistingGamepads()
    {
        foreach (var pad in Gamepad.all)
        {
            AddPlayer(pad);
        }
    }

    // Detect controllers connected after startup
    private void PollForNewControllers()
    {
        foreach (var pad in Gamepad.all)
        {
            bool alreadyAdded = false;

            foreach (var player in players)
            {
                if (player.gamepad == pad)
                {
                    alreadyAdded = true;
                    break;
                }
            }

            if (!alreadyAdded)
            {
                AddPlayer(pad);
            }
        }
    }

    // Creates a new PlayerSlot and spawns lobby visuals
    private void AddPlayer(Gamepad pad)
    {
        if (players.Count >= maxPlayers)
            return;

        PlayerSlot newPlayer = new PlayerSlot(pad);
        players.Add(newPlayer);

        characterSelectManager.SpawnPlayerLobbyVisuals(players.Count - 1);
    }

    // Prints basic button states to the console
    private void DebugControllerState(PlayerSlot player)
    {
        if (player == null || player.gamepad == null)
            return;

        Gamepad pad = player.gamepad;

        string state = "Gamepad State\n";
        state += "A: " + pad.buttonSouth.isPressed + "\n";
        state += "B: " + pad.buttonEast.isPressed + "\n";
        state += "X: " + pad.buttonWest.isPressed + "\n";
        state += "Y: " + pad.buttonNorth.isPressed + "\n";
        state += "Start: " + pad.startButton.isPressed + "\n";
        state += "Select: " + pad.selectButton.isPressed + "\n";

        Debug.Log(state);
    }
}