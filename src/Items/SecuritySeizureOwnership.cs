using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes;

namespace PCExpansion;

/// <summary>收缴盒归属同步；嵌套回调加入当前批次，不再递归进入图遍历。</summary>
internal static class SecuritySeizureOwnership
{
    [ThreadStatic] private static Synchronization? current;

    internal static void Synchronize(GameItem box, bool owned)
    {
        if (current != null)
        {
            current.Request(box, owned);
            return;
        }

        var operation = new Synchronization();
        current = operation;
        try
        {
            operation.Request(box, owned);
            operation.Drain();
        }
        finally { current = null; }
    }

    private sealed class Synchronization
    {
        private sealed record Work(Il2CppObjectBase Node, bool Owned, Work? Parent = null,
            GraphNodeStorage? ParentGraph = null, GameItem? WindowBox = null);

        private readonly Queue<Work> pending = new();
        private readonly HashSet<(IntPtr Pointer, bool Owned)> requestedBoxes = new();
        private readonly HashSet<(IntPtr Pointer, bool Owned)> visited = new();
        private readonly HashSet<(IntPtr Pointer, bool Owned)> applied = new();

        internal void Request(GameItem box, bool owned)
        {
            var identity = (box.Pointer, owned);
            if (!requestedBoxes.Add(identity)) return;
            // 原版 SetItemOwned 已处理盒本身；回边不能再次调用它。
            applied.Add(identity);
            var window = box.contentWindow;
            if (window == null) return;
            // 盒子已接受原版归属变更；即使随后被移走，内部仍须完成这次同步。
            pending.Enqueue(new Work(window, owned, WindowBox: box));
        }

        internal void Drain()
        {
            while (pending.TryDequeue(out var request))
            {
                var node = request.Node;
                if (!IsCurrent(request) || !visited.Add((node.Pointer, request.Owned))) continue;
                var graph = node.TryCast<GraphNodeStorage>();
                var children = graph?.children;
                if (children == null) continue;
                // 归属回调可以改变原生列表；快照避免持有跨回调的枚举器。
                foreach (var child in children.ToArray())
                {
                    if (child == null) continue;
                    var childRequest = new Work(child, request.Owned, request, graph);
                    if (!IsCurrent(childRequest)) continue;
                    var item = child.TryCast<GameItem>();
                    if (item != null && applied.Add((item.Pointer, request.Owned)))
                        GeneralHelper.SetItemOwned(item, request.Owned);
                    // 另一补丁移走节点后，不沿旧关系修改它的后代。
                    if (IsCurrent(childRequest)) pending.Enqueue(childRequest);
                }
            }
        }

        private static bool IsCurrent(Work request)
        {
            // 连祖先一起复核，避免队列里的子图已经随父节点被移出盒子。
            for (Work? link = request; link != null; link = link.Parent)
            {
                if (link.WindowBox != null && link.WindowBox.contentWindow?.Pointer != link.Node.Pointer)
                    return false;
                if (link.ParentGraph != null && !IsChild(link.ParentGraph, link.Node.Pointer)) return false;
            }
            return true;
        }

        private static bool IsChild(GraphNodeStorage graph, IntPtr pointer)
        {
            var children = graph.children;
            if (children == null) return false;
            foreach (var child in children)
                if (child != null && child.Pointer == pointer) return true;
            return false;
        }
    }
}
