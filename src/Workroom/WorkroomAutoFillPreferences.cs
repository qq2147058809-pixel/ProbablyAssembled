using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MelonLoader;

namespace PCExpansion;

// Local UI preferences only; not part of player saves. Written only on explicit preview.
internal static class WorkroomAutoFillPreferences
{
    private static readonly Dictionary<string, WorkroomAutoFillSolver.Options> values = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions json = new() { IncludeFields = true };
    private static MelonPreferences_Category? category;
    private static MelonPreferences_Entry<string>? entry;
    private static bool loaded;
    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        try
        {
            category = MelonPreferences.CreateCategory("PCExpansionAutoFill", WorkroomAutoFill.Text("title"));
            entry = category.CreateEntry("Selections", "{}", WorkroomAutoFill.Text("title"));
            if (entry.Value.Length > 65536) return;
            var saved = JsonSerializer.Deserialize<Dictionary<string, WorkroomAutoFillSolver.Options>>(entry.Value, json);
            if (saved == null) return;
            foreach (var pair in saved)
            {
                var machine = pair.Key.StartsWith("machine.", StringComparison.Ordinal);
                var family = pair.Key.Substring(pair.Key.IndexOf('.') + 1);
                if (machine ? family != "motherboard" && family != "case" :
                    !pair.Key.StartsWith("component.", StringComparison.Ordinal) || !WorkroomComponentTemplates.All.Any(t => t.Key == family)) continue;
                var o = pair.Value;
                if (o == null || o.Minimum < 1 || o.Maximum > 5 || o.Minimum > o.Maximum || o.Source is < 0 or > 2 ||
                    o.Quantity is < 0 or > 2 || o.Vram is < 0 or > 96 || o.Ram is < 0 or > 384 || o.Disk is < 0 or > 8192 ||
                    o.Counts == null || o.Counts.Count > 5 || o.Counts.Any(c =>
                        !WorkroomAutoFill.Kinds(family, machine).Contains(c.Key) || c.Value < 0 || c.Value > WorkroomAutoFill.Maximum(family, machine, c.Key))) continue;
                values[pair.Key] = o.Copy();
            }
        }
        catch (Exception ex) { Core.Log?.Warning("[装配筛选] 偏好读取失败，使用默认设置：" + ex.Message); }
    }
    internal static WorkroomAutoFillSolver.Options Get(string key)
    { Load(); return values.TryGetValue(key, out var o) ? o.Copy() : new(); }
    internal static void Remember(string key, WorkroomAutoFillSolver.Options options, bool persist)
    {
        Load(); values[key] = options.Copy();
        if (!persist) return;
        try
        {
            if (entry == null) return;
            entry.Value = JsonSerializer.Serialize(values, json); category!.SaveToFile(false);
        }
        catch (Exception ex) { Core.Log?.Warning("[装配筛选] 偏好暂仅保留在内存：" + ex.Message); }
    }
}
