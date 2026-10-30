#if UNITY_2019_1_OR_NEWER
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.World
{
    /// <summary>Colored capsule/cube + facing + world-space nameplate (name + HP bar) + select ring.</summary>
    public sealed class LegendEntityView : MonoBehaviour
    {
        public string EntityId { get; private set; } = "";
        public EntityKind Kind { get; private set; }
        public string DisplayName { get; private set; } = "";
        public int Hp { get; private set; }
        public int MaxHp { get; private set; } = 1;
        public int GridX { get; private set; }
        public int GridY { get; private set; }
        public int Direction { get; private set; }
        public bool IsSelf { get; private set; }
        public bool Selected { get; private set; }

        private Vector3 _from;
        private Vector3 _to;
        private float _lerpT = 1f;
        private float _lerpDur = 0.1f;
        private Renderer _renderer = null!;
        private Transform _body = null!;
        private TextMesh _label = null!;
        private Transform _barBg = null!;
        private Transform _barFg = null!;
        private GameObject? _selectRing;
        private GameObject? _facingMarker;

        public void Initialize(EntitySnapshot snap, float cellSize, bool isSelf, float lerpSeconds)
        {
            EntityId = snap.EntityId;
            Kind = snap.Kind;
            DisplayName = string.IsNullOrEmpty(snap.Name) ? snap.EntityId : snap.Name;
            Hp = snap.Hp;
            MaxHp = Mathf.Max(1, snap.MaxHp);
            IsSelf = isSelf;
            Direction = snap.Direction;
            _lerpDur = Mathf.Max(0.01f, lerpSeconds);
            GridX = snap.X;
            GridY = snap.Y;
            var pos = new Vector3(snap.X * cellSize, 0f, snap.Y * cellSize);
            transform.position = pos;
            _from = _to = pos;
            _lerpT = 1f;
            EnsureVisual();
            ApplyColor();
            ApplyFacing();
            RefreshNameplate();
        }

        public void ApplySnapshot(EntitySnapshot snap, float cellSize)
        {
            DisplayName = string.IsNullOrEmpty(snap.Name) ? snap.EntityId : snap.Name;
            Kind = snap.Kind;
            Direction = snap.Direction;
            SetHp(snap.Hp, snap.MaxHp);
            SnapOrLerpTo(snap.X, snap.Y, cellSize, instant: true);
            ApplyColor();
            ApplyFacing();
            RefreshNameplate();
        }

        public void SetHp(int hp, int maxHp)
        {
            Hp = hp;
            MaxHp = Mathf.Max(1, maxHp);
            RefreshNameplate();
        }

        public void SetDirection(int dir)
        {
            Direction = ((dir % 8) + 8) % 8;
            ApplyFacing();
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            ApplyColor();
            EnsureSelectRing();
            if (_selectRing != null)
                _selectRing.SetActive(selected);
        }

        public void LerpToGrid(int x, int y, float cellSize)
        {
            // Face move direction (Mir 0=N … clockwise 45°)
            int dx = x - GridX;
            int dy = y - GridY;
            if (dx != 0 || dy != 0)
                SetDirection(DirFromDelta(dx, dy));
            SnapOrLerpTo(x, y, cellSize, instant: false);
        }

        public static int DirFromDelta(int dx, int dy)
        {
            // Grid: +Y north, +X east → Mir-style 8-dir
            if (dx == 0 && dy > 0) return 0;
            if (dx > 0 && dy > 0) return 1;
            if (dx > 0 && dy == 0) return 2;
            if (dx > 0 && dy < 0) return 3;
            if (dx == 0 && dy < 0) return 4;
            if (dx < 0 && dy < 0) return 5;
            if (dx < 0 && dy == 0) return 6;
            if (dx < 0 && dy > 0) return 7;
            return 4;
        }

        private void SnapOrLerpTo(int x, int y, float cellSize, bool instant)
        {
            GridX = x;
            GridY = y;
            var target = new Vector3(x * cellSize, 0f, y * cellSize);
            if (instant)
            {
                transform.position = target;
                _from = _to = target;
                _lerpT = 1f;
            }
            else
            {
                _from = transform.position;
                _to = target;
                _lerpT = 0f;
            }
        }

        private void Update()
        {
            if (_lerpT < 1f)
            {
                _lerpT = Mathf.Min(1f, _lerpT + Time.deltaTime / _lerpDur);
                transform.position = Vector3.Lerp(_from, _to, _lerpT);
            }
            if (_label != null && Camera.main != null)
            {
                _label.transform.rotation = Quaternion.LookRotation(
                    _label.transform.position - Camera.main.transform.position);
            }
        }

        private void EnsureVisual()
        {
            if (_renderer != null) return;

            PrimitiveType prim = Kind switch
            {
                EntityKind.EntityMonster => PrimitiveType.Cube,
                EntityKind.EntityNpc => PrimitiveType.Cylinder,
                EntityKind.EntityLoot => PrimitiveType.Sphere,
                EntityKind.EntityPet => PrimitiveType.Capsule,
                _ => PrimitiveType.Capsule
            };
            foreach (var r in GetComponentsInChildren<Renderer>())
                Destroy(r.gameObject);

            var body = GameObject.CreatePrimitive(prim);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            if (Kind == EntityKind.EntityLoot)
                body.transform.localScale = Vector3.one * 0.45f;
            else if (IsSelf)
                body.transform.localScale = new Vector3(0.85f, 1.1f, 0.85f);
            _body = body.transform;
            _renderer = body.GetComponent<Renderer>();

            // Nose / facing marker (classic 2D feel: show which way we face)
            if (Kind != EntityKind.EntityLoot)
            {
                var nose = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                nose.name = "Facing";
                Object.Destroy(nose.GetComponent<Collider>());
                nose.transform.SetParent(body.transform, false);
                nose.transform.localPosition = new Vector3(0f, 0.15f, 0.55f);
                nose.transform.localScale = new Vector3(0.28f, 0.28f, 0.28f);
                nose.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, 0.9f);
                _facingMarker = nose;
            }

            var labelGo = new GameObject("Nameplate");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.characterSize = 0.11f;
            _label.fontSize = 52;
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = Color.white;

            var barBgGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barBgGo.name = "HpBg";
            Destroy(barBgGo.GetComponent<Collider>());
            barBgGo.transform.SetParent(transform, false);
            barBgGo.transform.localPosition = new Vector3(0f, 1.72f, 0f);
            barBgGo.transform.localScale = new Vector3(1.05f, 0.09f, 0.09f);
            barBgGo.GetComponent<Renderer>().material.color = new Color(0.08f, 0.08f, 0.08f);
            _barBg = barBgGo.transform;

            var barFgGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barFgGo.name = "HpFg";
            Destroy(barFgGo.GetComponent<Collider>());
            barFgGo.transform.SetParent(_barBg, false);
            barFgGo.transform.localPosition = Vector3.zero;
            barFgGo.transform.localScale = Vector3.one;
            barFgGo.GetComponent<Renderer>().material.color = Color.green;
            _barFg = barFgGo.transform;
        }

        private void EnsureSelectRing()
        {
            if (_selectRing != null) return;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectRing";
            Object.Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ring.transform.localScale = new Vector3(1.4f, 0.045f, 1.4f);
            var r = ring.GetComponent<Renderer>();
            r.material.color = new Color(1f, 0.85f, 0.15f, 0.9f);
            ring.SetActive(false);
            _selectRing = ring;
        }

        private void ApplyFacing()
        {
            // Yaw: Mir 0 = north (+Z)
            float yaw = Direction * 45f;
            if (_body != null)
                _body.localRotation = Quaternion.Euler(0f, yaw, 0f);
            else
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        private void ApplyColor()
        {
            if (_renderer == null) return;
            Color c;
            if (IsSelf) c = new Color(0.25f, 0.95f, 0.35f);
            else if (Kind == EntityKind.EntityMonster) c = new Color(0.95f, 0.25f, 0.2f);
            else if (Kind == EntityKind.EntityNpc) c = new Color(0.95f, 0.85f, 0.2f);
            else if (Kind == EntityKind.EntityLoot) c = new Color(0.95f, 0.78f, 0.2f);
            else if (Kind == EntityKind.EntityPet) c = new Color(0.7f, 0.4f, 0.95f);
            else c = new Color(0.3f, 0.55f, 1f);
            if (Selected) c = Color.Lerp(c, new Color(1f, 0.92f, 0.2f), 0.55f);
            _renderer.material.color = c;
        }

        private void RefreshNameplate()
        {
            if (_label != null)
            {
                string tag = IsSelf ? "[我]" : Kind == EntityKind.EntityMonster ? "[怪]" :
                    Kind == EntityKind.EntityNpc ? "[NPC]" : Kind == EntityKind.EntityLoot ? "[物]" : "";
                _label.text = $"{tag}{DisplayName}\n{Hp}/{MaxHp}";
            }
            if (_barFg != null)
            {
                float r = Mathf.Clamp01((float)Hp / MaxHp);
                _barFg.localScale = new Vector3(r, 1f, 1f);
                _barFg.localPosition = new Vector3((r - 1f) * 0.5f, 0f, 0f);
                _barFg.GetComponent<Renderer>().material.color =
                    r > 0.4f ? Color.green : (r > 0.15f ? Color.yellow : Color.red);
            }
            if (_barBg != null)
                _barBg.gameObject.SetActive(Kind != EntityKind.EntityLoot);
        }
    }
}
#endif
