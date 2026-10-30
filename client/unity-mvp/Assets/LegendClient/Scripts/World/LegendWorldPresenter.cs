#if UNITY_2019_1_OR_NEWER
using System.Collections.Generic;
using Legend.Client.Config;
using Legend.Client.Session;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.World
{
    /// <summary>Spawns / despawns AOI entities, tiled ground (safe vs wild), MoveSync interpolation.</summary>
    public sealed class LegendWorldPresenter : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendClientConfig _config = null!;
        private readonly Dictionary<string, LegendEntityView> _entities = new Dictionary<string, LegendEntityView>();
        private Transform _root = null!;
        private GameObject _ground = null!;
        private GameObject? _clickMarker;
        private string? _selectedId;
        private readonly List<string> _tabOrder = new List<string>();

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
            _tabOrder.Clear();
            HideClickMarker();
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

        /// <summary>Tab-cycle monsters by entity id order (stable).</summary>
        public string? CycleAttackable(int delta = 1)
        {
            _tabOrder.Clear();
            foreach (var kv in _entities)
            {
                if (!kv.Value.IsSelf && kv.Value.Kind == EntityKind.EntityMonster)
                    _tabOrder.Add(kv.Key);
            }
            _tabOrder.Sort(System.StringComparer.Ordinal);
            if (_tabOrder.Count == 0) return null;
            int idx = 0;
            if (!string.IsNullOrEmpty(_selectedId))
            {
                int cur = _tabOrder.IndexOf(_selectedId);
                if (cur >= 0) idx = cur;
            }
            idx = (idx + delta) % _tabOrder.Count;
            if (idx < 0) idx += _tabOrder.Count;
            return _tabOrder[idx];
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

        /// <summary>Raycast ground → grid cell. Returns false if no hit.</summary>
        public bool ScreenToGrid(Vector3 screenPos, out int gx, out int gy)
        {
            gx = 0;
            gy = 0;
            var cam = Camera.main;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float dist)) return false;
            var world = ray.GetPoint(dist);
            gx = Mathf.RoundToInt(world.x / _config.CellSize);
            gy = Mathf.RoundToInt(world.z / _config.CellSize);
            return true;
        }

        public void ShowClickMarker(int gx, int gy)
        {
            if (_clickMarker == null)
            {
                _clickMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                _clickMarker.name = "ClickMoveMarker";
                Object.Destroy(_clickMarker.GetComponent<Collider>());
                _clickMarker.transform.SetParent(transform, false);
                _clickMarker.transform.localScale = new Vector3(0.55f, 0.03f, 0.55f);
                _clickMarker.GetComponent<Renderer>().material.color = new Color(0.2f, 0.95f, 0.45f, 0.75f);
            }
            _clickMarker.SetActive(true);
            _clickMarker.transform.position = new Vector3(gx * _config.CellSize, 0.04f, gy * _config.CellSize);
        }

        public void HideClickMarker()
        {
            if (_clickMarker != null) _clickMarker.SetActive(false);
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
            {
                if (sync.Direction != 0 || sync.ToX != view.GridX || sync.ToY != view.GridY)
                    view.SetDirection(sync.Direction);
                view.LerpToGrid(sync.ToX, sync.ToY, _config.CellSize);
            }
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
            _ground.name = "GroundTiles";
            _ground.transform.SetParent(transform, false);
            float scale = (n * _config.CellSize) / 10f;
            _ground.transform.localScale = new Vector3(scale, 1f, scale);
            _ground.transform.position = new Vector3((n - 1) * 0.5f * _config.CellSize, 0f, (n - 1) * 0.5f * _config.CellSize);

            // Procedural checker + safe-zone tint (novice_plain defaults)
            int texN = Mathf.Clamp(n, 16, 128);
            var tex = new Texture2D(texN, texN, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            int sx1 = _config.SafeZoneX1, sy1 = _config.SafeZoneY1, sx2 = _config.SafeZoneX2, sy2 = _config.SafeZoneY2;
            for (int y = 0; y < texN; y++)
            for (int x = 0; x < texN; x++)
            {
                bool checker = ((x + y) & 1) == 0;
                bool safe = x >= sx1 && x <= sx2 && y >= sy1 && y <= sy2;
                Color c;
                if (safe)
                    c = checker ? new Color(0.32f, 0.48f, 0.34f) : new Color(0.28f, 0.42f, 0.30f);
                else
                    c = checker ? new Color(0.22f, 0.24f, 0.20f) : new Color(0.18f, 0.20f, 0.16f);
                // Soft path tint near spawn
                if (Mathf.Abs(x - 10) <= 1 || Mathf.Abs(y - 10) <= 1)
                    c = Color.Lerp(c, new Color(0.35f, 0.32f, 0.22f), 0.35f);
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            var mat = new Material(Shader.Find("Standard") ?? Shader.Find("Sprites/Default") ?? _ground.GetComponent<Renderer>().sharedMaterial.shader);
            mat.mainTexture = tex;
            mat.color = Color.white;
            _ground.GetComponent<Renderer>().material = mat;

            // Soft grid lines via child quads (sparse) — skip for perf; texture is enough
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
