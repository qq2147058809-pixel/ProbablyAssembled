using System;
using System.Collections.Generic;
using Il2Cpp;

namespace PCExpansion;

/// <summary>Two dedicated rack bodies. Their v3 machine record owns the actual installed parts.</summary>
internal static class ServerRoomHardware
{
    internal const string MarkerTag = "PCEXPANSION_SERVER_HARDWARE";
    private const string MiningTag = "PCEXPANSION_MINING_BODY";
    private const string ServerTag = "PCEXPANSION_SERVER_BODY";
    internal const int GridWidth = 5, GridHeight = 3;
    internal static string ArtworkName(ServerRoomMachine.Definition definition, bool rack = false) =>
        "server_room_" + (rack ? "rack_" : "") + definition.Kind + "_case_t" + definition.Tier;

    internal static bool IsHardware(GameItem? item) => item != null &&
        ServerRoomMachine.TryDefinition(Core.Clean(item.identifier), out _);

    internal static GameItem Create(string id)
    {
        if (!ServerRoomMachine.TryDefinition(id, out var definition))
            throw new ArgumentOutOfRangeException(nameof(id), id, "未知的机房专用机箱。");
        var item = DirectoryMaster.Item("machine_bay", true)
            ?? throw new InvalidOperationException("找不到原版机器舱模板。");
        try
        {
            Apply(item, definition);
            item.onLoaded = (Il2CppSystem.Action<GameItem>)(loaded => Apply(loaded, definition));
            return item;
        }
        catch
        {
            WorkroomItemCodec.Destroy(new WorkroomItemCodec.Candidate
                { Root = item, Nodes = new List<GameItem> { item } });
            throw;
        }
    }

    private static void Apply(GameItem item, ServerRoomMachine.Definition definition)
    {
        var name = LanguageText.Get(definition.Kind == ServerRoomMachine.Mining ?
            "serverroom.item.mining" : "serverroom.item.server", definition.Tier);
        var description = LanguageText.Get(definition.Kind == ServerRoomMachine.Mining ?
            "serverroom.item.mining_description" : "serverroom.item.server_description", definition.Tier);
        item.identifier = definition.Identifier;
        item.identifierName = name;
        item.SetName(name);
        item.shortDescription = description;
        item.longDescription = description;
        item.flavorText = LanguageText.Get("serverroom.item.flavor");
        item.unitCount = 1;
        item.SetValue(definition.BaseValue);
        item.SetShape(new GridShapeBuilder(GridWidth, GridHeight).SetDataFill(1).Build());
        item.spritePath = definition.Identifier;
        item.spriteAtlasPath = string.Empty;
        item.spriteChanged = true;
        item.RemoveAllGameItemType();
        item.SetGameItemType("MACHINE");
        item.SetGameItemType(ComputerSuppliesType.Identifier);
        item.EnableTag("MACHINE_TAG", false);
        item.DisableTag("NESTABLE_DEVICE_TAG", false);
        item.DisableTag(Components.CaseTag, false);
        item.DisableTag(Components.MarkerTag, false);
        item.DisableTag("BROKEN_MACHINE", false);
        item.EnableTag(MarkerTag, false);
        item.EnableTag(definition.Kind == ServerRoomMachine.Mining ? MiningTag : ServerTag, false);
        item.DisableTag(definition.Kind == ServerRoomMachine.Mining ? ServerTag : MiningTag, false);
        foreach (var tag in Components.TierTags) item.DisableTag(tag, false);
        item.EnableTag(Components.TierTags[definition.Tier - 1], false);
        item.soundContainerOpen = "open_machine";
        item.soundContainerClose = "close_machine";
        item.soundDragStart = "metal_machine_drag";
        item.soundDragEnd = "metal_machine_drop";
        Components.SetTradeProperties(item, false, false);
        item.RemoveItemFeatureByID(CaseEconomy.MachinePriceFeatureId);
        WorkroomMachineAssembly.Refresh(item);
        LowerAssemblerNpc.RefreshPurchaseEligibility(item);
    }

}
