using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

/// <summary>
/// 脚本宏定义。
/// </summary>
public static class ScriptingDefineSymbols
{
#if UNITY_2021_OR_NEWER
    // Unity 2021+ 使用 NamedBuildTarget
    private static readonly object[] BuildTargetGroups = new object[]
    {
        NamedBuildTarget.Standalone,
        NamedBuildTarget.iOS,
        NamedBuildTarget.Android,
        NamedBuildTarget.WebGL
    };
#else
    // Unity 2020 及以下版本使用 BuildTargetGroup
    private static readonly object[] BuildTargetGroups = new object[]
    {
        BuildTargetGroup.Standalone,
        BuildTargetGroup.iOS,
        BuildTargetGroup.Android,
        BuildTargetGroup.WebGL
    };
#endif

    /// <summary>
    /// 检查指定平台是否存在指定的脚本宏定义。
    /// </summary>
    /// <param name="buildTargetGroup">要检查脚本宏定义的平台。</param>
    /// <param name="scriptingDefineSymbol">要检查的脚本宏定义。</param>
    /// <returns>指定平台是否存在指定的脚本宏定义。</returns>
    public static bool HasScriptingDefineSymbol(object buildTargetGroup, string scriptingDefineSymbol)
    {
        if (string.IsNullOrEmpty(scriptingDefineSymbol))
        {
            return false;
        }

        string[] scriptingDefineSymbols = GetScriptingDefineSymbols(buildTargetGroup);
        foreach (string i in scriptingDefineSymbols)
        {
            if (i == scriptingDefineSymbol)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 为指定平台增加指定的脚本宏定义。
    /// </summary>
    /// <param name="buildTargetGroup">要增加脚本宏定义的平台。</param>
    /// <param name="scriptingDefineSymbol">要增加的脚本宏定义。</param>
    public static void AddScriptingDefineSymbol(object buildTargetGroup, string scriptingDefineSymbol)
    {
        if (string.IsNullOrEmpty(scriptingDefineSymbol))
        {
            return;
        }

        if (HasScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol))
        {
            return;
        }

        List<string> scriptingDefineSymbols = new List<string>(GetScriptingDefineSymbols(buildTargetGroup))
        {
            scriptingDefineSymbol
        };

        SetScriptingDefineSymbols(buildTargetGroup, scriptingDefineSymbols.ToArray());
    }

    /// <summary>
    /// 为指定平台移除指定的脚本宏定义。
    /// </summary>
    /// <param name="buildTargetGroup">要移除脚本宏定义的平台。</param>
    /// <param name="scriptingDefineSymbol">要移除的脚本宏定义。</param>
    public static void RemoveScriptingDefineSymbol(object buildTargetGroup, string scriptingDefineSymbol)
    {
        if (string.IsNullOrEmpty(scriptingDefineSymbol))
        {
            return;
        }

        if (!HasScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol))
        {
            return;
        }

        List<string> scriptingDefineSymbols = new List<string>(GetScriptingDefineSymbols(buildTargetGroup));
        while (scriptingDefineSymbols.Contains(scriptingDefineSymbol))
        {
            scriptingDefineSymbols.Remove(scriptingDefineSymbol);
        }

        SetScriptingDefineSymbols(buildTargetGroup, scriptingDefineSymbols.ToArray());
    }

    /// <summary>
    /// 为所有平台增加指定的脚本宏定义。
    /// </summary>
    /// <param name="scriptingDefineSymbol">要增加的脚本宏定义。</param>
    public static void AddScriptingDefineSymbol(string scriptingDefineSymbol)
    {
        if (string.IsNullOrEmpty(scriptingDefineSymbol))
        {
            return;
        }

        foreach (object buildTargetGroup in BuildTargetGroups)
        {
            AddScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol);
        }
    }

    /// <summary>
    /// 为所有平台移除指定的脚本宏定义。
    /// </summary>
    /// <param name="scriptingDefineSymbol">要移除的脚本宏定义。</param>
    public static void RemoveScriptingDefineSymbol(string scriptingDefineSymbol)
    {
        if (string.IsNullOrEmpty(scriptingDefineSymbol))
        {
            return;
        }

        foreach (object buildTargetGroup in BuildTargetGroups)
        {
            RemoveScriptingDefineSymbol(buildTargetGroup, scriptingDefineSymbol);
        }
    }

    /// <summary>
    /// 获取指定平台的脚本宏定义。
    /// </summary>
    /// <param name="buildTargetGroup">要获取脚本宏定义的平台。</param>
    /// <returns>平台的脚本宏定义。</returns>
    public static string[] GetScriptingDefineSymbols(object buildTargetGroup)
    {
#if UNITY_2021_OR_NEWER
        if (buildTargetGroup is NamedBuildTarget namedTarget)
        {
            return PlayerSettings.GetScriptingDefineSymbols(namedTarget).Split(';');
        }
#else
        if (buildTargetGroup is BuildTargetGroup targetGroup)
        {
            return PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup).Split(';');
        }
#endif
        throw new System.ArgumentException("Unsupported build target group type");
    }

    /// <summary>
    /// 设置指定平台的脚本宏定义。
    /// </summary>
    /// <param name="buildTargetGroup">要设置脚本宏定义的平台。</param>
    /// <param name="scriptingDefineSymbols">要设置的脚本宏定义。</param>
    public static void SetScriptingDefineSymbols(object buildTargetGroup, string[] scriptingDefineSymbols)
    {
#if UNITY_2021_OR_NEWER
        if (buildTargetGroup is NamedBuildTarget namedTarget)
        {
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, string.Join(";", scriptingDefineSymbols));
            return;
        }
#else
        if (buildTargetGroup is BuildTargetGroup targetGroup)
        {
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, string.Join(";", scriptingDefineSymbols));
            return;
        }
#endif
        throw new System.ArgumentException("Unsupported build target group type");
    }
}