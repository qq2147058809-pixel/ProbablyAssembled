using System;

namespace PCExpansion;

/// <summary>记录三区域转场；全黑后由主控提交，不读取或修改原版出门状态。</summary>
internal sealed class WorkroomTransition
{
    internal enum Region { Shop, Workroom, ServerRoom }
    internal enum Stage { Home, Inside, FadeOut, Black, FadeIn, Release }
    internal Stage Current { get; private set; }
    internal Region CurrentRegion { get; private set; }
    internal Region SourceRegion { get; private set; }
    internal Region TargetRegion { get; private set; }
    internal bool IsStable => Current is Stage.Home or Stage.Inside;
    internal bool BlocksInput => Current != Stage.Home;
    internal bool RegionVisible => CurrentRegion != Region.Shop;
    internal bool RoomVisible => CurrentRegion == Region.Workroom;
    internal bool ServerRoomVisible => CurrentRegion == Region.ServerRoom;
    internal float Opacity { get; private set; }
    internal bool ReadyToSwitch => Current == Stage.Black && elapsed >= BlackSeconds && neutralFrames >= 2;
    private float elapsed;
    private int neutralFrames;
    private const float FadeSeconds = .22f, BlackSeconds = .12f;

    internal bool Request(Region target)
    {
        if (!IsStable || target == CurrentRegion ||
            target is not (Region.Shop or Region.Workroom or Region.ServerRoom) ||
            (CurrentRegion != Region.Workroom && target != Region.Workroom)) return false;
        SourceRegion = CurrentRegion;
        TargetRegion = target;
        Change(Stage.FadeOut);
        return true;
    }

    internal bool Enter() => CurrentRegion == Region.Shop && Request(Region.Workroom);

    /// <summary>主控已清理来源面板、创建目标并切换可见根后，在同一帧提交。</summary>
    internal bool CommitSwitch()
    {
        if (!ReadyToSwitch) return false;
        CurrentRegion = TargetRegion;
        Change(Stage.FadeIn);
        return true;
    }

    /// <summary>仅在全黑且失败目标的部分资源已清理后调用；保留来源及输入隔离。</summary>
    internal bool CancelSwitch()
    {
        if (Current != Stage.Black) return false;
        TargetRegion = CurrentRegion = SourceRegion;
        Change(Stage.FadeIn);
        return true;
    }

    internal void Tick(float unscaledSeconds, bool inputNeutral)
    {
        // Called once per frame. A stalled frame must still visit every stage;
        // neutral samples taken during fade-out cannot qualify the black stage.
        var delta = float.IsFinite(unscaledSeconds) ? Math.Clamp(unscaledSeconds, 0, .1f) : 0;
        if (!IsStable) elapsed += delta;
        neutralFrames = inputNeutral ? Math.Min(2, neutralFrames + 1) : 0;
        switch (Current)
        {
            case Stage.FadeOut:
                Opacity = Math.Min(1, elapsed / FadeSeconds);
                if (Opacity >= 1) Change(Stage.Black);
                break;
            // Black deliberately has no automatic advance or timeout. Failed
            // creation/cleanup must retain the lease until the owner resolves it.
            case Stage.FadeIn:
                Opacity = Math.Max(0, 1 - elapsed / FadeSeconds);
                if (Opacity <= 0)
                {
                    if (CurrentRegion == Region.Shop) Change(Stage.Release);
                    else Settle();
                }
                break;
            case Stage.Release:
                if (neutralFrames >= 2) Settle();
                break;
        }
    }

    /// <summary>只供主控已完成全部自有UI清理、归还店铺租约后的生命周期重置。</summary>
    internal void Abort()
    {
        CurrentRegion = Region.Shop;
        Opacity = 0;
        Settle();
    }

    private void Settle()
    {
        SourceRegion = TargetRegion = CurrentRegion;
        Change(CurrentRegion == Region.Shop ? Stage.Home : Stage.Inside);
    }

    private void Change(Stage next)
    {
        Current = next;
        elapsed = 0;
        neutralFrames = 0;
    }
}
