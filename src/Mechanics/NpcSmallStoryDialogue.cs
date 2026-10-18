using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Text-independent graphs for the ten small stories. Persistence belongs to the runtime.</summary>
internal static class NpcSmallStoryDialogue
{
    // UI requirements and familiar greetings are text, rather than story playback entries.
    internal static readonly string[] TextOnlyIds =
        { "L2-UI01", "J2-UI01", "L5-G01", "L5-G02", "J5-G01", "J5-G02" };
    internal static readonly string[] EntryIds =
    {
        "L1-001", "L2-001", "L2-102", "L2-202", "L2-301", "L2-310", "L2-paid-pending", "L2-320", "L2-321",
        "L2-330", "L2-332", "L2-333", "L2-901", "L3-001", "L3-301", "L3-401",
        "L4-001", "L4-102", "L4-202", "L4-301", "L4-303", "L4-304", "L4-901", "L5-001",
        "J1-001", "J2-001", "J2-102", "J2-202", "J2-301", "J2-310", "J2-320", "J2-321", "J2-903",
        "J3-001", "J4-001", "J5-001"
    };
    internal static IReadOnlyDictionary<string, StoryContent.Line> Graph => StoryContent.SmallGraph;

    internal static string Text(string id) => LanguageText.Get("smallstory." + id);

    /// <remarks>
    /// Commands must revalidate the current client, visit, save/session and transaction state.
    /// A transaction callback replaces its response in place; it must also copy choices and
    /// start/end callbacks from Build(client, resultEntry, command), without StartDialogue,
    /// and execute the replacement start callback once to notify shown for its actual result ID.
    /// Every ordinary/player line notifies shown:ID. Every terminal requests end:ID then
    /// complete:node; Graph[ID].Complete distinguishes narrative endings from temporary stops.
    /// The runtime must prove the last line finished and was not interrupted before persisting.
    /// A line-start/end notification alone is not proof that its text has been fully displayed.
    /// </remarks>
    internal static Dialogue Build(StoreClient client, string entryId, Action<string, Dialogue> command)
        => BuildInto(null, client, entryId, command);

    internal static void Rewrite(Dialogue response, StoreClient client, string entryId, Action<string, Dialogue> command)
        => BuildInto(response, client, entryId, command);

    private static Dialogue BuildInto(Dialogue? root, StoreClient client, string entryId, Action<string, Dialogue> command)
    {
        if (client == null) throw new ArgumentNullException(nameof(client));
        if (command == null) throw new ArgumentNullException(nameof(command));
        bool pendingReward = entryId == "L2-paid-pending";
        if (pendingReward) entryId = "L2-310";
        if (!Graph.ContainsKey(entryId)) throw new ArgumentException("Unknown small-story entry: " + entryId, nameof(entryId));
        var built = new Dictionary<string, Dialogue>(StringComparer.Ordinal);
        return Bind(entryId);

        Dialogue Bind(string id)
        {
            if (built.TryGetValue(id, out var existing)) return existing;
            var spec = Graph[id];
            var line = id == entryId && root != null ? root : new Dialogue();
            line.nextDialogue = null;
            line.choices?.Clear();
            line.startAction = null; line.endAction = null; line.isEndActionExecuted = false;
            line.SetText(spec.Player ? DialogUIManager.PLAYER : client.displayName, Text(id));
            built.Add(id, line);
            line.SetStartAction((Il2CppSystem.Action)(Action)(() => command("shown:" + id, line)));
            string? next = pendingReward && id == "L2-311" ? "L2-330" : spec.Next;
            if (next != null) line.SetNextDialogue(Bind(next));
            foreach (var choiceId in spec.Choices)
            {
                var selected = Bind(choiceId);
                var choice = Graph[choiceId];
                if (choice.Command != null)
                {
                    // Native SelectChoice reads currentDialog after choiceAction. Run work on
                    // the response start, using a truthful unresolved fallback until it succeeds.
                    string fallback = choiceId.StartsWith("L4-", StringComparison.Ordinal) ? "L4-304" :
                        choiceId.StartsWith("L2-", StringComparison.Ordinal) ? "L2-321" : "J2-321";
                    var response = new Dialogue().SetText(client.displayName, Text(fallback));
                    var action = choice.Command;
                    response.SetStartAction((Il2CppSystem.Action)(Action)(() => command(action, response)));
                    selected.SetNextDialogue(response);
                }
                line.AddChoice(ItemDirectory.CreateEmptyItem("pcrepair_smallstory_choice"), Text(choiceId), "", selected, null);
            }
            if (!spec.Player && spec.Next == null && spec.Choices.Length == 0)
            {
                string node = id.Substring(0, 2);
                line.SetEndAction((Il2CppSystem.Action)(Action)(() =>
                {
                    command("end:" + id, line);
                    command("complete:" + node, line);
                }));
            }
            return line;
        }
    }

}
