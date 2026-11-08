using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 标准 Build 菜单和框架构建入口都会经过这里；Android/WebGL 无法枚举 StreamingAssets，
    /// 必须在打包前把 BinaryDataIndex.txt 写进随包目录。
    /// </summary>
    public sealed class BinaryDataIndexBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            BuildTarget target = report.summary.platform;
            if (target == BuildTarget.Android || target == BuildTarget.WebGL)
                ExcelTool.RebuildBinaryIndex();
        }
    }
}
