using UnityEngine;

namespace UPandaGF.Tests
{

/// <summary>测试用对象查找：兼容 2022.2+ 的 FindObjectsByType 与旧版 FindObjectsOfType。</summary>
internal static class TestObjectSearch
{
    public static T[] All<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }
}

}