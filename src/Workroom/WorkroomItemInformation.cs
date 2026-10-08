using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Il2Cpp;
using UnityEngine;

namespace PCExpansion;

/// <summary>提示内容由原版生成；保存快照提供不创建物品的基本信息回退。</summary>
internal static class WorkroomItemInformation
{
    internal sealed class Content
    {
        internal string Basic = "", Advanced = "", Characters = "";
    }

    internal static Content ReadNative(WorkroomItemCodec.Snapshot snapshot)
    {
        WorkroomItemCodec.Candidate? candidate = null;
        try
        {
            candidate = WorkroomItemCodec.Restore(snapshot);
            var element = candidate.Root.TryCast<GameItemElement>()
                ?? throw new InvalidOperationException("物品不支持原版信息接口。");
            var basic = element.GetTooltip();
            var advanced = element.GetTooltipAdvanced();
            return new Content
            {
                Basic = Display(basic), Advanced = Display(advanced),
                Characters = basic.nakedText + advanced.nakedText,
            };
        }
        finally
        {
            // Only strings survive this call, and cleanup must succeed before
            // the caller publishes native content into its display cache.
            if (candidate != null) WorkroomItemCodec.Destroy(candidate);
        }
    }

    private static string Display(RichText text)
    {
        if (text == null || string.IsNullOrWhiteSpace(text.nakedText))
            throw new InvalidOperationException("原版物品提示内容为空。");
        var formatted = string.IsNullOrWhiteSpace(text.richTextForeground) ? Escape(text.nakedText) : text.richTextForeground;
        // Native pixel text includes absolute pixel sizes for spacer lines.
        // The workroom's own font and scale determine readable text size.
        return Regex.Replace(formatted, @"</?size(?:=[^>]+)?>", "", RegexOptions.IgnoreCase);
    }

    internal static Content FromSnapshot(WorkroomItemCodec.Snapshot item)
    {
        var lines = new List<string> { "<b><color=#C4C4B0>" + Escape(item.Name) + "</color></b>" };
        var root = item.Nodes.Count == 0 ? null : item.Nodes[0];
        var baseValue = item.Value;
        if (root != null && root.Scalars.TryGetValue("unitBaseValue", out var savedValue) && savedValue.TryGetInt64(out var value))
            baseValue = value;
        lines.Add("<color=#429D8A>" + Escape(LanguageText.Get("workroom.info.value", item.Value, baseValue)) + "</color>");
        if (item.Count > 1) lines.Add(Escape(LanguageText.Get("workroom.info.quantity", item.Count)));
        // These are the native save DTO's actual type list and display helpers.
        // Never expose custody/assembly metadata tags as player descriptions.
        try
        {
            var save = JsonUtility.FromJson<SaveState>(item.Payload);
            var names = new List<string>();
            foreach (var type in save.saveItems[0].itemTypes)
            {
                var name = TypeHelper.GetTypeDisplayName(type);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var color = ColorUtility.ToHtmlStringRGB(TypeHelper.GetTypeColor(type));
                names.Add("<color=#" + color + ">[" + Escape(name) + "]</color>");
            }
            if (names.Count != 0) lines.Add(string.Join(" ", names));
        }
        catch (Exception ex) { Core.Debug("读取储存物品分类提示：" + ex.Message); }
        if (item.Broken) lines.Add("<color=#E49B7C>" + Escape(WorkroomStorage.Text("broken")) + "</color>");
        var shortDescription = ScalarText(root, "shortDescription");
        var longDescription = ScalarText(root, "longDescription");
        var flavor = ScalarText(root, "flavorText");
        var basic = new List<string>(lines);
        if (!string.IsNullOrWhiteSpace(shortDescription)) basic.Add(Escape(shortDescription));
        if (!string.IsNullOrWhiteSpace(flavor)) basic.Add("<i>" + Escape(flavor) + "</i>");
        var detailed = new List<string>(lines);
        var description = string.IsNullOrWhiteSpace(longDescription) ? shortDescription : longDescription;
        if (!string.IsNullOrWhiteSpace(description)) detailed.Add(Escape(description));
        if (!string.IsNullOrWhiteSpace(flavor)) detailed.Add("<i>" + Escape(flavor) + "</i>");
        return new Content { Basic = string.Join("\n", basic), Advanced = string.Join("\n", detailed),
            Characters = item.Name + shortDescription + longDescription + flavor + string.Join("", lines) };
    }

    private static string ScalarText(WorkroomItemCodec.Node? root, string name) =>
        root != null && root.Scalars.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
    private static string Escape(string text) => text.Replace("<", "&lt;").Replace(">", "&gt;");
}
