using NUnit.Framework;
using UnityEditor;
using UPandaGF.GFEditor;
using UnityEngine;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>图标资源与编辑器绑定回归：恢复的 PNG/GUISkin 必须能按模块相对路径加载。</summary>
    public class LogConsoleIconTests
    {
        [Test]
        public void LoaderAssignsBundledIconsAndSkin()
        {
            var go = new GameObject("LogConsoleIconTest");
            try
            {
                var console = go.AddComponent<UPandaLogConsole>();
                LogConsoleIconLoader.Assign(console);

                Assert.IsNotNull(console.Icons, "Icons 容器不能为空");
                Assert.IsNotNull(console.Icons.clearImage, "clear.png 未绑定");
                Assert.IsNotNull(console.Icons.logImage, "log_icon.png 未绑定");
                Assert.IsNotNull(console.Icons.warningImage, "warning_icon.png 未绑定");
                Assert.IsNotNull(console.Icons.errorImage, "error_icon.png 未绑定");
                Assert.IsNotNull(console.Icons.scrollerSkin, "logConsoleScrollerSkin 未绑定");
                StringAssert.Contains("LogConsole/Icons/log_icon.png", AssetDatabase.GetAssetPath(console.Icons.logImage));
                StringAssert.Contains("LogConsole/Icons/logConsoleScrollerSkin.guiskin",
                    AssetDatabase.GetAssetPath(console.Icons.scrollerSkin));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
