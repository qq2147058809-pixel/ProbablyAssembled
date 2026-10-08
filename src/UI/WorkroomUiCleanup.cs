using System;
using System.Collections.Generic;
using UnityEngine;

namespace PCExpansion;

/// <summary>逐项释放自有界面；探测失败保留引用与输入保护，已销毁对象不再调用。</summary>
internal sealed class WorkroomUiCleanup
{
    private readonly List<Exception> failures = new();

    internal void Run(string stage, Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            failures.Add(new InvalidOperationException("工作间界面清理失败；step=" + stage, ex));
        }
    }

    internal void ThrowIfFailed()
    {
        if (failures.Count != 0)
            throw new AggregateException("工作间界面尚未释放，保留操作保护并稍后重试。", failures);
    }

    // Unlike the display helper, a failed probe must not count as completed cleanup.
    internal static bool IsAlive(UnityEngine.Object? value) =>
        !ReferenceEquals(value, null) && UnityEngine.Object.IsNativeObjectAlive(value);

    internal static void Deactivate(GameObject? root)
    {
        if (IsAlive(root)) root!.SetActive(false);
    }

    internal static void DeactivateCanvas(Canvas? canvas)
    {
        if (IsAlive(canvas)) Deactivate(canvas!.gameObject);
    }
}
