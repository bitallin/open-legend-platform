#if UNITY_2019_1_OR_NEWER
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.World
{
    /// <summary>Capsule/cube placeholder + world-space nameplate (name + HP bar).</summary>
    public sealed class LegendEntityView : MonoBehaviour
    {
        public string EntityId { get; private set; } = "";
        public EntityKind Kind { get; private set; }
        public string DisplayName { get; private set; } = "";
        public int Hp { get; private set; }
        public int MaxHp { get; private set; } = 1;
        public int GridX { get; private set; }
        public int GridY { get; private set; }
        public bool IsSelf { get; private set; }
        public bool Selected { get; private set; }

        private Vector3 _from;
        private Vector3 _to;
        private float _lerpT = 1f;
        private float _lerpDur = 0.1f;
        private Renderer _renderer = null!;
        private TextMesh _label = null!;
        private Transform _barBg = null!;
        private Transform _barFg = null!;
        private GameObject? _selectRing;

        public void Initialize(EntitySnapshot snap, float cellSize, bool isSelf, float lerpSeconds)
        {
            EntityId = snap.EntityId;
            Kind = snap.Kind;
            DisplayName = string.IsNullOrEmpty(snap.Name) ? snap.EntityId : snap.Name;
            Hp = snap.Hp;
            MaxHp = Mathf.Max(1, snap.MaxHp);
            IsSelf = isSelf;
            _lerpDur = Mathf.Max(0.01f, lerpSeconds);
            GridX = snap.X;
            GridY = snap.Y;
            var pos = new Vector3(snap.X * cellSize, 0f, snap.Y * cellSize);
            transform.position = pos;
            _from = _to = pos;
            _lerpT = 1f;
            EnsureVisual();
            ApplyColor();
            RefreshNameplate();
        }

        public void ApplySnapshot(EntitySnapshot snap, float cellSize)
        {
            DisplayName = string.IsNullOrEmpty(snap.Name) ? snap.EntityId : snap.Name;
            Kind = snap.Kind;
            SetHp(snap.Hp, snap.MaxHp);
            SnapOrLerpTo(snap.X, snap.Y, cellSize, instant: true);
            ApplyColor();
            RefreshNameplate();
        }

        public void SetHp(int hp, int maxHp)
        {
            Hp = hp;
            MaxHp = Mathf.Max(1, maxHp);
            RefreshNameplate();
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
            SnapOrLerpTo(x, y, cellSize, instant: false);
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
            // Rebuild mesh: destroy default if any and create child primitive
            foreach (var r in GetComponentsInChildren<Renderer>())
                Destroy(r.gameObject);

            var body = GameObject.CreatePrimitive(prim);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            if (Kind == EntityKind.EntityLoot)
                body.transform.localScale = Vector3.one * 0.45f;
            // Keep collider on body for click select
            _renderer = body.GetComponent<Renderer>();

            var labelGo = new GameObject("Nameplate");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, 2.0f, 0f);
            _label = labelGo.AddComponent<TextMesh>();
            _label.characterSize = 0.12f;
            _label.fontSize = 48;
            _label.anchor = TextAnchor.LowerCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = Color.white;

            var barBgGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barBgGo.name = "HpBg";
            Destroy(barBgGo.GetComponent<Collider>());
            barBgGo.transform.SetParent(transform, false);
            barBgGo.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            barBgGo.transform.localScale = new Vector3(1.0f, 0.08f, 0.08f);
            barBgGo.GetComponent<Renderer>().material.color = new Color(0.1f, 0.1f, 0.1f);
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
            ring.transform.localScale = new Vector3(1.35f, 0.04f, 1.35f);
            var r = ring.GetComponent<Renderer>();
            r.material.color = new Color(1f, 0.85f, 0.15f, 0.85f);
            ring.SetActive(false);
            _selectRing = ring;
        }

        private void ApplyColor()
        {
            if (_renderer == null) return;
            Color c;
            if (IsSelf) c = new Color(0.25f, 0.95f, 0.35f);
            else if (Kind == EntityKind.EntityMonster) c = new Color(0.95f, 0.25f, 0.2f);
            else if (Kind == EntityKind.EntityNpc) c = new Color(0.95f, 0.85f, 0.2f);
            else if (Kind == EntityKind.EntityLoot) c = new Color(0.9f, 0.7f, 0.2f);
            else if (Kind == EntityKind.EntityPet) c = new Color(0.7f, 0.4f, 0.95f);
            else c = new Color(0.3f, 0.55f, 1f);
            if (Selected) c = Color.Lerp(c, new Color(1f, 0.92f, 0.2f), 0.55f);
            _renderer.material.color = c;
        }

        private void RefreshNameplate()
        {
            if (_label != null)
                _label.text = $"{DisplayName}\n{Hp}/{MaxHp}";
            if (_barFg != null)
            {
                float r = Mathf.Clamp01((float)Hp / MaxHp);
                _barFg.localScale = new Vector3(r, 1f, 1f);
                _barFg.localPosition = new Vector3((r - 1f) * 0.5f, 0f, 0f);
                _barFg.GetComponent<Renderer>().material.color = r > 0.4f ? Color.green : (r > 0.15f ? Color.yellow : Color.red);
            }
            if (_barBg != null)
                _barBg.gameObject.SetActive(Kind != EntityKind.EntityLoot);
        }
    }
}
#endif
