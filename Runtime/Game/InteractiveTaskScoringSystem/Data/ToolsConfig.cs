using System;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [Serializable]
    public class ToolConfig
    {
        public string id;           // 工具id
        public string toolName;     // 工具名称
        public string toolModel;    // 工具型号
    }
    [CreateAssetMenu(fileName = "ToolsConfig", menuName = "UPandaGF/InteractiveTaskScoringSystem/工具配置")]
    public class ToolsConfig : ScriptableObject//工具配置
    {
        public ToolConfig[] tools;
    }
}

