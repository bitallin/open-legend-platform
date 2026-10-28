# Legend Unity SDK (2022 LTS)

100% original Legend protocol client for **Unity 2022 LTS** (PC + Android). No 996 / third-party engine protocol.

Protocol version: **1.1.0**

## Import

1. Create / open a Unity **2022.3 LTS** project (3D or URP).
2. Copy `client/unity-sdk/Runtime/` into `Assets/LegendSdk/Runtime/`.
3. Install NuGetForUnity (or manually reference) **Google.Protobuf** 3.28.x matching the server.
4. Ensure scripting define / API compatibility level supports C# 9+ (Unity 2022 default is fine).

## Scene setup

1. Create an empty GameObject `LegendDemo`.
2. Add component `LegendWorldDemoBehaviour`.
3. Set `Host` / `Port` to your `Legend.Host` (default `127.0.0.1:7100`).
4. Optional: fill `DebugBlockedCells` / `DebugSafeZones` from map JSON so Scene view Gizmos show walls (red) and safe zones (green).
5. Enter Play Mode → IMGUI panel: Login → Create/Select character → WASD / left-side virtual stick to move.

## API flow

```
Connect → Handshake(1.1.0) → Register → Login
  → CharList / CreateChar / DeleteChar / SelectChar
  → EnterWorld(character_id)
  → MoveReq / MoveSync + MapChangeNotify (portals)
```

High-level helper: `LegendUnityNetworkClient.ConnectLoginSelectEnterAsync(...)`.

Input abstraction:

- `KeyboardWasdInput` — PC
- `VirtualJoystickInput` — touch / on-screen stick (`LegendWorldDemoBehaviour` wires left-half screen)

## Regenerating protocol stubs

After editing `protocol/legend_protocol.proto` on the server:

```bash
dotnet build server/Legend.Contracts/Legend.Contracts.csproj -c Release
cp server/Legend.Contracts/obj/Release/net8.0/LegendProtocol.cs client/unity-sdk/Runtime/LegendProtocol.cs
```

Also keep `Runtime/ProtocolConstants.cs` `MsgId` / version in sync with `server/Legend.Contracts/ProtocolConstants.cs`.

## Notes

- Network callbacks are marshalled to the Unity main thread via `PumpMainThreadEvents()` in `Update()`.
- Blocking / safe-zone Gizmos are **debug aids only**; authority remains on the server.
