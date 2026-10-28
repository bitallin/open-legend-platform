#if UNITY_5_3_OR_NEWER
using System.Collections.Generic;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.UnitySdk
{
    /// <summary>
    /// Drop onto an empty GameObject in Unity 2022 LTS to connect to Legend.Host,
    /// spawn placeholder capsules for self & AOI peers, and move with arrow keys / WASD.
    /// </summary>
    public sealed class LegendWorldDemoBehaviour : MonoBehaviour
    {
        [Header("Server Connection")]
        public string Host = "127.0.0.1";
        public int Port = 7100;
        public string Username = "unity_hero";
        public string Password = "pass123";
        public string CharacterName = "Unity先锋";
        public string ClassId = "warrior";

        private LegendUnityNetworkClient? _client;
        private readonly Dictionary<string, GameObject> _entityCapsules = new();
        private int _myX = 10;
        private int _myY = 10;

        private async void Start()
        {
            _client = new LegendUnityNetworkClient();
            _client.OnSelfEnteredWorld += SpawnOrUpdateCapsule;
            _client.OnEntityEnterAoi += SpawnOrUpdateCapsule;
            _client.OnEntityLeaveAoi += (entityId, _) =>
            {
                if (_entityCapsules.TryGetValue(entityId, out var go))
                {
                    Destroy(go);
                    _entityCapsules.Remove(entityId);
                }
            };
            _client.OnMoveSync += sync =>
            {
                if (!sync.Accepted)
                {
                    Debug.LogWarning($"[LegendSDK] Move rejected: {sync.Reason}");
                    return;
                }
                if (_client?.SelfEntity != null && sync.EntityId == _client.SelfEntity.EntityId)
                {
                    _myX = sync.ToX;
                    _myY = sync.ToY;
                }
                if (_entityCapsules.TryGetValue(sync.EntityId, out var go))
                {
                    go.transform.position = new Vector3(sync.ToX, 0f, sync.ToY);
                }
            };
            _client.OnServerNotice += notice =>
            {
                Debug.Log($"[LegendSDK Notice] [{notice.NoticeType}] {notice.Title}: {notice.Content} (Item={notice.GrantedItemId} x{notice.GrantedItemCount})");
            };

            string platform = Application.isMobilePlatform ? "Android" : "PC";
            await _client.ConnectAndEnterAsync(Host, Port, Username, Password, CharacterName, ClassId, platform);
        }

        private void Update()
        {
            _client?.PumpMainThreadEvents();

            if (_client?.SelfEntity == null)
                return;

            int dx = 0, dy = 0;
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) dy = 1;
            else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) dy = -1;
            else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dx = -1;
            else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dx = 1;

            if (dx != 0 || dy != 0)
            {
                _ = _client.RequestMoveAsync(_myX + dx, _myY + dy, direction: 4);
            }
        }

        private void SpawnOrUpdateCapsule(EntitySnapshot snap)
        {
            if (!_entityCapsules.TryGetValue(snap.EntityId, out var go))
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = $"{snap.Name} ({snap.EntityId})";
                _entityCapsules[snap.EntityId] = go;
            }
            go.transform.position = new Vector3(snap.X, 0f, snap.Y);
            if (_client?.SelfEntity != null && snap.EntityId == _client.SelfEntity.EntityId)
            {
                _myX = snap.X;
                _myY = snap.Y;
            }
        }

        private void OnDestroy()
        {
            _client?.Dispose();
        }
    }
}
#endif
