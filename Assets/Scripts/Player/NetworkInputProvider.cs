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

        // Buttons
        bool jump  = (keyboard != null && keyboard.spaceKey.isPressed)    || (gamepad != null && gamepad.buttonNorth.isPressed);
        bool dash  = (keyboard != null && keyboard.leftShiftKey.isPressed) || (gamepad != null && gamepad.rightShoulder.isPressed);
        bool melee = (mouse != null && mouse.leftButton.isPressed)         || (keyboard != null && keyboard.leftCtrlKey.isPressed) || (gamepad != null && gamepad.buttonSouth.isPressed);
        bool shoot = (mouse != null && mouse.rightButton.isPressed)        || (keyboard != null && keyboard.leftAltKey.isPressed)  || (gamepad != null && gamepad.buttonWest.isPressed);

        data.Buttons.Set((int)PlayerButton.Jump,  jump);
        data.Buttons.Set((int)PlayerButton.Dash,  dash);
        data.Buttons.Set((int)PlayerButton.Melee, melee);
        data.Buttons.Set((int)PlayerButton.Shoot, shoot);

        // Aim world point (for projectiles); PlayerCombat turns this into a direction
        // relative to its spawn point at sim time.
        Vector2 aimWorld = Vector2.zero;
        if (mouse != null && Camera.main != null)
        {
            Vector3 mw = Camera.main.ScreenToWorldPoint(mouse.position.ReadValue());
            aimWorld = new Vector2(mw.x, mw.y);
        }
        data.AimWorldPoint = aimWorld;

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
