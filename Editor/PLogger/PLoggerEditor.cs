using System.IO;
using UnityEditor;
using UnityEngine;

public class PLoggerEditor
{
    [MenuItem("UPandaGF/日志系统/启动日志", false, 0)]
    public static void EnablePLoger()
    {
        ScriptingDefineSymbols.AddScriptingDefineSymbol("OPEN_PLOG");
    }

    [MenuItem("UPandaGF/日志系统/剔除日志", false, 1)]
    public static void ClosePLoger()
    {
        ScriptingDefineSymbols.RemoveScriptingDefineSymbol("OPEN_PLOG");
        string configPath = Application.streamingAssetsPath + "/Data/LogConfig.json";
        if(File.Exists(configPath))
        {
            File.Delete(configPath);
            Debug.Log("LogConfig.json已删除");
            AssetDatabase.Refresh();
        }
    }

}
