using UnityEngine;
using UnityEngine.InputSystem;

// Represents one connected controller and its state
public class PlayerSlot
{
    public Gamepad gamepad;
    public int selectedIndex;
    public bool isLocked;
    public PlayerCharacterController playerCharacterController;
    public GameObject currentAvatar;
    
    public int PlayerId { get; private set; }
    public bool UsesKeyboard { get; private set; }
    public bool IsDebugPlayer { get; private set; }
    public bool IsAlive { get; private set; }
    public bool HasConfirmedTarget { get; private set; }
    public Vector3 ConfirmedTargetPoint { get; private set; }
    public PlayerController Controller { get; private set; }
    public PlayerAimController AimController { get; private set; }
    public string DisplayName => PlayerId > 0 ? $"P{PlayerId}" : "Player";
    public string InputLabel => IsDebugPlayer
        ? "Debug Fake"
        : UsesKeyboard
        ? "Keyboard"
        : gamepad != null
            ? $"{gamepad.displayName} ({gamepad.deviceId})"
            : "Unassigned";
    public bool HasPlayableInput => UsesKeyboard || gamepad != null || IsDebugPlayer;

    public PlayerSlot(Gamepad pad)
        : this(0, pad, false)
    {
    }

    public PlayerSlot(int playerId, Gamepad pad, bool usesKeyboard)
        : this(playerId, pad, usesKeyboard, false)
    {
    }

    public PlayerSlot(int playerId, Gamepad pad, bool usesKeyboard, bool isDebugPlayer)
    {
        PlayerId = playerId;
        gamepad = pad;
        UsesKeyboard = usesKeyboard;
        IsDebugPlayer = isDebugPlayer;
        selectedIndex = 0;
        isLocked = false;
        playerCharacterController = null;
        currentAvatar = null;
        IsAlive = true;
        HasConfirmedTarget = false;
        ConfirmedTargetPoint = Vector3.zero;
    }

    public void Bind(PlayerController controller, PlayerAimController aimController)
    {
        Controller = controller;
        AimController = aimController;
        currentAvatar = controller != null ? controller.gameObject : null;
        playerCharacterController = controller != null
            ? controller.GetComponent<PlayerCharacterController>()
            : null;
    }

    public void SetAlive(bool isAlive)
    {
        IsAlive = isAlive;
    }

    public void ClearConfirmedTarget()
    {
        HasConfirmedTarget = false;
        ConfirmedTargetPoint = Vector3.zero;
    }

    public void ConfirmTarget(Vector3 point)
    {
        HasConfirmedTarget = true;
        ConfirmedTargetPoint = point;
    }

    public Vector2 ReadMoveInput()
    {
        if (IsDebugPlayer)
            return Vector2.zero;

        if (UsesKeyboard)
        {
            return ReadKeyboardVector(
                Keyboard.current,
                Key.W,
                Key.S,
                Key.A,
                Key.D);
        }

        if (gamepad == null)
            return Vector2.zero;

        Vector2 moveInput = gamepad.leftStick.ReadValue();

        if (moveInput.sqrMagnitude > 0.01f)
            return moveInput;

        return ReadDigitalVector(
            gamepad.dpad.up.isPressed,
            gamepad.dpad.down.isPressed,
            gamepad.dpad.left.isPressed,
            gamepad.dpad.right.isPressed);
    }

    public Vector2 ReadAimInput()
    {
        if (IsDebugPlayer)
            return Vector2.zero;

        if (gamepad == null)
            return Vector2.zero;

        return gamepad.rightStick.ReadValue();
    }

    public bool WasConfirmPressedThisFrame()
    {
        if (IsDebugPlayer)
            return false;

        if (UsesKeyboard)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.enterKey.wasPressedThisFrame ||
                    keyboard.spaceKey.wasPressedThisFrame);
        }

        return gamepad != null &&
               (gamepad.buttonSouth.wasPressedThisFrame ||
                gamepad.rightShoulder.wasPressedThisFrame);
    }

    public bool WasJoinPressedThisFrame()
    {
        if (IsDebugPlayer)
            return false;

        if (UsesKeyboard)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null &&
                   (keyboard.spaceKey.wasPressedThisFrame ||
                    keyboard.enterKey.wasPressedThisFrame);
        }

        return gamepad != null &&
               (gamepad.buttonSouth.wasPressedThisFrame ||
                gamepad.startButton.wasPressedThisFrame);
    }

    public bool WasStartPressedThisFrame()
    {
        if (IsDebugPlayer)
            return false;

        if (UsesKeyboard)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.enterKey.wasPressedThisFrame;
        }

        return gamepad != null && gamepad.startButton.wasPressedThisFrame;
    }

    private static Vector2 ReadKeyboardVector(
        Keyboard keyboard,
        Key up,
        Key down,
        Key left,
        Key right)
    {
        if (keyboard == null)
            return Vector2.zero;

        return ReadDigitalVector(
            keyboard[up].isPressed,
            keyboard[down].isPressed,
            keyboard[left].isPressed,
            keyboard[right].isPressed);
    }

    private static Vector2 ReadDigitalVector(
        bool up,
        bool down,
        bool left,
        bool right)
    {
        float x = 0f;
        float y = 0f;

        if (left)
            x -= 1f;
        if (right)
            x += 1f;
        if (down)
            y -= 1f;
        if (up)
            y += 1f;

        Vector2 value = new Vector2(x, y);
        return value.sqrMagnitude > 1f ? value.normalized : value;
    }
}
