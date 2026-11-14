using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 玩家键位覆盖层的 JSON 文件读写（输入系统自带的轻量持久化）。
    /// <para>文件内容为 <see cref="InputBindingOverlay"/>；默认路径
    /// <c>Application.persistentDataPath/Input/input-bindings.json</c>。
    /// 设置系统若有自己的存储/云同步，可以不用本类，直接保存 <see cref="InputBindingOverlay"/> JSON。</para>
    /// <para>写入使用“临时文件 + 备份替换”：主文件损坏时可回退 <c>.bak</c>。</para>
    /// </summary>
    public static class InputBindingOverlayStore
    {
        public const string DefaultFileName = "input-bindings.json";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>默认目录（persistentDataPath/Input）。</summary>
        public static string DefaultDirectory => Path.Combine(Application.persistentDataPath, "Input");

        /// <summary>默认文件路径（persistentDataPath/Input/input-bindings.json）。</summary>
        public static string DefaultPath => Path.Combine(DefaultDirectory, DefaultFileName);

        /// <summary>保存覆盖层；失败返回 false 并给出 error。</summary>
        public static bool Save(string path, InputBindingOverlay overlay, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path)) { error = "路径为空"; return false; }
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string json = InputBindingOverlay.ToJson(overlay ?? new InputBindingOverlay());
                string temp = path + ".tmp";
                string backup = path + ".bak";
                File.WriteAllText(temp, json, Utf8NoBom);
                if (File.Exists(path))
                {
                    try { File.Replace(temp, path, backup); }
                    catch (PlatformNotSupportedException) { File.Copy(path, backup, true); File.Copy(temp, path, true); }
                    catch (IOException) { File.Copy(path, backup, true); File.Copy(temp, path, true); }
                }
                else
                {
                    File.Move(temp, path);
                }
                if (File.Exists(temp)) File.Delete(temp);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 读取覆盖层。文件不存在返回 null 且 error=null；损坏时尝试 <c>.bak</c>，
        /// 两者都不可用返回 null 并给出 error。
        /// </summary>
        public static InputBindingOverlay Load(string path, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            var overlay = TryLoadFile(path, out error);
            if (overlay != null) return overlay;

            string backup = path + ".bak";
            if (File.Exists(backup))
            {
                var restored = TryLoadFile(backup, out var backupError);
                if (restored != null)
                {
                    error = "主文件读取失败，已回退备份：" + error;
                    return restored;
                }
                error = error + "；备份同样失败：" + backupError;
            }
            return null;
        }

        private static InputBindingOverlay TryLoadFile(string path, out string error)
        {
            error = null;
            try
            {
                return InputBindingOverlay.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return null;
            }
        }
    }
}
