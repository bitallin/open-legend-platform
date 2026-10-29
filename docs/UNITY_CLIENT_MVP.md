# Unity Client MVP (Phase F → G)

Phase F delivered the playable Bootstrap + IMGUI HUD.

**Phase G** upgrades the HUD to runtime **uGUI** — see [`UNITY_CLIENT_UGUI.md`](UNITY_CLIENT_UGUI.md) and [`client/unity-mvp/README.md`](../client/unity-mvp/README.md).

- Protocol: **1.21.0** (unchanged; client-only)
- SDK: `client/unity-sdk/`
- Pure FSM / UI mode tests: `client/Legend.ClientCore/` + `PhaseFClientCoreTests` / `PhaseGClientUiTests`
- Verify: `./scripts/verify-phase-g-client.sh`
