using System;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
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

