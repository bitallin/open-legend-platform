using System.Text.Json;
using Legend.Storage;
using Microsoft.Data.Sqlite;

namespace Legend.Merge;

public sealed class MergeReport
{
    public bool Ok { get; set; }
    public bool DryRun { get; set; }
    public string Target { get; set; } = "";
    public int AccountsFromA { get; set; }
    public int AccountsFromB { get; set; }
    public int AccountsMerged { get; set; }
    public int CharactersFromA { get; set; }
    public int CharactersFromB { get; set; }
    public int CharactersWritten { get; set; }
    public int CharactersRenamed { get; set; }
    public int GuildsFromA { get; set; }
    public int GuildsFromB { get; set; }
    public int GuildsWritten { get; set; }
    public int GuildsRenamed { get; set; }
    public int MailsWritten { get; set; }
    public List<string> Notes { get; set; } = new();
}

public static class RealmMerger
{
    public static async Task<MergeReport> MergeAsync(string sourceA, string sourceB, string target, bool dryRun, CancellationToken ct = default)
    {
        var report = new MergeReport { DryRun = dryRun, Target = target };
        if (!File.Exists(sourceA) || !File.Exists(sourceB))
        {
            report.Notes.Add("source db missing");
            return report;
        }

        // Initialize empty target schema via storage
        if (!dryRun)
        {
            try { File.Delete(target); } catch { /* ignore */ }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            await using var storage = await StorageFactory.CreateAsync(new StorageOptions
            {
                Backend = "Sqlite",
                SqlitePath = target
            }, ct);
        }

        var aAccounts = await LoadAccounts(sourceA, ct);
        var bAccounts = await LoadAccounts(sourceB, ct);
        var aChars = await LoadCharacters(sourceA, ct);
        var bChars = await LoadCharacters(sourceB, ct);
        var aGuilds = await LoadGuilds(sourceA, ct);
        var bGuilds = await LoadGuilds(sourceB, ct);
        var aMails = await LoadMails(sourceA, ct);
        var bMails = await LoadMails(sourceB, ct);
        var aMod = await LoadModeration(sourceA, ct);
        var bMod = await LoadModeration(sourceB, ct);

        report.AccountsFromA = aAccounts.Count;
        report.AccountsFromB = bAccounts.Count;
        report.CharactersFromA = aChars.Count;
        report.CharactersFromB = bChars.Count;
        report.GuildsFromA = aGuilds.Count;
        report.GuildsFromB = bGuilds.Count;

        // Build merge plan: start with A as base, merge B in
        var accountIdMap = new Dictionary<string, string>(StringComparer.Ordinal); // old->new
        var usernameToAccount = new Dictionary<string, AccountRow>(StringComparer.OrdinalIgnoreCase);
        var outAccounts = new List<AccountRow>();

        foreach (var acc in aAccounts)
        {
            outAccounts.Add(acc);
            usernameToAccount[acc.Username] = acc;
            accountIdMap[acc.AccountId] = acc.AccountId;
        }
        foreach (var acc in bAccounts)
        {
            if (usernameToAccount.TryGetValue(acc.Username, out var existing))
            {
                accountIdMap[acc.AccountId] = existing.AccountId;
                report.Notes.Add($"account merge: {acc.Username} ({acc.AccountId} -> {existing.AccountId})");
            }
            else
            {
                outAccounts.Add(acc);
                usernameToAccount[acc.Username] = acc;
                accountIdMap[acc.AccountId] = acc.AccountId;
            }
        }
        report.AccountsMerged = outAccounts.Count;

        var charIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outChars = new List<CharacterRow>();

        void AddChar(CharacterRow ch, bool fromB)
        {
            string newAccountId = accountIdMap.TryGetValue(ch.AccountId, out var mapped) ? mapped : ch.AccountId;
            string newCharId = ch.CharacterId;
            if (outChars.Any(c => c.CharacterId == newCharId))
            {
                newCharId = Guid.NewGuid().ToString("N");
                report.Notes.Add($"character id remap: {ch.CharacterId} -> {newCharId}");
            }
            charIdMap[ch.CharacterId] = newCharId;

            string name = ch.Name;
            if (!usedNames.Add(name))
            {
                string baseName = name + "_m";
                name = baseName;
                int n = 2;
                while (!usedNames.Add(name))
                {
                    name = baseName + n;
                    n++;
                }
                report.CharactersRenamed++;
                report.Notes.Add($"character rename: {ch.Name} -> {name}");
            }

            outChars.Add(ch with { CharacterId = newCharId, AccountId = newAccountId, Name = name });
        }

        foreach (var ch in aChars) AddChar(ch, false);
        foreach (var ch in bChars) AddChar(ch, true);
        report.CharactersWritten = outChars.Count;

        // Guilds: A first, then B with rename
        var usedGuildNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var guildIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var outGuilds = new List<GuildRow>();

        void AddGuild(GuildRow g)
        {
            string newGuildId = g.GuildId;
            if (outGuilds.Any(x => x.GuildId == newGuildId))
            {
                newGuildId = Guid.NewGuid().ToString("N");
            }
            guildIdMap[g.GuildId] = newGuildId;

            string name = g.Name;
            if (!usedGuildNames.Add(name))
            {
                string baseName = name + "_m";
                name = baseName;
                int n = 2;
                while (!usedGuildNames.Add(name))
                {
                    name = baseName + n;
                    n++;
                }
                report.GuildsRenamed++;
                report.Notes.Add($"guild rename: {g.Name} -> {name}");
            }

            string leader = charIdMap.TryGetValue(g.LeaderCharacterId, out var nl) ? nl : g.LeaderCharacterId;
            var members = g.Members.Select(m => charIdMap.TryGetValue(m, out var nm) ? nm : m).Distinct().ToList();
            // Remap ranks_json keys
            string ranks = RemapJsonObjectKeys(g.RanksJson, charIdMap);

            outGuilds.Add(g with
            {
                GuildId = newGuildId,
                Name = name,
                LeaderCharacterId = leader,
                Members = members,
                RanksJson = ranks
            });
        }

        foreach (var g in aGuilds) AddGuild(g);
        foreach (var g in bGuilds) AddGuild(g);
        report.GuildsWritten = outGuilds.Count;

        // Fix character guild_id
        outChars = outChars.Select(c =>
        {
            if (string.IsNullOrWhiteSpace(c.GuildId)) return c;
            if (guildIdMap.TryGetValue(c.GuildId, out var ng))
                return c with { GuildId = ng };
            return c with { GuildId = "" };
        }).ToList();

        // Mails from both, remap owner + new mail ids if needed
        var outMails = new List<MailRow>();
        foreach (var m in aMails.Concat(bMails))
        {
            if (!charIdMap.TryGetValue(m.OwnerCharacterId, out var owner))
                continue;
            string mailId = m.MailId;
            if (outMails.Any(x => x.MailId == mailId))
                mailId = Guid.NewGuid().ToString("N");
            outMails.Add(m with { MailId = mailId, OwnerCharacterId = owner });
        }
        report.MailsWritten = outMails.Count;

        // Moderation: prefer banned/muted flags OR-ed
        var outMod = new Dictionary<string, ModRow>(StringComparer.Ordinal);
        foreach (var m in aMod.Concat(bMod))
        {
            string acc = accountIdMap.TryGetValue(m.AccountId, out var na) ? na : m.AccountId;
            if (outMod.TryGetValue(acc, out var exist))
            {
                outMod[acc] = exist with
                {
                    IsBanned = exist.IsBanned || m.IsBanned,
                    BanReason = exist.IsBanned ? exist.BanReason : m.BanReason,
                    BanExpiresUnixMs = MaxExpire(exist.BanExpiresUnixMs, m.BanExpiresUnixMs, exist.IsBanned || m.IsBanned),
                    IsMuted = exist.IsMuted || m.IsMuted,
                    MuteReason = exist.IsMuted ? exist.MuteReason : m.MuteReason,
                    MuteExpiresUnixMs = MaxExpire(exist.MuteExpiresUnixMs, m.MuteExpiresUnixMs, exist.IsMuted || m.IsMuted)
                };
            }
            else outMod[acc] = m with { AccountId = acc };
        }

        report.Notes.Add($"plan accounts={outAccounts.Count} chars={outChars.Count} guilds={outGuilds.Count} mails={outMails.Count} moderation={outMod.Count}");

        if (dryRun)
        {
            report.Ok = true;
            return report;
        }

        await using (var conn = Open(target))
        {
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            foreach (var a in outAccounts)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR REPLACE INTO accounts(account_id,username,password_hash,created_at) VALUES($id,$u,$h,$c)";
                cmd.Parameters.AddWithValue("$id", a.AccountId);
                cmd.Parameters.AddWithValue("$u", a.Username);
                cmd.Parameters.AddWithValue("$h", a.PasswordHash);
                cmd.Parameters.AddWithValue("$c", a.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var c in outChars)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO characters(character_id,account_id,name,class_id,level,exp,map_id,x,y,hp,mp,max_hp,max_mp,inventory_json,gold,quest_json,equipment_json,guild_id,skills_json,dungeon_progress_json,appearance_json,created_at,updated_at)
                    VALUES($cid,$aid,$n,$cl,$lv,$xp,$m,$x,$y,$hp,$mp,$mhp,$mmp,$inv,$g,$qj,$eq,$gid,$sk,$dp,$ap,$ca,$ua)
                    """;
                cmd.Parameters.AddWithValue("$cid", c.CharacterId);
                cmd.Parameters.AddWithValue("$aid", c.AccountId);
                cmd.Parameters.AddWithValue("$n", c.Name);
                cmd.Parameters.AddWithValue("$cl", c.ClassId);
                cmd.Parameters.AddWithValue("$lv", c.Level);
                cmd.Parameters.AddWithValue("$xp", c.Exp);
                cmd.Parameters.AddWithValue("$m", c.MapId);
                cmd.Parameters.AddWithValue("$x", c.X);
                cmd.Parameters.AddWithValue("$y", c.Y);
                cmd.Parameters.AddWithValue("$hp", c.Hp);
                cmd.Parameters.AddWithValue("$mp", c.Mp);
                cmd.Parameters.AddWithValue("$mhp", c.MaxHp);
                cmd.Parameters.AddWithValue("$mmp", c.MaxMp);
                cmd.Parameters.AddWithValue("$inv", c.InventoryJson);
                cmd.Parameters.AddWithValue("$g", c.Gold);
                cmd.Parameters.AddWithValue("$qj", c.QuestJson);
                cmd.Parameters.AddWithValue("$eq", c.EquipmentJson);
                cmd.Parameters.AddWithValue("$gid", c.GuildId ?? "");
                cmd.Parameters.AddWithValue("$sk", c.SkillsJson);
                cmd.Parameters.AddWithValue("$dp", c.DungeonProgressJson);
                cmd.Parameters.AddWithValue("$ap", c.AppearanceJson);
                cmd.Parameters.AddWithValue("$ca", c.CreatedAt);
                cmd.Parameters.AddWithValue("$ua", c.UpdatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var g in outGuilds)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO guilds(guild_id,name,leader_character_id,members_json,warehouse_json,ranks_json,created_at)
                    VALUES($id,$n,$l,$m,$w,$r,$c)
                    """;
                cmd.Parameters.AddWithValue("$id", g.GuildId);
                cmd.Parameters.AddWithValue("$n", g.Name);
                cmd.Parameters.AddWithValue("$l", g.LeaderCharacterId);
                cmd.Parameters.AddWithValue("$m", JsonSerializer.Serialize(g.Members));
                cmd.Parameters.AddWithValue("$w", g.WarehouseJson);
                cmd.Parameters.AddWithValue("$r", g.RanksJson);
                cmd.Parameters.AddWithValue("$c", g.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var m in outMails)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO mails(mail_id,owner_character_id,from_name,subject,body,attach_gold,attach_items_json,is_read,claimed,created_at)
                    VALUES($id,$o,$f,$s,$b,$g,$i,$r,$c,$ca)
                    """;
                cmd.Parameters.AddWithValue("$id", m.MailId);
                cmd.Parameters.AddWithValue("$o", m.OwnerCharacterId);
                cmd.Parameters.AddWithValue("$f", m.FromName);
                cmd.Parameters.AddWithValue("$s", m.Subject);
                cmd.Parameters.AddWithValue("$b", m.Body);
                cmd.Parameters.AddWithValue("$g", m.AttachGold);
                cmd.Parameters.AddWithValue("$i", m.AttachItemsJson);
                cmd.Parameters.AddWithValue("$r", m.IsRead ? 1 : 0);
                cmd.Parameters.AddWithValue("$c", m.Claimed ? 1 : 0);
                cmd.Parameters.AddWithValue("$ca", m.CreatedAt);
                await cmd.ExecuteNonQueryAsync(ct);
            }

            foreach (var m in outMod.Values)
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO account_moderation(account_id,is_banned,ban_reason,ban_expires_unix_ms,is_muted,mute_reason,mute_expires_unix_ms,updated_at)
                    VALUES($id,$ib,$br,$be,$im,$mr,$me,$u)
                    """;
                cmd.Parameters.AddWithValue("$id", m.AccountId);
                cmd.Parameters.AddWithValue("$ib", m.IsBanned ? 1 : 0);
                cmd.Parameters.AddWithValue("$br", m.BanReason);
                cmd.Parameters.AddWithValue("$be", m.BanExpiresUnixMs);
                cmd.Parameters.AddWithValue("$im", m.IsMuted ? 1 : 0);
                cmd.Parameters.AddWithValue("$mr", m.MuteReason);
                cmd.Parameters.AddWithValue("$me", m.MuteExpiresUnixMs);
                cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }

        report.Ok = true;
        return report;
    }

