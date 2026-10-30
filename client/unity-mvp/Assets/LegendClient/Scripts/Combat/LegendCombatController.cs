#if UNITY_2019_1_OR_NEWER
using Legend.Client.Audio;
using Legend.Client.Config;
using Legend.Client.Session;
using Legend.Client.UI;
using Legend.Client.World;
using Legend.ClientCore;
using Legend.Contracts.Proto;
using UnityEngine;

namespace Legend.Client.Combat
{
    /// <summary>Click-to-select + attack (Space / RMB / Attack button) with damage popups + SFX.</summary>
    public sealed class LegendCombatController : MonoBehaviour
    {
        private LegendSessionService _session = null!;
        private LegendWorldPresenter _world = null!;
        private LegendClientConfig _config = null!;
        private LegendHudController? _hud;
        private LegendAudioService? _audio;

        public void Bind(LegendSessionService session, LegendWorldPresenter world, LegendClientConfig config)
        {
            _session = session;
            _world = world;
            _config = config;
        }

        public void BindHud(LegendHudController hud) => _hud = hud;
        public void BindAudio(LegendAudioService audio) => _audio = audio;

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
            {
                _audio?.PlayPickup();
                _ = _session.PickupAsync(view.EntityId);
            }
        }

        public void TryAttackSelectedOrNearest()
        {
            if (_session.Fsm.Phase != SessionPhase.InWorld) return;
            var self = _session.Client?.SelfEntity;
            if (self != null && self.InSafeZone)
            {
                _hud?.ShowToast("安全区内无法攻击", new Color(0.15f, 0.4f, 0.3f, 0.92f));
                return;
            }
            var target = _world.SelectedEntityId;
            if (string.IsNullOrEmpty(target) || _world.Get(target) == null ||
                _world.Get(target)!.Kind != EntityKind.EntityMonster)
            {
                target = _world.FindNearestAttackable();
                if (!string.IsNullOrEmpty(target))
                    _world.Select(target);
            }
            if (string.IsNullOrEmpty(target))
            {
                _hud?.ShowToast("附近没有可攻击目标", new Color(0.35f, 0.2f, 0.15f, 0.9f));
                return;
            }
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
            {
                LegendDamagePopup.Spawn(pos, "-" + cs.Damage, cs.TargetDied ? Color.magenta : new Color(1f, 0.92f, 0.2f));
                _audio?.PlayHit();
            }

            if (cs.TargetDied && view != null && view.Kind == EntityKind.EntityMonster)
            {
                _session.ChatLog.Add($"[combat] Killed {view.DisplayName}");
                _hud?.ShowToast($"击杀 {view.DisplayName}", new Color(0.45f, 0.15f, 0.15f, 0.9f), 1.6f);
            }

            var selfId = _session.Client?.SelfEntity?.EntityId;
            if (!string.IsNullOrEmpty(selfId) && cs.TargetEntityId == selfId && cs.TargetDied)
                _hud?.ShowToast("你已死亡（服务器将复活到出生点）", new Color(0.45f, 0.08f, 0.08f, 0.95f), 3.5f);
        }
    }
}
#endif
