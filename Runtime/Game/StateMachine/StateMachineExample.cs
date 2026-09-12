using UnityEngine;

namespace UPandaGF.StateMechine
{
    // ============================================================
    // 状态机使用案例
    // ------------------------------------------------------------
    // 演示一个「玩家」分层状态机的完整用法：
    //   Player（根）
    //     ├─ Idle        待机
    //     ├─ Move        移动（包含子状态）
    //     │   ├─ Walk    走路
    //     │   └─ Run     跑步
    //     └─ Attack      攻击
    //
    // 把本脚本挂到场景任意空物体上，进入 Play 模式后：
    //   数字键 1 → 待机（单级切换）
    //   数字键 2 → 移动（单级切换，进入默认子状态 Walk）
    //   数字键 3 → 跑步（多级路径切换 "Move/Run"）
    //   数字键 4 → 攻击（单级切换）
    //   B 键    → 返回上一级状态（GoBack）
    //   T 键    → 打印状态树（调试）
    // ============================================================

    /// <summary>玩家根状态</summary>
    public class PlayerState : BaseState
    {
        public override string StateID => "Player";
        public override void OnEnter() { Debug.Log("[状态] 进入 Player"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Player"); }
    }

    /// <summary>待机状态</summary>
    public class IdleState : BaseState
    {
        public override string StateID => "Idle";
        public override void OnEnter() { Debug.Log("[状态] 进入 Idle"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Idle"); }
    }

    /// <summary>移动状态（包含 Walk / Run 两个子状态）</summary>
    public class MoveState : BaseState
    {
        public override string StateID => "Move";
        public override void OnEnter() { Debug.Log("[状态] 进入 Move"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Move"); }
    }

    /// <summary>走路状态</summary>
    public class WalkState : BaseState
    {
        public override string StateID => "Walk";
        public override void OnEnter() { Debug.Log("[状态] 进入 Walk"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Walk"); }
    }

    /// <summary>跑步状态</summary>
    public class RunState : BaseState
    {
        public override string StateID => "Run";
        public override void OnEnter() { Debug.Log("[状态] 进入 Run"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Run"); }
    }

    /// <summary>攻击状态</summary>
    public class AttackState : BaseState
    {
        public override string StateID => "Attack";
        public override void OnEnter() { Debug.Log("[状态] 进入 Attack"); }
        public override void OnExit() { Debug.Log("[状态] 退出 Attack"); }
    }

    /// <summary>
    /// 状态机使用示例：演示注册、切换、返回、事件监听与调试打印。
    /// 挂载到场景任意空物体上即可运行。
    /// </summary>
    public class StateMachineExample : MonoBehaviour
    {
        private StateMachineManager manager;

        private PlayerState player;
        private MoveState move;

        void Start()
        {
            // 1. 获取或创建管理器（与示例挂在同一物体）
            manager = GetComponent<StateMachineManager>();
            if (manager == null) manager = gameObject.AddComponent<StateMachineManager>();

            // 2. 监听状态改变事件（参数：旧路径 -> 新路径）
            manager.OnStateChanged += (oldPath, newPath) =>
                Debug.Log($"状态切换: {oldPath} -> {newPath}");

            // 3. 注册状态并建立层级（先注册根，再注册子）
            player = new PlayerState();
            manager.RegisterState(player);                    // 根状态

            manager.RegisterState(new IdleState(), "Player"); // Player 的子状态
            move = new MoveState();
            manager.RegisterState(move, "Player");
            manager.RegisterState(new AttackState(), "Player");

            manager.RegisterState(new WalkState(), "Move");   // Move 的子状态
            manager.RegisterState(new RunState(), "Move");

            // 4. 设置默认子状态（注意：SetDefaultChild 只在下次 OnEnter 时生效）
            player.SetDefaultChild("Idle");
            move.SetDefaultChild("Walk");

            // 5. 手动激活初始子状态（演示直接切换子状态）
            player.SwitchToChild("Idle");

            Debug.Log(manager.GetStateInfo());
        }

        void Update()
        {
            if (manager == null) return;

            // 键盘演示不同的切换方式
            if (Input.GetKeyDown(KeyCode.Alpha1)) manager.SwitchState("Idle");     // 单级切换
            if (Input.GetKeyDown(KeyCode.Alpha2)) manager.SwitchState("Move");     // 单级切换（进入默认 Walk）
            if (Input.GetKeyDown(KeyCode.Alpha3)) manager.SwitchState("Move/Run"); // 多级路径切换
            if (Input.GetKeyDown(KeyCode.Alpha4)) manager.SwitchState("Attack");   // 单级切换
            if (Input.GetKeyDown(KeyCode.B)) manager.GoBack();                     // 返回上一级
            if (Input.GetKeyDown(KeyCode.T)) Debug.Log(manager.GetStateInfo());    // 打印状态树
        }
    }
}
