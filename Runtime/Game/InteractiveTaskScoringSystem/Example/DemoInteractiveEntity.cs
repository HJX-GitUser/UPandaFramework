using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 示例：可交互的任务实体（一个方块）。
    /// 同时实现 TaskEntityBase（交互物本身）与 EntityOperationCheck（自定义判定条件），
    /// 演示「鼠标点击 → 操作检查 → 播放执行动画 → 回调完成」的完整闭环。
    /// </summary>
    public class DemoInteractiveEntity : TaskEntityBase, EntityOperationCheck
    {
        [Header("演示设置")]
        [Tooltip("鼠标悬停时的颜色")]
        public Color hoverColor = new Color(1f, 0.9f, 0.3f);
        [Tooltip("可交互（已激活）时的颜色")]
        public Color activeColor = new Color(0.3f, 0.9f, 1f);
        [Tooltip("执行动画时长（秒）")]
        public float executeDuration = 0.5f;
        [Tooltip("执行动画的弹起高度")]
        public float popHeight = 0.6f;

        private Renderer cachedRenderer;
        private MaterialPropertyBlock mpb;
        private Color baseColor = Color.white;
        private bool interactiveEnabled;

        protected override void Awake()
        {
            base.Awake();   // 基类会把自己注册到 TaskEntityManager
            cachedRenderer = GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            if (cachedRenderer != null && cachedRenderer.sharedMaterial != null)
            {
                baseColor = cachedRenderer.sharedMaterial.color;
            }
            SetColor(baseColor);
        }

        // ---------------- EntityOperationCheck：本次点击是否满足条件 ----------------
        /// <summary>
        /// 判定条件：点击的对象就是本实体。
        /// 真实项目里可以在这里判断工具型号、零件编号、朝向等（例如只有装了正确的工具才能拧螺丝）。
        /// </summary>
        public bool ConditionMet(TaskEntityBase arg)
        {
            //Debug.Log($"[示例] 操作检查：点击的对象是 {arg.name}，本实体是 {name}，判定结果：{(arg == this ? "通过" : "不通过")}");
            return arg == this;
        }

        // ---------------- InteractiveTrigger：交互反馈 ----------------
        public override void OnEnter() { SetColor(hoverColor); }

        public override void OnExit() { SetColor(interactiveEnabled ? activeColor : baseColor); }

        public override void OnStay() { }

        public override void OnSelectExit() { }

        public override void OnSelect()
        {
            if (IsPointerOverUI()) return;   // 防止点到 UI 时穿透触发
            base.OnSelect();                 // 交给 TaskDataManager 做操作检查
        }

        public override void EnableInteractive()
        {
            interactiveEnabled = true;
            SetColor(activeColor);
        }

        public override void DisableInteractive()
        {
            interactiveEnabled = false;
            SetColor(baseColor);
        }

        public override void EnableGuide()
        {
            // 引导：闪一下 + 日志提示（正式项目可换成描边、箭头或 UI 高亮）
            Debug.Log($"[示例] 请点击：{name}（步骤ID：{string.Join(" / ", StepIDGroup)}）");
            StartCoroutine(BlinkRoutine());
        }

        /// <summary>
        /// 操作检查通过后被调用：播放一个小动画，动画结束后再回调，演示"异步执行"。
        /// 注意：回调必须调用，否则整条任务链会卡在本步骤。
        /// </summary>
        public override void Execute(UnityAction callback)
        {
            StartCoroutine(ExecuteRoutine(callback));
        }

        public override void Skip()
        {
            Debug.Log($"[示例] 步骤被跳过：{name}");
            SetColor(baseColor);
        }

        private IEnumerator BlinkRoutine()
        {
            for (int i = 0; i < 3; i++)
            {
                SetColor(hoverColor);
                yield return new WaitForSeconds(0.15f);
                SetColor(activeColor);
                yield return new WaitForSeconds(0.15f);
            }
        }

        private IEnumerator ExecuteRoutine(UnityAction callback)
        {
            Vector3 start = transform.localPosition;
            Vector3 target = start + Vector3.up * popHeight;
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, executeDuration);
                transform.localPosition = Vector3.Lerp(start, target, Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI * 0.5f));
                transform.Rotate(Vector3.up, 180f * Time.deltaTime, Space.Self);
                yield return null;
            }
            SetColor(Color.green);
            yield return new WaitForSeconds(0.2f);
            transform.localPosition = start;
            DisableInteractive();
            callback?.Invoke();   // 通知操作检查：执行完毕，可以推进流程
        }

        private void SetColor(Color c)
        {
            if (cachedRenderer == null) return;
            cachedRenderer.GetPropertyBlock(mpb);
            mpb.SetColor("_Color", c);        // Built-in / Standard
            mpb.SetColor("_BaseColor", c);    // URP / HDRP
            cachedRenderer.SetPropertyBlock(mpb);
        }
    }
}
