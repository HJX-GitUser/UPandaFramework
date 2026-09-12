using System.IO;
using UnityEditor;
using UnityEngine;

public class PLoggerEditor
{
    [MenuItem("UPandaGF/日志系统/启动日志", false, 0)]
    public static void EnablePLoger()
    {
        ScriptingDefineSymbols.AddScriptingDefineSymbol("OPEN_PLOG");
        Debug.Log("已为 Standalone / iOS / Android / WebGL 添加 OPEN_PLOG 宏定义，重新编译后日志生效。");
    }

    [MenuItem("UPandaGF/日志系统/剔除日志", false, 1)]
    public static void ClosePLoger()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol("OPEN_PLOG");
        // 修复：不再顺手删除 StreamingAssets/Data/LogConfig.json —— 原实现把"关日志"和"删配置"绑在一起，
        // 用户只是不想打日志，结果配置资产一起没了。删不删交给使用者自己决定。
        Debug.Log("已移除 OPEN_PLOG 宏定义，重新编译后日志调用会被整体剔除。日志配置文件保持不变："
            + Application.streamingAssetsPath + "/Data/LogConfig.json");
    }

}
