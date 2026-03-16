using System;
using System.Collections;
using System.Collections.Generic;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 操作配置
    /// </summary>
    [System.Serializable]
    public class OperatingStepData
    {
        public int id;                          // 操作id
        public int correctTargetId;             // 正确操作目标实体id
        public string tips;                     // 步骤提示
    }

}
