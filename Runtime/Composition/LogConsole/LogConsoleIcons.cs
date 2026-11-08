using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 日志控制台使用的图标与滚动条皮肤。图标为项目自有资产，位于本模块的 <c>Icons</c> 目录。
    /// <para>由编辑器在创建控制台时写入；运行时为 null 时视图会自动退回纯文字按钮。</para>
    /// </summary>
    [Serializable]
    public sealed class LogConsoleIcons
    {
        public Texture2D clearImage;
        public Texture2D collapseImage;
        public Texture2D clearOnNewSceneImage;
        public Texture2D showTimeImage;
        public Texture2D showSceneImage;
        public Texture2D userImage;
        public Texture2D showMemoryImage;
        public Texture2D softwareImage;
        public Texture2D dateImage;
        public Texture2D showFpsImage;
        public Texture2D infoImage;
        public Texture2D saveLogsImage;
        public Texture2D searchImage;
        public Texture2D copyImage;
        public Texture2D closeImage;
        public Texture2D buildFromImage;
        public Texture2D systemInfoImage;
        public Texture2D graphicsInfoImage;
        public Texture2D backImage;
        public Texture2D logImage;
        public Texture2D warningImage;
        public Texture2D errorImage;
        public Texture2D barImage;
        public Texture2D buttonActiveImage;
        public Texture2D evenLogImage;
        public Texture2D oddLogImage;
        public Texture2D selectedImage;
        public GUISkin scrollerSkin;
    }
}