    private static long MaxExpire(long a, long b, bool active)
    {
        if (!active) return 0;
        if (a <= 0 || b <= 0) return 0; // permanent wins
        return Math.Max(a, b);
    }

    private static string RemapJsonObjectKeys(string json, Dictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return "{}";
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return json;
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                string key = map.TryGetValue(prop.Name, out var nk) ? nk : prop.Name;
                dict[key] = prop.Value.Clone();
            }
            return JsonSerializer.Serialize(dict);
        }
        catch { return json; }
    }

    private static SqliteConnection Open(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString());

    private sealed record AccountRow(string AccountId, string Username, string PasswordHash, string CreatedAt);
    private sealed record CharacterRow(
        string CharacterId, string AccountId, string Name, string ClassId, int Level, int Exp,
        string MapId, int X, int Y, int Hp, int Mp, int MaxHp, int MaxMp, string InventoryJson, int Gold,
        string QuestJson, string EquipmentJson, string GuildId, string SkillsJson, string DungeonProgressJson,
        string AppearanceJson, string CreatedAt, string UpdatedAt);
    private sealed record GuildRow(string GuildId, string Name, string LeaderCharacterId, List<string> Members, string WarehouseJson, string RanksJson, string CreatedAt);
    private sealed record MailRow(string MailId, string OwnerCharacterId, string FromName, string Subject, string Body, int AttachGold, string AttachItemsJson, bool IsRead, bool Claimed, string CreatedAt);
    private sealed record ModRow(string AccountId, bool IsBanned, string BanReason, long BanExpiresUnixMs, bool IsMuted, string MuteReason, long MuteExpiresUnixMs);

    private static async Task<List<AccountRow>> LoadAccounts(string db, CancellationToken ct)
    {
        var list = new List<AccountRow>();
        await using var conn = Open(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account_id,username,password_hash,created_at FROM accounts";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new AccountRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    private static async Task<List<CharacterRow>> LoadCharacters(string db, CancellationToken ct)
    {
        var list = new List<CharacterRow>();
        await using var conn = Open(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT character_id,account_id,name,class_id,level,exp,map_id,x,y,hp,mp,max_hp,max_mp,inventory_json,gold,quest_json,equipment_json,guild_id,skills_json,dungeon_progress_json,appearance_json,created_at,updated_at FROM characters";
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new CharacterRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetInt32(4), r.GetInt32(5), r.GetString(6), r.GetInt32(7), r.GetInt32(8),
                    r.GetInt32(9), r.GetInt32(10), r.GetInt32(11), r.GetInt32(12),
                    r.GetString(13), r.GetInt32(14), r.GetString(15), r.GetString(16),
                    r.IsDBNull(17) ? "" : r.GetString(17),
                    r.IsDBNull(18) ? "[]" : r.GetString(18),
                    r.IsDBNull(19) ? "{}" : r.GetString(19),
                    r.IsDBNull(20) ? "{}" : r.GetString(20),
                    r.GetString(21), r.GetString(22)));
            }
        }
        catch (SqliteException)
        {
            // older schema fallback ignored — empty
        }
        return list;
    }

    private static async Task<List<GuildRow>> LoadGuilds(string db, CancellationToken ct)
    {
        var list = new List<GuildRow>();
        await using var conn = Open(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT guild_id,name,leader_character_id,members_json,warehouse_json,ranks_json,created_at FROM guilds";
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var members = JsonSerializer.Deserialize<List<string>>(r.GetString(3)) ?? new();
                list.Add(new GuildRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), members,
                    r.IsDBNull(4) ? "[]" : r.GetString(4),
                    r.IsDBNull(5) ? "{}" : r.GetString(5),
                    r.GetString(6)));
            }
        }
        catch (SqliteException) { }
        return list;
    }

    private static async Task<List<MailRow>> LoadMails(string db, CancellationToken ct)
    {
        var list = new List<MailRow>();
        await using var conn = Open(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT mail_id,owner_character_id,from_name,subject,body,attach_gold,attach_items_json,is_read,claimed,created_at FROM mails";
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new MailRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
                    r.GetInt32(5), r.GetString(6), r.GetInt64(7) != 0, r.GetInt64(8) != 0, r.GetString(9)));
            }
        }
        catch (SqliteException) { }
        return list;
    }

    private static async Task<List<ModRow>> LoadModeration(string db, CancellationToken ct)
    {
        var list = new List<ModRow>();
        await using var conn = Open(db);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT account_id,is_banned,ban_reason,ban_expires_unix_ms,is_muted,mute_reason,mute_expires_unix_ms FROM account_moderation";
        try
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new ModRow(
                    r.GetString(0),
                    r.GetInt64(1) != 0,
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? 0 : r.GetInt64(3),
                    r.GetInt64(4) != 0,
                    r.IsDBNull(5) ? "" : r.GetString(5),
                    r.IsDBNull(6) ? 0 : r.GetInt64(6)));
            }
        }
        catch (SqliteException) { }
        return list;
    }
}
