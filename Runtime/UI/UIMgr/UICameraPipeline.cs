using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF
{
    /// <summary>当前激活的渲染管线类别（只按类型名判断，不硬依赖 URP/HDRP 程序集）。</summary>
    public enum UIRenderPipelineKind
    {
        BuiltIn,
        Universal,
        HighDefinition,
        Unknown
    }

    /// <summary>
    /// UI 相机的渲染管线适配：
    /// <para>Built-in 只保证相机 depth 在普通相机之后；URP 默认采用方案 B——把 UI 相机设为
    /// Overlay 并加入主相机 Camera Stack（无主相机时回退 Base）；HDRP 追加
    /// <c>HDAdditionalCameraData</c> 并开启 clearDepth。</para>
    /// <para>URP/HDRP 类型通过反射查找，因此包本身不引用 SRP 程序集：未安装对应管线时只记录一次警告，
    /// Built-in 工程照常工作。渲染管线在运行时切换（画质档位）后可再次调用 <see cref="Adapt"/>。</para>
    /// </summary>
    public static class UICameraPipeline
    {
        /// <summary>UI 相机默认 depth，与 UIManager 自建相机一致。</summary>
        public const int DefaultDepth = 10;
        /// <summary>URP UI 相机默认 priority。</summary>
        public const float DefaultPriority = 10f;

        private const string UrpDataType = "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData";
        private const string UrpAssembly = "Unity.RenderPipelines.Universal.Runtime";
        private const string HdrpDataType = "UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData";
        private const string HdrpAssembly = "Unity.RenderPipelines.HighDefinition.Runtime";

        private static Type urpCameraDataType;
        private static Type hdrpCameraDataType;
        private static bool warnedUrp;
        private static bool warnedHdrp;

        /// <summary>读取当前渲染管线；无 SRP 时为 Built-in。</summary>
        public static UIRenderPipelineKind Detect()
        {
            RenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline;
            if (asset != null)
            {
                UIRenderPipelineKind kind = Classify(asset.GetType().FullName);
                if (kind != UIRenderPipelineKind.Unknown) return kind;
            }

            RenderPipeline current = RenderPipelineManager.currentPipeline;
            if (current != null)
            {
                UIRenderPipelineKind kind = Classify(current.GetType().FullName);
                if (kind != UIRenderPipelineKind.Unknown) return kind;
            }
            return asset == null ? UIRenderPipelineKind.BuiltIn : UIRenderPipelineKind.Unknown;
        }

        /// <summary>
        /// 让 UI 相机适配当前/指定渲染管线。可重复调用；只补缺少的数据组件并提升 depth/priority，
        /// 不覆盖调用方设置的 clearFlags、cullingMask 或 targetTexture。
        /// </summary>
        public static void Adapt(Camera camera, UIRenderPipelineKind? pipeline = null,
            int minDepth = DefaultDepth, float priority = DefaultPriority)
        {
            if (camera == null) return;
            UIRenderPipelineKind kind = pipeline ?? Detect();

            if (camera.depth < minDepth) camera.depth = minDepth;
            float effectivePriority = Mathf.Max(priority, camera.depth);

            switch (kind)
            {
                case UIRenderPipelineKind.Universal:
                {
                    Component data = EnsureComponent(camera, ref urpCameraDataType, UrpDataType, UrpAssembly,
                        ref warnedUrp, "UniversalAdditionalCameraData", "URP");
                    if (data != null)
                    {
                        // 预算好的 Overlay 相机（已加入主相机 Camera Stack）保持不变：
                        // 只由 UIManager 管理 Canvas，不强行改回 Base 或清空 stack。
                        object renderType = GetMember(data, "renderType");
                        bool isOverlay = renderType != null &&
                                         string.Equals(renderType.ToString(), "Overlay", StringComparison.OrdinalIgnoreCase);
                        if (!isOverlay)
                        {
                            SetMember(data, "renderType", "Base");
                            SetMember(data, "priority", effectivePriority);
                            SetMember(data, "clearDepth", true);
                        }
                    }
                    break;
                }
                case UIRenderPipelineKind.HighDefinition:
                {
                    Component data = EnsureComponent(camera, ref hdrpCameraDataType, HdrpDataType, HdrpAssembly,
                        ref warnedHdrp, "HDAdditionalCameraData", "HDRP");
                    if (data != null) SetMember(data, "clearDepth", true);
                    break;
                }
            }
        }

        /// <summary>相机上是否已有 URP 的 AdditionalCameraData（未安装 URP 时为 false）。</summary>
        public static bool HasUniversalAdditionalData(Camera camera)
        {
            if (camera == null) return false;
            Type type = ResolveType(ref urpCameraDataType, UrpDataType, UrpAssembly);
            return type != null && camera.GetComponent(type) != null;
        }

        /// <summary>
        /// 方案 B：把 UI 相机设为 Overlay 并加入主相机的 Camera Stack。
        /// 只有当前管线是 URP 时生效；返回 false 时调用方应回退到 Base 方案。
        /// </summary>
        public static bool ConfigureOverlay(Camera baseCamera, Camera overlayCamera)
        {
            if (baseCamera == null || overlayCamera == null || baseCamera == overlayCamera) return false;
            if (Detect() != UIRenderPipelineKind.Universal) return false;

            Component baseData = EnsureComponent(baseCamera, ref urpCameraDataType, UrpDataType, UrpAssembly,
                ref warnedUrp, "UniversalAdditionalCameraData", "URP");
            Component overlayData = EnsureComponent(overlayCamera, ref urpCameraDataType, UrpDataType, UrpAssembly,
                ref warnedUrp, "UniversalAdditionalCameraData", "URP");
            if (baseData == null || overlayData == null) return false;

            SetMember(baseData, "renderType", "Base");
            SetMember(overlayData, "renderType", "Overlay");
            SetMember(overlayData, "clearDepth", false);
            SetMember(overlayData, "renderPostProcessing", false);

            IList stack = GetCameraStack(baseData);
            if (stack == null) return false;
            if (!stack.Contains(overlayCamera)) stack.Add(overlayCamera);
            return true;
        }

        /// <summary>从指定主相机的 Camera Stack 移除 Overlay；已不在 stack 中时也返回 true。</summary>
        public static bool RemoveFromStack(Camera baseCamera, Camera overlayCamera)
        {
            if (baseCamera == null || overlayCamera == null) return false;
            Type type = ResolveType(ref urpCameraDataType, UrpDataType, UrpAssembly);
            if (type == null) return false;

            Component baseData = baseCamera.GetComponent(type);
            if (baseData == null) return false;
            IList stack = GetCameraStack(baseData);
            if (stack == null) return false;

            while (stack.Contains(overlayCamera)) stack.Remove(overlayCamera);
            return true;
        }

        /// <summary>方案 B 不可用（无主相机/未安装 URP）时回退为 Base 相机。</summary>
        public static bool MakeBase(Camera camera)
        {
            if (camera == null) return false;
            Component data = EnsureComponent(camera, ref urpCameraDataType, UrpDataType, UrpAssembly,
                ref warnedUrp, "UniversalAdditionalCameraData", "URP");
            if (data == null) return false;

            SetMember(data, "renderType", "Base");
            SetMember(data, "priority", Mathf.Max(DefaultPriority, camera.depth));
            SetMember(data, "clearDepth", true);
            return true;
        }

        private static IList GetCameraStack(Component baseData)
        {
            return GetMember(baseData, "cameraStack") as IList;
        }

        private static UIRenderPipelineKind Classify(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return UIRenderPipelineKind.Unknown;
            if (fullName.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return UIRenderPipelineKind.Universal;
            if (fullName.IndexOf("HighDefinition", StringComparison.OrdinalIgnoreCase) >= 0)
                return UIRenderPipelineKind.HighDefinition;
            return UIRenderPipelineKind.Unknown;
        }

        private static Component EnsureComponent(Camera camera, ref Type cached, string typeName,
            string assemblyName, ref bool warned, string componentName, string pipelineName)
        {
            Type type = ResolveType(ref cached, typeName, assemblyName);
            if (type == null)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[UICameraPipeline] 当前工程使用 " + pipelineName +
                                     "，但找不到 " + componentName + " 类型；UI 相机未追加管线数据组件。");
                }
                return null;
            }

            Component existing = camera.GetComponent(type);
            if (existing != null) return existing;

            try { return camera.gameObject.AddComponent(type); }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Debug.LogWarning("[UICameraPipeline] 为 UI 相机添加 " + componentName + " 失败：" + e.Message);
                }
                return null;
            }
        }

        private static Type ResolveType(ref Type cached, string fullName, string assemblyName)
        {
            if (cached != null) return cached;

            Type type = Type.GetType(fullName + ", " + assemblyName);
            if (type == null)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length && type == null; i++)
                {
                    try { type = assemblies[i].GetType(fullName, false); }
                    catch (Exception) { /* 动态程序集/反射异常时继续找。 */ }
                }
            }

            if (type != null) cached = type;
            return type;
        }

        private static object GetMember(Component component, string name)
        {
            if (component == null) return null;
            Type type = component.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanRead) return property.GetValue(component, null);
                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                return field != null ? field.GetValue(component) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void SetMember(Component component, string name, object value)
        {
            if (component == null) return;
            Type type = component.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(component, Convert(value, property.PropertyType), null);
                    return;
                }

                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field != null) field.SetValue(component, Convert(value, field.FieldType));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UICameraPipeline] 设置 " + type.Name + "." + name + " 失败：" + e.Message);
            }
        }

        private static object Convert(object value, Type targetType)
        {
            if (value == null) return null;
            if (targetType.IsInstanceOfType(value)) return value;
            if (targetType.IsEnum) return Enum.Parse(targetType, value.ToString(), true);
            return System.Convert.ChangeType(value, targetType);
        }
    }
}
