#if UNITY_2019_1_OR_NEWER
using System.Collections.Generic;
using Legend.Client.Config;
using Legend.Client.Session;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.World
{
    /// <summary>Spawns / despawns AOI entities and applies MoveSync interpolation.</summary>
    public sealed class LegendWorldPresenter : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendClientConfig _config = null!;
        private readonly Dictionary<string, LegendEntityView> _entities = new Dictionary<string, LegendEntityView>();
        private Transform _root = null!;
        private GameObject _ground = null!;
        private string? _selectedId;

        public string? SelectedEntityId => _selectedId;
        public LegendEntityView? SelfView { get; private set; }

        public void Bind(LegendSessionService session, LegendClientConfig config)
        {
            _session = session;
            _config = config;
            if (_root == null)
            {
                var go = new GameObject("Entities");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }
            EnsureGround();
        }

        public void WireNetwork()
        {
            var c = _session.Client;
            if (c == null) return;
            c.OnSelfEnteredWorld += e =>
            {
                ClearAll();
                EnsureEntity(e, isSelf: true);
                FollowCamera(e.X, e.Y, instant: true);
            };
            c.OnEntityEnterAoi += e => EnsureEntity(e, isSelf: false);
            c.OnEntitySpawn += e => EnsureEntity(e, isSelf: false);
            c.OnEntityLeaveAoi += (id, _) => RemoveEntity(id);
            c.OnEntityDespawn += (id, _) => RemoveEntity(id);
            c.OnMoveSync += OnMoveSync;
            c.OnMapChange += mc =>
            {
                if (_entities.TryGetValue(mc.EntityId, out var view))
                    view.LerpToGrid(mc.ToX, mc.ToY, _config.CellSize);
                if (_session.Client?.SelfEntity != null && mc.EntityId == _session.Client.SelfEntity.EntityId)
                    FollowCamera(mc.ToX, mc.ToY, instant: true);
            };
            c.OnCombatSync += cs =>
            {
                if (_entities.TryGetValue(cs.TargetEntityId, out var view))
                    view.SetHp(cs.TargetHp, cs.TargetMaxHp);
            };
        }

        public void ClearAll()
        {
            foreach (var kv in _entities)
            {
                if (kv.Value != null)
                    Destroy(kv.Value.gameObject);
            }
            _entities.Clear();
            SelfView = null;
            _selectedId = null;
        }

        public LegendEntityView? Get(string entityId)
        {
            return _entities.TryGetValue(entityId, out var v) ? v : null;
        }

        public void Select(string? entityId)
        {
            if (_selectedId != null && _entities.TryGetValue(_selectedId, out var prev))
                prev.SetSelected(false);
            _selectedId = entityId;
            if (_selectedId != null && _entities.TryGetValue(_selectedId, out var cur))
                cur.SetSelected(true);
        }

        public string? FindNearestAttackable(float maxCells = 2.5f)
        {
            var self = _session.Client?.SelfEntity;
            if (self == null) return null;
            string? best = null;
            float bestDist = maxCells;
            foreach (var kv in _entities)
            {
                var v = kv.Value;
                if (v.IsSelf) continue;
                if (v.Kind != EntityKind.EntityMonster) continue;
                float d = Mathf.Abs(v.GridX - self.X) + Mathf.Abs(v.GridY - self.Y);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = v.EntityId;
                }
            }
            return best;
        }


        public string? FindNearestLoot(float maxCells = 2.5f)
        {
            var self = _session.Client?.SelfEntity;
            if (self == null) return null;
            string? best = null;
            float bestDist = maxCells;
            foreach (var kv in _entities)
            {
                var v = kv.Value;
                if (v.Kind != EntityKind.EntityLoot) continue;
                float d = Mathf.Abs(v.GridX - self.X) + Mathf.Abs(v.GridY - self.Y);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = v.EntityId;
                }
            }
            return best;
        }

        public void TickCamera()
        {
            var self = _session.Client?.SelfEntity;
            if (self == null || SelfView == null) return;
            FollowCamera(SelfView.transform.position.x / _config.CellSize,
                SelfView.transform.position.z / _config.CellSize, instant: false);
        }

        private void OnMoveSync(MoveSync sync)
        {
            if (!sync.Accepted) return;
            if (_entities.TryGetValue(sync.EntityId, out var view))
                view.LerpToGrid(sync.ToX, sync.ToY, _config.CellSize);
        }

        private void EnsureEntity(EntitySnapshot snap, bool isSelf)
        {
            if (snap == null || string.IsNullOrEmpty(snap.EntityId)) return;
            if (_entities.TryGetValue(snap.EntityId, out var existing))
            {
                existing.ApplySnapshot(snap, _config.CellSize);
                if (isSelf) SelfView = existing;
                return;
            }
            var go = new GameObject(snap.Name);
            go.transform.SetParent(_root, false);
            var view = go.AddComponent<LegendEntityView>();
            view.Initialize(snap, _config.CellSize, isSelf, _config.MoveLerpSeconds);
            _entities[snap.EntityId] = view;
            if (isSelf) SelfView = view;
        }

        private void RemoveEntity(string id)
        {
            if (!_entities.TryGetValue(id, out var view)) return;
            if (_selectedId == id) _selectedId = null;
            if (SelfView == view) SelfView = null;
            Destroy(view.gameObject);
            _entities.Remove(id);
        }

        private void EnsureGround()
        {
            if (_ground != null) return;
            int n = Mathf.Max(16, _config.GroundSize);
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Ground";
            _ground.transform.SetParent(transform, false);
            // Unity plane is 10x10 units; scale so it covers n cells
            float scale = (n * _config.CellSize) / 10f;
            _ground.transform.localScale = new Vector3(scale, 1f, scale);
            _ground.transform.position = new Vector3(n * 0.5f * _config.CellSize, 0f, n * 0.5f * _config.CellSize);
            var r = _ground.GetComponent<Renderer>();
            r.material.color = new Color(0.22f, 0.28f, 0.22f);
        }

        private void FollowCamera(float gridX, float gridY, bool instant)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var target = new Vector3(
                gridX * _config.CellSize,
                _config.CameraHeight,
                gridY * _config.CellSize - _config.CameraDistance);
            if (instant)
                cam.transform.position = target;
            else
                cam.transform.position = Vector3.Lerp(cam.transform.position, target, Time.deltaTime * 6f);
            cam.transform.LookAt(new Vector3(gridX * _config.CellSize, 0.5f, gridY * _config.CellSize));
        }
    }
}
#endif
