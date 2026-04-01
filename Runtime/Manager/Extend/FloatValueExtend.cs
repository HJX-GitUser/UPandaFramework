using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class FloatValueExtend
{
    /// <summary>
    /// 格式化时间为 时:分:秒
    /// </summary>
    /// <param name="totalSeconds"></param>
    /// <returns></returns>
    public static string FormatTime(this float totalSeconds)
    {
        int hours = Mathf.FloorToInt(totalSeconds / 3600);
        int minutes = Mathf.FloorToInt((totalSeconds % 3600) / 60);
        int seconds = Mathf.FloorToInt(totalSeconds % 60);
        int milliseconds = Mathf.FloorToInt((totalSeconds * 1000) % 1000);
        // 毫秒显示（可选，按需使用）
        // return $"{hours:D2}:{minutes:D2}:{seconds:D2}.{milliseconds:D3}";

        return $"{hours:D2}:{minutes:D2}:{seconds:D2}";
    }
}
