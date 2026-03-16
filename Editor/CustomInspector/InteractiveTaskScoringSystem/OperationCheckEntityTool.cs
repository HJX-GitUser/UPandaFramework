using System.Collections;
using System.Collections.Generic;
using UnityEngine;
namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    public static class OperationCheckEntityTool
    {
        public static TaskEntityBase GetEntity(int targetID)
        {
            TaskEntityBase[] taskEntities = Object.FindObjectsOfType<TaskEntityBase>();
            TaskEntityBase arg = null;
            foreach (var item in taskEntities)
            {
                if (targetID == item.Id)
                {
                    arg = item;
                    break;
                }
            }
            return arg;
        }
    }


}


