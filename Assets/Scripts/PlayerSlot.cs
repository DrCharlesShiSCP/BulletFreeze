using UnityEngine;
using UnityEngine.InputSystem;

// Represents one connected controller and its state
public class PlayerSlot
{
    // The physical gamepad assigned to this player
    public Gamepad gamepad;

    // Currently selected character index
    public int selectedIndex;

    // Whether this player has locked in their choice
    public bool isLocked;

    // Lobby visual controller
    public PlayerCharacterController playerCharacterController;

    // Runtime avatar used in gameplay scenes
    public GameObject currentAvatar;

    public PlayerSlot(Gamepad pad)
    {
        gamepad = pad;
        selectedIndex = 0;
        isLocked = false;
        playerCharacterController = null;
        currentAvatar = null;
    }
}