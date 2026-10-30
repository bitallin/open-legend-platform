using Legend.Contracts;
using Legend.Contracts.Proto;

namespace Legend.Game;

public sealed partial class GameWorldModule
{
    private async Task ResolveSupportSkillAsync(
        IClientSession session,
        PacketFrame frame,
        WorldPlayerEntity attacker,
        GridAoiManager aoi,
        SkillConfigItem skill,
        int skillLevel,
        int cooldownMs,
        long now,
        CancellationToken ct)
    {
        string kind = (skill.Kind ?? "").Trim().ToLowerInvariant();
        attacker.Mp = Math.Max(0, attacker.Mp - skill.MpCost);
        long nextReady = now + cooldownMs;
        attacker.SkillReadyAtUnixMs[skill.SkillId] = nextReady;

        string message = "OK";
        var combat = new CombatSync
        {
            AttackerEntityId = attacker.EntityId,
            TargetEntityId = attacker.EntityId,
            SkillId = skill.SkillId,
            Result = CombatResultKind.CombatHeal,
            MapId = attacker.MapId,
            TargetMaxHp = attacker.MaxHp
        };

        switch (kind)
        {
            case "heal":
            {
                int heal = Math.Max(1, skill.HealAmount + skill.HealAmountPerLevel * (skillLevel - 1));
                int before = attacker.Hp;
                attacker.Hp = Math.Min(attacker.MaxHp, attacker.Hp + heal);
                combat.Heal = attacker.Hp - before;
                combat.TargetHp = attacker.Hp;
                message = combat.Heal > 0 ? $"恢复 {combat.Heal}" : "生命已满";
                break;
            }
            case "buff":
            {
                if (!string.IsNullOrWhiteSpace(skill.ApplyBuffOnCast))
                    await ApplyBuffAsync(attacker, skill.ApplyBuffOnCast, ct);
                if (string.Equals(skill.SkillId, "group_hide", StringComparison.OrdinalIgnoreCase))
                    await ShareBuffWithPartyAsync(attacker, skill.ApplyBuffOnCast, ct);
                combat.Result = CombatResultKind.CombatHit;
                combat.Damage = 0;
                combat.TargetHp = attacker.Hp;
                message = string.IsNullOrWhiteSpace(skill.DisplayName) ? "增益生效" : skill.DisplayName;
                break;
            }
            case "summon":
            {
                string petId = (skill.SummonPetId ?? "").Trim();
                var def = FindPet(petId);
                if (def is null)
                {
                    attacker.Mp += skill.MpCost;
                    attacker.SkillReadyAtUnixMs.Remove(skill.SkillId);
                    await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
                    {
                        Success = false,
                        ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                        Message = "没有对应的召唤兽",
                        RemainingMp = attacker.Mp
                    }, ct);
                    return;
                }
                attacker.OwnedPets.Add(def.PetId);
                if (!string.IsNullOrEmpty(attacker.ActivePetEntityId))
                    await DespawnPetEntityAsync(attacker, "replace", persist: false, ct);
                var pet = SpawnPetEntity(attacker, def);
                attacker.ActivePetId = def.PetId;
                attacker.ActivePetEntityId = pet.EntityId;
                await PersistPetsAsync(attacker, ct);
                await BroadcastNearAsync(pet.MapId, pet.X, pet.Y, MsgId.EntitySpawn,
                    new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "SkillSummon" }, ct);
                await session.SendAsync(MsgId.EntitySpawn, 0,
                    new EntitySpawn { Entity = pet.ToSnapshot(), Reason = "SkillSummon" }, ct);
                await BroadcastPetSyncAsync(attacker, pet, "summon", ct);
                combat.Result = CombatResultKind.CombatHit;
                combat.TargetHp = attacker.Hp;
                message = "召唤了 " + def.DisplayName;
                break;
            }
            case "teleport":
            {
                var home = _versionPack.Map;
                var portal = new PortalConfig
                {
                    PortalId = "skill_" + skill.SkillId,
                    TargetMapId = home.MapId,
                    TargetX = home.SpawnX,
                    TargetY = home.SpawnY
                };
                await TransferViaPortalAsync(session, attacker, aoi, portal, ct);
                combat.Result = CombatResultKind.CombatHit;
                combat.TargetHp = attacker.Hp;
                message = "回到了 " + home.MapName;
                break;
            }
            default:
                attacker.Mp += skill.MpCost;
                attacker.SkillReadyAtUnixMs.Remove(skill.SkillId);
                await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
                {
                    Success = false,
                    ErrorCode = ProtocolErrorCode.ErrInvalidSkill,
                    Message = "技能类型未实现",
                    RemainingMp = attacker.Mp
                }, ct);
                return;
        }

        await session.SendAsync(MsgId.AttackResp, frame.Seq, new AttackResp
        {
            Success = true,
            ErrorCode = ProtocolErrorCode.ErrNone,
            Message = message,
            RemainingMp = attacker.Mp,
            NextReadyUnixMs = nextReady
        }, ct);
        await BroadcastNearAsync(attacker.MapId, attacker.X, attacker.Y, MsgId.CombatSync, combat, ct);
        await session.SendAsync(MsgId.CombatSync, 0, combat, ct);
        await _storage.Characters.SavePositionAsync(
            attacker.CharacterId, attacker.MapId, attacker.X, attacker.Y, attacker.Hp, attacker.Mp, ct);
        await GrantSkillExpAsync(attacker, skill, skill.SkillExpPerCast, ct);
    }
}
