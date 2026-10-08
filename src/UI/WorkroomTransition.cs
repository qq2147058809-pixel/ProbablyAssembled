using System;

namespace PCExpansion;

/// <summary>黑屏期间切换区域；只记录过渡，不读取或修改原版出门状态。</summary>
internal sealed class WorkroomTransition
{
    internal enum Stage { Home, EnterFade, EnterBlack, EnterReveal, Inside, ExitFade, ExitBlack, ExitReveal, Release }
    internal Stage Current { get; private set; }
    internal bool BlocksInput => Current != Stage.Home;
    internal bool RoomVisible { get; private set; }
    internal float Opacity { get; private set; }
    private float elapsed;
    private int neutralFrames;
    private const float FadeSeconds = .22f, BlackSeconds = .12f;

    internal bool Enter()
    {
        if (Current != Stage.Home) return false;
        Change(Stage.EnterFade);
        return true;
    }

    internal bool Exit()
    {
        if (Current != Stage.Inside) return false;
        Change(Stage.ExitFade);
        return true;
    }

    internal void Tick(float unscaledSeconds, bool inputNeutral)
    {
        // A stalled frame must still show each black/switch/reveal stage.
        var delta = float.IsFinite(unscaledSeconds) ? Math.Clamp(unscaledSeconds, 0, .1f) : 0;
        elapsed += delta;
        neutralFrames = inputNeutral ? Math.Min(2, neutralFrames + 1) : 0;
        switch (Current)
        {
            case Stage.EnterFade:
            case Stage.ExitFade:
                Opacity = Math.Min(1, elapsed / FadeSeconds);
                if (Opacity >= 1) Change(Current == Stage.EnterFade ? Stage.EnterBlack : Stage.ExitBlack);
                break;
            case Stage.EnterBlack:
                if (elapsed >= BlackSeconds && neutralFrames >= 2)
                { RoomVisible = true; Change(Stage.EnterReveal); }
                break;
            case Stage.ExitBlack:
                if (elapsed >= BlackSeconds && neutralFrames >= 2)
                { RoomVisible = false; Change(Stage.ExitReveal); }
                break;
            case Stage.EnterReveal:
            case Stage.ExitReveal:
                Opacity = Math.Max(0, 1 - elapsed / FadeSeconds);
                if (Opacity <= 0) Change(Current == Stage.EnterReveal ? Stage.Inside : Stage.Release);
                break;
            case Stage.Release:
                if (neutralFrames >= 2) Abort();
                break;
        }
    }

    internal void Abort()
    {
        RoomVisible = false;
        Opacity = 0;
        Change(Stage.Home);
    }

    private void Change(Stage next)
    {
        Current = next;
        elapsed = 0;
        neutralFrames = 0;
    }
}
