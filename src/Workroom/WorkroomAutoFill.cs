using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

internal static class WorkroomAutoFill
{
    internal static string Text(string key, params object[] args) => LanguageText.Get("workroom.autofill." + key, args);
    internal sealed class Placement
    {
        internal WorkroomStorageState.Record Record = null!;
        internal string Slot = "", Kind = "";
    }
    internal sealed class Plan
    {
        internal long Epoch, Revision;
        internal string Family = "";
        internal bool Machine, Complete, Limit;
        internal WorkroomAutoFillSolver.Options Options = new();
        internal List<Placement> Added = new();
        internal List<string> Messages = new();
        internal WorkroomAutoFillSolver.Choice? Score;
        internal bool CanFill => !Limit && Added.Count > 0 && Score != null;
    }
    internal static string Name(string family, bool machine, string kind) => machine ?
        WorkroomMachineAssembly.Text(kind == "body" ? family + "_body" : kind) :
        WorkroomComponentTemplates.All.First(t => t.Key == family).PartName(kind);
    internal static string[] Kinds(string family, bool machine) => machine ? WorkroomMachineAssembly.Kinds(family) :
        WorkroomComponentTemplates.All.First(t => t.Key == family).Kinds.Where(k => k != "body").ToArray();
    internal static int Maximum(string family, bool machine, string kind) => machine ? WorkroomMachineAssembly.MaxCount(family, kind) :
        WorkroomComponentTemplates.All.First(t => t.Key == family).MaxCount(kind);
    private static string Slot(string family, bool machine, string kind, int index = 0) => machine ?
        kind == "body" ? WorkroomMachineAssembly.MainSlot(family) : WorkroomMachineAssembly.Slot(family, kind, index) :
        WorkroomComponentAssembly.Slot(WorkroomComponentTemplates.All.First(t => t.Key == family), kind, index);
    private static bool Accepts(string slot, WorkroomItemCodec.Snapshot item, Func<string, WorkroomStorageState.Record?> find) =>
        WorkroomComponentAssembly.Accepts(slot, item, find);
    private static bool Bare(WorkroomItemCodec.Snapshot item) => item.Nodes.Count == 1 &&
        item.Nodes[0].Machine == null && item.Nodes[0].SlotItems.Count == 0 &&
        !WorkroomMachineAssembly.ActiveTag(item.Nodes[0], CaseUnboxing.LockedTagName);
    private static long BaseValue(WorkroomItemCodec.Snapshot item)
    {
        if (item.Nodes[0].Machine is { } machine)
            return checked(BaseValue(machine.Body) + machine.Parts.Sum(p => BaseValue(p.Item)));
        if (item.Nodes[0].Composition is { } parts) return parts.Sum(BaseValue);
        return item.Nodes[0].Scalars.TryGetValue("unitBaseValue", out var value) && value.TryGetInt64(out var number) && number >= 0 ? number : item.Value;
    }
    private static void Capacities(WorkroomItemCodec.Snapshot item, WorkroomAutoFillSolver.Item facts)
    {
        if (item.Nodes[0].Machine is { } machine)
        { foreach (var part in machine.Parts) Capacities(part.Item, facts); return; }
        var capacity = WorkroomComponentAssembly.Capacity(item);
        if (WorkroomComponentTemplates.TryPart(item.Identifier, out var family, out _, out _))
        {
            if (family.Key == "gpu") facts.Vram += capacity;
            if (family.Key == "ram") facts.Ram += capacity;
            if (family.Key == "ssd") facts.Disk += capacity;
            return;
        }
        var tag = Components.Find(item.Identifier)?.Owner.Tag;
        if (tag == Components.GpuTag) facts.Vram += capacity;
        if (tag == Components.RamTag) facts.Ram += capacity;
        if (tag == Components.HddTag) facts.Disk += capacity;
    }
    internal static WorkroomAutoFillSolver.Item Facts(WorkroomStorageState.Record record, string kind, bool caseBody = false)
    {
        var item = record.Item;
        var result = new WorkroomAutoFillSolver.Item { Id = record.Id, Kind = kind, Value = BaseValue(item), Box = record.Place == "box" ? 1 : 0 };
        if (item.Nodes[0].Machine is { } machine)
        {
            var parts = WorkroomMachineAssembly.Flatten(machine);
            result.Minimum = parts.Select(p => p.Tier).DefaultIfEmpty(0).Min();
            result.Maximum = parts.Select(p => p.Tier).DefaultIfEmpty(0).Max();
            result.RamTier = parts.Where(p => p.TypeTag == Components.RamTag).Select(p => p.Tier).FirstOrDefault();
        }
        else
        {
            result.Minimum = result.Maximum = caseBody ? 0 : WorkroomComponentAssembly.Configuration(item);
            if (Components.Find(item.Identifier)?.Owner.Tag == Components.RamTag) result.RamTier = result.Minimum;
        }
        // Capacitors contribute watts to the assembled PSU, rather than imposing
        // an individual tier floor on the power group. Whole PSUs keep their
        // configuration tier when selected for a machine.
        if (WorkroomComponentTemplates.TryPart(item.Identifier, out var partFamily, out var partKind, out var partTier) &&
            partFamily.Key == "psu" && partKind == "power")
        {
            result.Minimum = result.Maximum = 0;
            result.Watts = WorkroomComponentTemplates.PsuWatts(partTier);
        }
        Capacities(item, result);
        return result;
    }
    internal static Plan Preview(string family, bool machine, long epoch, WorkroomAutoFillSolver.Options options)
    {
        var plan = new Plan { Family = family, Machine = machine, Epoch = epoch,
            Revision = WorkroomStorage.State.Revision, Options = options.Copy() };
        if (!WorkroomComponentAssembly.Ready(epoch)) { plan.Messages.Add(Text("unavailable")); return plan; }
        try { return Select(plan); }
        catch (Exception ex)
        {
            Core.Log?.Error("[装配筛选] 预览失败，库存保持原状：" + ex);
            plan.Score = null; plan.Added.Clear(); plan.Complete = false;
            plan.Messages.Add(Text("unavailable")); return plan;
        }
    }
    private static Plan Select(Plan initial)
    {
        var family = initial.Family; var machine = initial.Machine; var o = initial.Options;
        var template = machine ? null : WorkroomComponentTemplates.All.First(t => t.Key == family);
        var all = WorkroomStorage.State.Items.ToArray();
        var fixedSlots = all.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
        var main = Slot(family, machine, "body");
        var sources = all.Where(r => (r.Place == "room" && o.Source != 2 || r.Place == "box" && o.Source != 1) &&
            r.Item.Count == 1 && r.Item.Nodes.Count == 1 && WorkroomComponentAssembly.Intact(r.Item)).ToArray();
        var related = (machine ? WorkroomMachineAssembly.Slots(family) : WorkroomComponentAssembly.Slots(template!)).ToHashSet(StringComparer.Ordinal);
        foreach (var r in fixedSlots.Values.Where(r => related.Contains(r.Slot)))
        {
            if (!WorkroomComponentAssembly.Intact(r.Item) || !WorkroomAutoFillSolver.Allowed(Facts(r, ""), o))
            { initial.Messages.Add(Text("fixed_conflict", r.Item.Name)); return initial; }
            if (machine && r.Item.Nodes[0].Machine is { } nested)
            {
                var parts = WorkroomMachineAssembly.Flatten(nested);
                if (parts.Where(p => p.TypeTag == Components.RamTag).Select(p => p.Tier).Distinct().Count() > 1 ||
                    parts.Count > 0 && parts.Max(p => p.Tier) - parts.Min(p => p.Tier) > 3)
                { initial.Messages.Add(Text("incompatible")); return initial; }
            }
        }
        var hasBody = fixedSlots.TryGetValue(main, out var fixedBody);
        var bodies = hasBody ? new[] { fixedBody! } : sources.Where(r => (!machine || Bare(r.Item)) &&
            Accepts(main, r.Item, s => fixedSlots.TryGetValue(s, out var v) ? v : null) && WorkroomAutoFillSolver.Allowed(Facts(r, "body"), o))
            .GroupBy(r => machine ? Components.Find(r.Item.Identifier)!.Tier : 0)
            .Select(g => g.OrderBy(r => BaseValue(r.Item)).ThenBy(r => r.Place == "room" ? 0 : 1).ThenBy(r => r.Id, StringComparer.Ordinal).First()).ToArray();
        if (bodies.Length == 0) { initial.Messages.Add(Text("missing", Name(family, machine, "body"), 1)); return initial; }
        Plan? best = null;
        foreach (var body in bodies)
        {
            if (machine && !Bare(body.Item)) { initial.Messages.Add(Text("body_used")); continue; }
            var bodyTier = machine ? Components.Find(body.Item.Identifier)!.Tier : 0;
            WorkroomStorageState.Record? Find(string s) => s == main ? body : fixedSlots.TryGetValue(s, out var r) ? r : null;
            var groups = new List<WorkroomAutoFillSolver.Group>();
            var bg = new WorkroomAutoFillSolver.Group { Kind = "body", Required = 1, Maximum = 1 };
            (hasBody ? bg.Fixed : bg.Available).Add(Facts(body, "body", machine && family == "case")); groups.Add(bg);
            var blocked = false;
            foreach (var kind in Kinds(family, machine))
            {
                var maximum = machine ? WorkroomMachineAssembly.Count(family, kind, bodyTier) : template!.MaxCount(kind);
                // Minimum necessary includes storage; only an explicit custom quantity may omit it.
                var required = machine && family == "motherboard" && kind == "ssd" && o.Quantity == 2 ? 0 : 1;
                var count = o.Quantity == 1 ? maximum : o.Quantity == 2 ? o.Counts.GetValueOrDefault(kind, required) : required;
                if (count < required || count > maximum)
                { initial.Messages.Add(Text("count_invalid", Name(family, machine, kind), required, maximum)); blocked = true; break; }
                var group = new WorkroomAutoFillSolver.Group { Kind = kind, Required = count, Maximum = o.Quantity == 2 ? count : maximum };
                for (var i = 0; i < maximum; i++) if (fixedSlots.TryGetValue(Slot(family, machine, kind, i), out var existing))
                    group.Fixed.Add(Facts(existing, kind));
                if (group.Fixed.Count > group.Maximum) { initial.Messages.Add(Text("fixed_count", Name(family, machine, kind))); blocked = true; break; }
                var probe = Enumerable.Range(0, maximum).Select(i => Slot(family, machine, kind, i)).FirstOrDefault(s => Find(s) == null);
                if (probe != null)
                    foreach (var r in sources.Where(r => r.Id != body.Id && Accepts(probe, r.Item, Find)))
                    {
                        if (machine && kind == "motherboard" && (r.Item.Nodes[0].Machine is not { } board ||
                            !WorkroomMachineAssembly.Assess(board).Complete || !WorkroomMachineAssembly.Flatten(board).Any(p => p.TypeTag == Components.HddTag))) continue;
                        group.Available.Add(Facts(r, kind));
                    }
                groups.Add(group);
            }
            if (blocked) continue;
            var solved = WorkroomAutoFillSolver.Solve(groups, o, machine, !machine && family == "ram", !machine && family == "psu");
            if (solved.Limit) { initial.Limit = true; initial.Messages.Clear(); initial.Messages.Add(Text("limit")); return initial; }
            if (solved.Choice == null) continue;
            var p = new Plan { Family = family, Machine = machine, Epoch = initial.Epoch, Revision = initial.Revision,
                Options = o.Copy(), Score = solved.Choice, Complete = solved.Complete };
            var lookup = all.ToDictionary(r => r.Id, StringComparer.Ordinal);
            foreach (var g in groups)
            {
                var occupied = new HashSet<string>(fixedSlots.Keys, StringComparer.Ordinal);
                foreach (var added in solved.Choice.Added.Where(a => a.Kind == g.Kind))
                {
                    var slot = g.Kind == "body" ? main : Enumerable.Range(0, machine ? WorkroomMachineAssembly.Count(family, g.Kind, bodyTier) : template!.MaxCount(g.Kind))
                        .Select(i => Slot(family, machine, g.Kind, i)).First(s => !occupied.Contains(s));
                    occupied.Add(slot); p.Added.Add(new Placement { Record = lookup[added.Id], Slot = slot, Kind = g.Kind });
                }
                var missing = g.Required - g.Fixed.Count - solved.Choice.Added.Count(a => a.Kind == g.Kind);
                if (missing > 0) p.Messages.Add(Text("missing", Name(family, machine, g.Kind), missing));
            }
            if (p.Score.Vram < o.Vram) p.Messages.Add(Text("capacity_missing", Text("vram"), p.Score.Vram, o.Vram));
            if (p.Score.Ram < o.Ram) p.Messages.Add(Text("capacity_missing", Text("ram"), p.Score.Ram, o.Ram));
            if (p.Score.Disk < o.Disk) p.Messages.Add(Text("capacity_missing", Text("disk"), p.Score.Disk, o.Disk));
            if (!p.Complete && p.Messages.Count == 0) p.Messages.Add(Text("tier_missing"));
            if (!ValidatePlan(p, out _)) continue;
            if (best == null || WorkroomAutoFillSolver.Better(p.Score, best.Score!, o, !machine && family == "ram", !machine && family == "psu")) best = p;
        }
        if (best != null) return best;
        if (initial.Messages.Count == 0) initial.Messages.Add(Text("incompatible"));
        return initial;
    }
    private static bool ValidatePlan(Plan p, out List<WorkroomStorageState.Record> next)
    {
        next = new List<WorkroomStorageState.Record>(WorkroomStorage.State.Items);
        var staged = next.Where(r => r.Place == "slot").ToDictionary(r => r.Slot, StringComparer.Ordinal);
        WorkroomStorageState.Record? Find(string s) => staged.TryGetValue(s, out var r) ? r : null;
        foreach (var placement in p.Added)
        {
            if (!next.Any(r => ReferenceEquals(r, placement.Record)) || !WorkroomComponentAssembly.Intact(placement.Record.Item) ||
                !Accepts(placement.Slot, placement.Record.Item, Find)) return false;
            WorkroomComponentAssembly.FillSlot(next, staged, placement.Record, placement.Slot);
        }
        if (p.Machine)
        {
            var body = Find(Slot(p.Family, true, "body")); if (body == null) return false;
            var info = new WorkroomMachineAssembly.MachineInfo { Version = 3, Kind = p.Family, Body = body.Item };
            foreach (var kind in Kinds(p.Family, true))
            for (var i = 0; i < WorkroomMachineAssembly.MaxCount(p.Family, kind); i++)
                if (Find(Slot(p.Family, true, kind, i)) is { } r) info.Parts.Add(new WorkroomMachineAssembly.Installed { Kind = kind, Index = i, Item = r.Item });
            var assessment = WorkroomMachineAssembly.AssessDraft(info);
            return p.Complete ? assessment.Complete : assessment.Status == WorkroomMachineAssembly.DetectionStatus.MissingParts || assessment.Complete;
        }
        if (p.Family == "psu")
        {
            var watts = WorkroomComponentAssembly.Slots(WorkroomComponentTemplates.All.First(t => t.Key == "psu"))
                .Select(Find).Where(r => r != null).Sum(r => Facts(r!, "").Watts);
            if (WorkroomComponentTemplates.PsuTier(watts) > p.Options.Maximum) return false;
        }
        if (p.Complete)
        {
            var parts = WorkroomComponentAssembly.Slots(WorkroomComponentTemplates.All.First(t => t.Key == p.Family)).Select(Find).Where(r => r != null).Select(r => r!.Item).ToList();
            WorkroomComponentParts.ValidateComposition(parts);
            var tier = WorkroomComponentParts.EffectiveTier(parts);
            return tier >= p.Options.Minimum && tier <= p.Options.Maximum;
        }
        return true;
    }
    internal static bool Apply(Plan p, bool partial)
    {
        if (!p.CanFill || !p.Complete && !partial || !WorkroomComponentAssembly.Ready(p.Epoch) ||
            WorkroomStorage.State.Revision != p.Revision) return false;
        var applied = false;
        WorkroomStorage.Run(() =>
        {
            if (!WorkroomStorage.State.Ready || WorkroomStorage.State.Epoch != p.Epoch || WorkroomItemCodec.CleanupPending ||
                WorkroomStorage.State.Revision != p.Revision || !ValidatePlan(p, out var next)) return;
            WorkroomStorage.State.Replace(next); applied = true;
            WorkroomStorage.NotifyText(Text("applied", p.Added.Count));
        });
        return applied;
    }
}
