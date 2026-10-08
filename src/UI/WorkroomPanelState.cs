namespace PCExpansion;

/// <summary>面板开关与关闭后的输入释放守卫；与可销毁的Unity视图分离。</summary>
internal sealed class WorkroomPanelState
{
    internal bool IsOpen { get; private set; }
    internal bool BlocksInput => (IsOpen && modal) || releasing;
    private bool releasing, roomOwner, modal;
    private int neutralFrames;

    internal bool Open(bool inRoom, bool isModal = true)
    {
        if (BlocksInput) return false;
        IsOpen = true;
        modal = isModal;
        roomOwner = inRoom;
        neutralFrames = 0;
        return true;
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        releasing = modal;
        neutralFrames = 0;
    }

    internal void Tick(bool inRoom, bool stable, bool escapePressed, bool inputNeutral)
    {
        if (IsOpen && (!stable || roomOwner != inRoom || escapePressed)) Close();
        if (!releasing) return;
        neutralFrames = inputNeutral ? neutralFrames + 1 : 0;
        if (neutralFrames >= 2) releasing = false;
    }

    internal void Reset()
    {
        IsOpen = releasing = false;
        neutralFrames = 0;
    }
}
