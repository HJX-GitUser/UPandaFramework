using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UILoadInfoAttribute : Attribute
    {
        public E_UI_Layer ui_Layer { get; }
        public AssetLoadMethod loadMethod { get; }
        public string loadPath { get; }

        /// <summary>
        /// UI加载信息
        /// </summary>
        /// <param name="ui_LayerArg">显示层级</param>
        /// <param name="loadMethodArg">加载方式</param>
        /// <param name="loadPathArg">【Resources只需要相对路径，AssetBundle需要编辑器路径，你可以在Project面板对应资源上右键->Copy Path】</param>
        public UILoadInfoAttribute(E_UI_Layer ui_LayerArg, AssetLoadMethod loadMethodArg, string loadPathArg)
        {
            ui_Layer = ui_LayerArg;
            loadMethod = loadMethodArg;
            loadPath = loadPathArg;
        }
    }
}


