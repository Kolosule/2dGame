using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The ONLY place that reads local input devices. Fusion calls OnInput each tick
/// on the local client to poll input for the input-authority player.
/// </summary>
public class NetworkInputProvider : MonoBehaviour, INetworkRunnerCallbacks
{
    // Tap latches: OnInput samples "is the key down right now", so a tap that starts and ends
    // entirely between two polls would be lost. Update() runs every render frame and latches
    // press edges; OnInput ORs the latch in and clears it. Only needed for the edge-triggered
    // actions (Jump/Dash/Melee/Shoot/Stealth all act on the press edge in their Simulate paths).
    private bool jumpQueued, dashQueued, meleeQueued, shootQueued, stealthQueued;

    // Last aim point computed with a live camera. Reused when the camera or mouse is momentarily
    // unavailable (scene transition), instead of sending (0,0) = "aim at the world origin".
    private Vector2 lastAimWorld;

    void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var gamepad = Gamepad.current;

        if ((keyboard != null && keyboard.spaceKey.wasPressedThisFrame)     || (gamepad != null && gamepad.buttonNorth.wasPressedThisFrame))   jumpQueued = true;
        if ((keyboard != null && keyboard.leftShiftKey.wasPressedThisFrame) || (gamepad != null && gamepad.rightShoulder.wasPressedThisFrame)) dashQueued = true;
        if ((mouse != null && mouse.leftButton.wasPressedThisFrame)         || (keyboard != null && keyboard.leftCtrlKey.wasPressedThisFrame) || (gamepad != null && gamepad.buttonSouth.wasPressedThisFrame)) meleeQueued = true;
        if ((mouse != null && mouse.rightButton.wasPressedThisFrame)        || (keyboard != null && keyboard.leftAltKey.wasPressedThisFrame)  || (gamepad != null && gamepad.buttonWest.wasPressedThisFrame))  shootQueued = true;
        if ((keyboard != null && keyboard.qKey.wasPressedThisFrame)         || (gamepad != null && gamepad.buttonEast.wasPressedThisFrame))   stealthQueued = true;
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var data = new NetInput();

        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        var gamepad = Gamepad.current;

        // Horizontal (-1/0/1)
        float h = 0f;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) h -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) h += 1f;
        }
        if (gamepad != null)
        {
            float gx = gamepad.leftStick.ReadValue().x;
            if (Mathf.Abs(gx) > 0.2f) h = Mathf.Sign(gx);
        }
        data.Horizontal = (sbyte)Mathf.Clamp(Mathf.RoundToInt(h), -1, 1);

        // Vertical aim (-1/0/1)
        float v = 0f;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) v += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) v -= 1f;
        }
        if (gamepad != null)
        {
            float gy = gamepad.leftStick.ReadValue().y;
            if (Mathf.Abs(gy) > 0.2f) v = Mathf.Sign(gy);
        }
        data.VerticalAim = (sbyte)Mathf.Clamp(Mathf.RoundToInt(v), -1, 1);

        // Buttons: held state OR the Update()-latched tap, so fast taps are never dropped.
        bool jump    = jumpQueued    || (keyboard != null && keyboard.spaceKey.isPressed)     || (gamepad != null && gamepad.buttonNorth.isPressed);
        bool dash    = dashQueued    || (keyboard != null && keyboard.leftShiftKey.isPressed) || (gamepad != null && gamepad.rightShoulder.isPressed);
        bool melee   = meleeQueued   || (mouse != null && mouse.leftButton.isPressed)         || (keyboard != null && keyboard.leftCtrlKey.isPressed) || (gamepad != null && gamepad.buttonSouth.isPressed);
        bool shoot   = shootQueued   || (mouse != null && mouse.rightButton.isPressed)        || (keyboard != null && keyboard.leftAltKey.isPressed)  || (gamepad != null && gamepad.buttonWest.isPressed);
        bool stealth = stealthQueued || (keyboard != null && keyboard.qKey.isPressed)         || (gamepad != null && gamepad.buttonEast.isPressed);
        jumpQueued = dashQueued = meleeQueued = shootQueued = stealthQueued = false;

        data.Buttons.Set((int)PlayerButton.Jump,    jump);
        data.Buttons.Set((int)PlayerButton.Dash,    dash);
        data.Buttons.Set((int)PlayerButton.Melee,   melee);
        data.Buttons.Set((int)PlayerButton.Shoot,   shoot);
        data.Buttons.Set((int)PlayerButton.Stealth, stealth);

        // Aim world point (for projectiles); PlayerCombat turns this into a direction
        // relative to its spawn point at sim time. If the camera or mouse is momentarily
        // unavailable, reuse the last valid point rather than sending (0,0).
        if (mouse != null && Camera.main != null)
        {
            Vector3 mw = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
            lastAimWorld = new Vector2(mw.x, mw.y);
        }
        data.AimWorldPoint = lastAimWorld;

        input.Set(data);
    }

    // --- Unused INetworkRunnerCallbacks members ---
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player) { }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) { }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
}
