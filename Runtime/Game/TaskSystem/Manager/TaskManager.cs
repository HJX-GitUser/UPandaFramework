using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    public class TaskManager : BaseMonoSingleton<TaskManager>
    {
        public List<Task> tasks;
        private GameObject taskNode;
        private void Start()
        {
            taskNode = new GameObject("TaskNode");
            taskNode.transform.SetParent(transform);
        }
    }
}

