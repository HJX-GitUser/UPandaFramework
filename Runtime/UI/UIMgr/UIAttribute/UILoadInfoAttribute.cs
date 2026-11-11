using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>面板根 RectTransform 的布局策略。</summary>
    public enum UIPanelPlacement
    {
        /// <summary>铺满所在 UI 层（默认，保持旧行为）。</summary>
        FullScreen = 0,
        /// <summary>保留预制体尺寸与锚点，只把 anchoredPosition 归零。</summary>
        Window = 1,
        /// <summary>完全保留预制体布局，不做任何 RectTransform 重置。</summary>
        Preserve = 2
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class UILoadInfoAttribute : Attribute
    {
        public E_UI_Layer ui_Layer { get; }
        public AssetLoadMethod loadMethod { get; }
        public string loadPath { get; }

        /// <summary>面板根布局策略；默认 FullScreen，保持旧项目行为。</summary>
        public UIPanelPlacement placement { get; set; } = UIPanelPlacement.FullScreen;

        /// <summary>模态面板：打开后阻挡下层交互，并可触发下层 OnPause。</summary>
        public bool modal { get; set; }

        /// <summary>打开时申请暂停凭据；关闭/销毁时释放。</summary>
        public bool pauseGame { get; set; }

        /// <summary>返回键是否可关闭该面板；默认 true。</summary>
        public bool closeOnBack { get; set; } = true;

        /// <summary>同层排序权重，越大越靠上；默认 0。</summary>
        public int order { get; set; }

        /// <summary>关闭后是否回到面板池复用；默认 false，按面板逐个开启。</summary>
        public bool pool { get; set; }

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


