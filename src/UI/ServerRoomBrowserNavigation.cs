using System;
using System.Collections.Generic;

namespace PCExpansion;

/// <summary>浏览器仅保存导航位置；历史项不保存或重放资产命令。</summary>
internal sealed class ServerRoomBrowserNavigation
{
    internal enum Page { Overview, Mining, Trading, Operation, Catalog, Rack }
    internal readonly record struct Route(Page Kind, int Parameter = 0)
    {
        internal int Site => Kind == Page.Trading ? 1 : Kind == Page.Catalog ? 2 : 0;
    }

    private const int Limit = 32;
    private readonly List<Route>[] history = { new(), new(), new() };
    private readonly int[] positions = new int[3];
    internal int Site { get; private set; }
    internal Route Current => history[Site][positions[Site]];
    internal bool CanGoBack => positions[Site] > 0;
    internal bool CanGoForward => positions[Site] + 1 < history[Site].Count;

    internal ServerRoomBrowserNavigation() => Reset();

    internal void Reset()
    {
        for (var site = 0; site < 3; site++) { history[site].Clear(); positions[site] = 0; }
        history[0].Add(new Route(Page.Overview));
        history[1].Add(new Route(Page.Trading));
        history[2].Add(new Route(Page.Catalog));
        Site = 0;
    }

    internal Route SelectSite(int site)
    {
        if (site < 0 || site >= history.Length) throw new ArgumentOutOfRangeException(nameof(site));
        Site = site;
        return Current;
    }

    internal Route Navigate(Route route)
    {
        if (!Enum.IsDefined(typeof(Page), route.Kind) ||
            route.Kind == Page.Rack && (route.Parameter < 0 || route.Parameter >= 10) ||
            route.Kind == Page.Trading && (route.Parameter < 0 || route.Parameter >= 5) ||
            route.Kind == Page.Catalog && (route.Parameter < 0 || route.Parameter >= 2))
            throw new ArgumentOutOfRangeException(nameof(route));
        Site = route.Site;
        if (route == Current) return Current;
        var list = history[Site];
        if (CanGoForward) list.RemoveRange(positions[Site] + 1, list.Count - positions[Site] - 1);
        list.Add(route);
        if (list.Count > Limit) list.RemoveAt(0);
        positions[Site] = list.Count - 1;
        return Current;
    }

    internal Route Back()
    {
        if (CanGoBack) positions[Site]--;
        return Current;
    }

    internal Route Forward()
    {
        if (CanGoForward) positions[Site]++;
        return Current;
    }
}
