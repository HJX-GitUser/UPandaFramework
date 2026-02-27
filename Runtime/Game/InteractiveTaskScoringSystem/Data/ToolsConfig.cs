using System;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    [Serializable]
    public class ToolConfig
    {
        public string id;           // 工具id
        public string toolName;     // 工具名称
    }
    [CreateAssetMenu(fileName = "ToolsConfig", menuName = "UPandaGF/InteractiveTaskScoringSystem/工具配置")]
    public class ToolsConfig : ScriptableObject//工具配置
    {
        public ToolConfig[] tools;
    }

    [Serializable]
    public class PartConfig
    {
        public string id;           // id
        public string partName;     // 名称
    }

    [CreateAssetMenu(fileName = "PartsConfig", menuName = "UPandaGF/InteractiveTaskScoringSystem/交互部件配置")]
    public class PartsConfig : ScriptableObject// 交互部件配置
    {
        public PartConfig[] parts;
    }
}

