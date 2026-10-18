using System;
using System.Collections.Generic;
using System.Linq;

namespace PCExpansion;

// Pure bounded selection: no Unity objects, inventory mutation or frame callbacks.
internal static class WorkroomAutoFillSolver
{
    internal sealed class Options
    {
        public Options() { }
        public int Minimum = 1, Maximum = 3, Source, Quantity;
        public bool Performance;
        public long Vram, Ram, Disk;
        public Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        internal Options Copy() => new() { Minimum = Minimum, Maximum = Maximum, Source = Source,
            Quantity = Quantity, Performance = Performance, Vram = Vram, Ram = Ram, Disk = Disk,
            Counts = new Dictionary<string, int>(Counts, StringComparer.Ordinal) };
    }
    internal sealed class Item
    {
        internal string Id = "", Kind = "";
        internal int Minimum, Maximum, RamTier, Box;
        internal long Vram, Ram, Disk, Watts, Value;
    }
    internal sealed class Group
    {
        internal string Kind = "";
        internal int Required, Maximum;
        internal List<Item> Fixed = new(), Available = new();
    }
    internal sealed class Choice
    {
        internal List<Item> Added = new();
        internal int Minimum = 6, Maximum, RamTier, Boxes, Count, Missing;
        internal long Vram, Ram, Disk, Watts;
        internal decimal Value;
        internal string Tie => string.Join("|", Added.Select(i => i.Id).OrderBy(s => s, StringComparer.Ordinal));
    }
    internal sealed class Result
    {
        internal Choice? Choice;
        internal bool Complete, Limit;
    }
    private readonly record struct Key(int Minimum, int Maximum, int RamTier, long Vram, long Ram, long Disk, int Missing, int Count, long Watts);
    private static Key State(Choice c, Options o, bool ramComponent, bool psuComponent, bool vramDone, bool ramDone, bool diskDone) =>
        new(ramComponent || psuComponent ? Tier(c, ramComponent, psuComponent) : c.Minimum, c.Maximum, c.RamTier,
            !o.Performance || vramDone ? Math.Min(c.Vram, o.Vram) : c.Vram,
            (!ramComponent && !o.Performance) || ramDone ? Math.Min(c.Ram, o.Ram) : c.Ram,
            !o.Performance || diskDone ? Math.Min(c.Disk, o.Disk) : c.Disk, c.Missing, c.Count, c.Watts);
    private sealed class Budget
    {
        internal int Remaining = 180000;
        internal void Step() { if (--Remaining < 0) throw new SearchLimit(); }
    }
    private sealed class SearchLimit : Exception { }
    internal static bool Allowed(Item item, Options o) => item.Minimum == 0 || item.Minimum >= o.Minimum && item.Maximum <= o.Maximum;
    private static Choice? Join(Choice a, Choice b, bool machine)
    {
        var min = Math.Min(a.Minimum, b.Minimum); var max = Math.Max(a.Maximum, b.Maximum);
        if (machine && (max > 0 && max - min > 3 || a.RamTier != 0 && b.RamTier != 0 && a.RamTier != b.RamTier)) return null;
        return new Choice { Added = a.Added.Concat(b.Added).ToList(), Minimum = min, Maximum = max,
            RamTier = a.RamTier != 0 ? a.RamTier : b.RamTier, Boxes = a.Boxes + b.Boxes,
            Vram = checked(a.Vram + b.Vram), Ram = checked(a.Ram + b.Ram), Disk = checked(a.Disk + b.Disk), Watts = checked(a.Watts + b.Watts),
            Value = a.Value + b.Value, Count = a.Count + b.Count, Missing = a.Missing + b.Missing };
    }
    private static Choice Unit(Item i, bool added) => new() { Added = added ? new() { i } : new(),
        Minimum = i.Minimum == 0 ? 6 : i.Minimum, Maximum = i.Maximum, RamTier = i.RamTier,
        Vram = i.Vram, Ram = i.Ram, Disk = i.Disk, Watts = i.Watts, Value = added ? i.Value : 0,
        Count = added ? 1 : 0, Boxes = added ? i.Box : 0 };
    private static bool Cheaper(Choice a, Choice b)
    {
        var cmp = a.Value.CompareTo(b.Value);
        if (cmp != 0) return cmp < 0;
        if (a.Boxes != b.Boxes) return a.Boxes < b.Boxes;
        return StringComparer.Ordinal.Compare(a.Tie, b.Tie) < 0;
    }
    private static void Keep(Dictionary<Key, Choice> table, Choice c, Options o, bool ramComponent, bool psuComponent,
        bool vramDone = false, bool ramDone = false, bool diskDone = false)
    {
        var key = State(c, o, ramComponent, psuComponent, vramDone, ramDone, diskDone);
        if (!table.TryGetValue(key, out var old) || Better(c, old, o, ramComponent, psuComponent)) table[key] = c;
    }
    private static List<Choice> SelectGroup(Group group, Options o, bool machine, bool ramComponent, bool psuComponent, Budget budget)
    {
        Choice? start = new();
        foreach (var item in group.Fixed)
        {
            if (!Allowed(item, o)) return new();
            start = Join(start!, Unit(item, false), machine);
            if (start == null) return new();
        }
        var free = group.Maximum - group.Fixed.Count;
        if (free < 0) return new();
        var levels = Enumerable.Range(0, free + 1).Select(_ => new Dictionary<Key, Choice>()).ToArray();
        Keep(levels[0], start!, o, ramComponent, psuComponent);
        // Only equivalent facts can be collapsed; retain enough real identities for all slots.
        var items = group.Available.Where(i => Allowed(i, o)).GroupBy(i =>
            (i.Minimum, i.Maximum, i.RamTier, i.Vram, i.Ram, i.Disk, i.Watts))
            .SelectMany(g => g.OrderBy(i => i.Value).ThenBy(i => i.Box).ThenBy(i => i.Id, StringComparer.Ordinal).Take(free))
            .OrderBy(i => i.Id, StringComparer.Ordinal);
        foreach (var item in items)
        for (var count = free - 1; count >= 0; count--)
        foreach (var previous in levels[count].Values)
        {
            budget.Step();
            var candidate = Join(previous, Unit(item, true), machine);
            if (candidate != null) Keep(levels[count + 1], candidate, o, ramComponent, psuComponent);
        }
        var result = new List<Choice>();
        for (var count = 0; count <= free; count++)
        foreach (var choice in levels[count].Values)
        {
            if (group.Kind == "body" && group.Fixed.Count + count < group.Required) continue;
            choice.Missing = Math.Max(0, group.Required - group.Fixed.Count - count);
            result.Add(choice);
        }
        return result;
    }
    private static int Tier(Choice c, bool ramComponent, bool psuComponent = false) => psuComponent ? WorkroomComponentTemplates.PsuTier(c.Watts) : ramComponent ?
        c.Ram < 16 ? 1 : c.Ram < 32 ? 2 : c.Ram < 48 ? 3 : c.Ram < 64 ? 4 : 5 : c.Minimum == 6 ? 0 : c.Minimum;
    internal static bool Complete(Choice c, Options o, bool ramComponent, bool psuComponent = false) => c.Missing == 0 &&
        c.Vram >= o.Vram && c.Ram >= o.Ram && c.Disk >= o.Disk && Tier(c, ramComponent, psuComponent) >= o.Minimum && Tier(c, ramComponent, psuComponent) <= o.Maximum;
    internal static bool Better(Choice a, Choice b, Options o, bool ramComponent, bool psuComponent = false)
    {
        // Complete plans always beat partial plans; for partial plans first reduce missing requirements.
        var ac = Complete(a, o, ramComponent, psuComponent); var bc = Complete(b, o, ramComponent, psuComponent);
        if (ac != bc) return ac;
        if (!ac)
        {
            if (a.Missing != b.Missing) return a.Missing < b.Missing;
            var ad = Math.Max(0, o.Vram - a.Vram) + Math.Max(0, o.Ram - a.Ram) + Math.Max(0, o.Disk - a.Disk);
            var bd = Math.Max(0, o.Vram - b.Vram) + Math.Max(0, o.Ram - b.Ram) + Math.Max(0, o.Disk - b.Disk);
            if (ad != bd) return ad < bd;
            if ((ramComponent || psuComponent) && Tier(a, ramComponent, psuComponent) != Tier(b, ramComponent, psuComponent))
                return Tier(a, ramComponent, psuComponent) > Tier(b, ramComponent, psuComponent);
        }
        // Minimum quantity means no optional items unless needed to meet capacity / tier.
        if (o.Quantity == 0 && a.Count != b.Count) return a.Count < b.Count;
        if (o.Performance)
        {
            var at = Tier(a, ramComponent, psuComponent); var bt = Tier(b, ramComponent, psuComponent);
            if (at != bt) return at > bt;
            if (psuComponent && a.Watts != b.Watts) return a.Watts > b.Watts;
            if (a.Vram != b.Vram) return a.Vram > b.Vram;
            if (a.Ram != b.Ram) return a.Ram > b.Ram;
            if (a.Disk != b.Disk) return a.Disk > b.Disk;
        }
        else
        {
            if (a.Value != b.Value) return a.Value < b.Value;
            if (a.Count != b.Count) return a.Count < b.Count;
            if (psuComponent && a.Watts != b.Watts) return a.Watts < b.Watts;
            var ae = a.Vram + a.Ram + a.Disk; var be = b.Vram + b.Ram + b.Disk;
            if (ae != be) return ae < be;
        }
        return Cheaper(a, b);
    }
    internal static Result Solve(IReadOnlyList<Group> groups, Options options, bool machine, bool ramComponent, bool psuComponent = false)
    {
        if (options.Minimum < 1 || options.Maximum > 5 || options.Minimum > options.Maximum ||
            options.Vram < 0 || options.Ram < 0 || options.Disk < 0) throw new ArgumentException("Invalid auto-fill options");
        var budget = new Budget();
        try
        {
            var prepared = groups.Select(g => SelectGroup(g, options, machine, ramComponent, psuComponent, budget)).ToArray();
            Choice? Search(bool fullOnly)
            {
                var states = new Dictionary<Key, Choice>(); Keep(states, new(), options, ramComponent, psuComponent);
                for (var index = 0; index < groups.Count; index++)
                {
                    var choices = prepared[index].Where(c => !fullOnly || c.Missing == 0).ToArray();
                    var remaining = groups.Skip(index + 1).SelectMany(g => g.Fixed.Concat(g.Available)).ToArray();
                    var vramDone = !remaining.Any(i => i.Vram > 0); var ramDone = !remaining.Any(i => i.Ram > 0);
                    var diskDone = !remaining.Any(i => i.Disk > 0);
                    var next = new Dictionary<Key, Choice>();
                    foreach (var previous in states.Values)
                    foreach (var choice in choices)
                    {
                        budget.Step();
                        var merged = Join(previous, choice, machine);
                        if (merged != null && (!(ramComponent || psuComponent) || Tier(merged, ramComponent, psuComponent) <= options.Maximum))
                            Keep(next, merged, options, ramComponent, psuComponent, vramDone, ramDone, diskDone);
                    }
                    states = next;
                    if (states.Count == 0) return null;
                }
                Choice? winner = null;
                foreach (var c in states.Values)
                    if (winner == null || Better(c, winner, options, ramComponent, psuComponent)) winner = c;
                return winner;
            }
            var best = Search(true);
            if (best == null || !Complete(best, options, ramComponent, psuComponent)) best = Search(false);
            return new Result { Choice = best, Complete = best != null && Complete(best, options, ramComponent, psuComponent) };
        }
        catch (SearchLimit) { return new Result { Limit = true }; }
    }
}
