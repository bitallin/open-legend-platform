#if UNITY_2019_1_OR_NEWER
using Legend.Client.Config;
using Legend.Client.Session;
using Legend.Client.World;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.Combat
{
    /// <summary>Click-to-select + attack (Space / RMB / Attack button).</summary>
    public sealed class LegendCombatController : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendWorldPresenter _world = null!;
        private LegendClientConfig _config = null!;

        public void Bind(LegendSessionService session, LegendWorldPresenter world, LegendClientConfig config)
        {
            _session = session;
            _world = world;
            _config = config;
        }

        public void WireNetwork()
        {
            var c = _session.Client;
            if (c == null) return;
            c.OnCombatSync += OnCombatSync;
            c.OnLootDrop += loot =>
            {
                var e = loot.Loot;
                if (e != null)
                    _session.ChatLog.Add($"[loot] {e.Name} @ ({e.X},{e.Y})");
            };
        }

        public void TrySelectFromScreen(Vector3 screenPos)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 200f)) return;
            var view = hit.collider.GetComponentInParent<LegendEntityView>();
            if (view == null || view.IsSelf) return;
            _world.Select(view.EntityId);
            if (view.Kind == EntityKind.EntityLoot)
                _ = _session.PickupAsync(view.EntityId);
        }

        public void TryAttackSelectedOrNearest()
        {
            if (_session.Fsm.Phase != SessionPhase.InWorld) return;
            var target = _world.SelectedEntityId;
            if (string.IsNullOrEmpty(target) || _world.Get(target) == null ||
                _world.Get(target)!.Kind != EntityKind.EntityMonster)
            {
                target = _world.FindNearestAttackable();
                if (!string.IsNullOrEmpty(target))
                    _world.Select(target);
            }
            if (string.IsNullOrEmpty(target)) return;
            _ = _session.AttackAsync(target, _config.BasicAttackSkillId);
        }

        private void OnCombatSync(CombatSync cs)
        {
            var view = _world.Get(cs.TargetEntityId);
            Vector3 pos = view != null
                ? view.transform.position
                : Vector3.zero;
            if (cs.Result == CombatResultKind.CombatMiss)
                LegendDamagePopup.Spawn(pos, "MISS", Color.gray);
            else if (cs.Result == CombatResultKind.CombatHeal)
                LegendDamagePopup.Spawn(pos, "+" + cs.Heal, Color.cyan);
            else
                LegendDamagePopup.Spawn(pos, "-" + cs.Damage, cs.TargetDied ? Color.magenta : Color.yellow);

            if (cs.TargetDied && view != null && view.Kind == EntityKind.EntityMonster)
                _session.ChatLog.Add($"[combat] Killed {view.DisplayName}");
        }
    }
}
#endif
