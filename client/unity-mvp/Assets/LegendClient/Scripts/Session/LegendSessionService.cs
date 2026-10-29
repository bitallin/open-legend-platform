#if UNITY_2019_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using Legend.UnitySdk;
using UnityEngine;

namespace Legend.Client.Session
{
    /// <summary>Wraps <see cref="LegendUnityNetworkClient"/> and drives <see cref="LegendSessionFsm"/>.</summary>
    public sealed class LegendSessionService
    {
        public LegendSessionFsm Fsm { get; } = new LegendSessionFsm();
        public LegendUnityNetworkClient? Client { get; private set; }
        public InventoryViewModel Inventory { get; } = new InventoryViewModel();
        public ChatLogBuffer ChatLog { get; } = new ChatLogBuffer(50);
        public IReadOnlyList<CharacterSummary> Characters => _chars;
        public string Host { get; private set; } = "127.0.0.1";
        public int Port { get; private set; } = 7100;
        public string Platform { get; private set; } = "PC";
        public string RealmId { get; private set; } = "realm-classic-01";
        public string LastUsername { get; private set; } = "";
        public string LastPassword { get; private set; } = "";
        public bool WantReconnectHint { get; private set; }

        private readonly List<CharacterSummary> _chars = new List<CharacterSummary>();
        private bool _suppressDisconnectFail;

        public event Action? StateChanged;

        public void Configure(string host, int port, string platform, string realmId)
        {
            Host = host;
            Port = port;
            Platform = platform;
            RealmId = realmId;
        }

        public void Pump()
        {
            Client?.PumpMainThreadEvents();
        }

        public async Task LoginOrRegisterAsync(string username, string password)
        {
            LastUsername = username;
            LastPassword = password;
            if (!Fsm.BeginConnect(Host, Port))
                return;
            Notify();
            try
            {
                DisposeClient();
                Client = new LegendUnityNetworkClient();
                Wire(Client);
                await Client.ConnectAsync(Host, Port);
                await Client.HandshakeAsync();
                await Client.RegisterAsync(username, password, Platform);
                await Client.LoginAsync(username, password, Platform);
                await RefreshCharListAsync();
                Fsm.MarkAuthenticated();
                WantReconnectHint = false;
                Notify();
            }
            catch (Exception ex)
            {
                Fsm.Fail(ex.Message);
                Notify();
            }
        }

        public async Task RefreshCharListAsync()
        {
            if (Client == null) return;
            _chars.Clear();
            _chars.AddRange(await Client.CharListAsync());
            Notify();
        }

        public async Task CreateCharAsync(string name, string classId)
        {
            if (Client == null) return;
            try
            {
                await Client.CreateCharAsync(name, classId);
                await RefreshCharListAsync();
                Fsm.SetStatus("Created " + name);
                Notify();
            }
            catch (Exception ex)
            {
                Fsm.Fail(ex.Message);
                Notify();
            }
        }

        public async Task SelectAndEnterAsync(CharacterSummary character)
        {
            if (Client == null || character == null) return;
            try
            {
                Fsm.BeginEnterWorld(character.Name);
                Notify();
                await Client.SelectCharAsync(character.CharacterId);
                await Client.EnterWorldAsync(RealmId, character.CharacterId);
                SyncInventoryFromClient();
                var map = Client.SelfEntity?.MapId ?? "?";
                Fsm.MarkInWorld(map);
                ChatLog.Add($"[system] Entered {map}");
                Notify();
            }
            catch (Exception ex)
            {
                Fsm.Fail(ex.Message);
                Notify();
            }
        }

        public async Task ReconnectAsync()
        {
            if (string.IsNullOrEmpty(LastUsername))
            {
                Fsm.Fail("No saved credentials");
                Notify();
                return;
            }
            Fsm.Reset();
            await LoginOrRegisterAsync(LastUsername, LastPassword);
        }

        public async Task<MoveSync?> TryMoveAsync(int toX, int toY, int dir)
        {
            if (Client == null || Fsm.Phase != SessionPhase.InWorld) return null;
            try
            {
                return await Client.RequestMoveAsync(toX, toY, dir);
            }
            catch (Exception ex)
            {
                Fsm.SetStatus("Move: " + ex.Message);
                Notify();
                return null;
            }
        }

        public async Task AttackAsync(string targetEntityId, string skillId)
        {
            if (Client == null || Fsm.Phase != SessionPhase.InWorld) return;
            try
            {
                var (resp, _) = await Client.RequestAttackAsync(targetEntityId, skillId);
                if (!resp.Success)
                    Fsm.SetStatus("Attack: " + resp.Message);
                Notify();
            }
            catch (Exception ex)
            {
                Fsm.SetStatus("Attack: " + ex.Message);
                Notify();
            }
        }

        public async Task SendChatAsync(ChatChannel channel, string content)
        {
            if (Client == null || string.IsNullOrWhiteSpace(content)) return;
            try
            {
                await Client.RequestChatAsync(channel, content.Trim());
            }
            catch (Exception ex)
            {
                ChatLog.Add("[error] " + ex.Message);
                Notify();
            }
        }

        public async Task PickupAsync(string lootEntityId)
        {
            if (Client == null) return;
            try
            {
                var resp = await Client.RequestPickupAsync(lootEntityId);
                if (resp.Success)
                    SyncInventoryFromClient();
                Notify();
            }
            catch (Exception ex)
            {
                Fsm.SetStatus("Pickup: " + ex.Message);
                Notify();
            }
        }

        public void DisposeClient()
        {
            _suppressDisconnectFail = true;
            try
            {
                Client?.Dispose();
            }
            finally
            {
                Client = null;
                _suppressDisconnectFail = false;
            }
        }

        private void Wire(LegendUnityNetworkClient client)
        {
            client.OnInventoryNotify += inv =>
            {
                Inventory.Replace(ToTuples(inv.Items), inv.Gold);
                Notify();
            };
            client.OnChatNotify += chat =>
            {
                ChatLog.Add($"[{chat.Channel}] {chat.SenderName}: {chat.Content}");
                Notify();
            };
            client.OnLevelUp += lu =>
            {
                ChatLog.Add($"[system] Level up → {lu.NewLevel}");
                Notify();
            };
            client.OnExpGain += _ => Notify();
            client.OnServerNotice += n =>
            {
                ChatLog.Add($"[notice] {n.Title}: {n.Content}");
                Notify();
            };
            client.OnDisconnected += () =>
            {
                if (_suppressDisconnectFail) return;
                if (Fsm.Phase == SessionPhase.InWorld || Fsm.Phase == SessionPhase.EnteringWorld ||
                    Fsm.Phase == SessionPhase.CharSelect)
                {
                    WantReconnectHint = true;
                    Fsm.MarkDisconnected(expectReconnect: true);
                    Notify();
                }
            };
        }

        private void SyncInventoryFromClient()
        {
            if (Client == null) return;
            Inventory.Replace(ToTuples(Client.Inventory), Client.Gold);
        }

        private static IEnumerable<(string, int)> ToTuples(IReadOnlyList<InventoryItem> items)
        {
            if (items == null) yield break;
            foreach (var it in items)
                yield return (it.ItemId, it.Count);
        }

        private static IEnumerable<(string, int)> ToTuples(Google.Protobuf.Collections.RepeatedField<InventoryItem> items)
        {
            if (items == null) yield break;
            foreach (var it in items)
                yield return (it.ItemId, it.Count);
        }

        private void Notify() => StateChanged?.Invoke();
    }
}
#endif
