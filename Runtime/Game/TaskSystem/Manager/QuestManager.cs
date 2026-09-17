using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 任务系统核心（单例）。
    ///
    /// 职责：
    ///   1. 从 Resources/Quests 加载所有 QuestDefinition（模板）；
    ///   2. 为每个模板建立 QuestInstance（运行态，进度与状态都在这里）；
    ///   3. 订阅 GameplayEventBus，把"击杀/拾取/到达/对话"事件推进到匹配的目标上；
    ///   4. 维护状态机（Locked → Available → Active → Completed → TurnedIn）与前置任务解锁；
    ///   5. 提交任务时通过 IRewardReceiver 发奖励；
    ///   6. JSON 存档 / 读档。
    ///
    /// 本类没有任何 Update 轮询：所有进度变化都由事件驱动，状态变化再通过 C# 事件通知 UI。
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestManager : MonoBehaviour
    {
        /// <summary>默认存档文件名（位于 Application.persistentDataPath）。</summary>
        public const string DefaultSaveFileName = "quests.json";

        private static QuestManager instance;
        private static bool isQuitting;

        /// <summary>是否允许在第一次访问时自动创建 QuestManager（默认允许，方便"零配置"接入）。</summary>
        public static bool AutoCreateInstance = true;

        /// <summary>单例入口：场景里没有会自动创建一个（DontDestroyOnLoad）。</summary>
        public static QuestManager Instance
        {
            get
            {
                if (instance != null) return instance;
                if (isQuitting || !Application.isPlaying) return null;   // 编辑期 / 退出中不自动创建
                instance = FindObjectOfType<QuestManager>();
                if (instance == null && AutoCreateInstance)
                {
                    GameObject go = new GameObject("QuestManager");
                    instance = go.AddComponent<QuestManager>();          // Awake 会完成初始化
                }
                return instance;
            }
        }

        /// <summary>只读取实例：不会主动创建（编辑期 / 退出中返回 null），UI 脚本安全使用。</summary>
        public static QuestManager InstanceOrNull
        {
            get
            {
                if (instance != null) return instance;
                if (isQuitting || !Application.isPlaying) return null;
                return FindObjectOfType<QuestManager>();
            }
        }

        [Header("启动行为")]
        [Tooltip("进入游戏时自动加载 Resources 下的任务模板。")]
        public bool initializeOnAwake = true;
        [Tooltip("进入游戏时自动读取本地存档。")]
        public bool autoLoadSaveOnAwake = false;

        [Header("HUD")]
        [Tooltip("HUD 最多同时追踪多少个活跃任务。")]
        public int maxTrackedQuests = 3;

        // ------------------------------ 运行时数据 ------------------------------

        private readonly Dictionary<string, QuestDefinition> definitions = new Dictionary<string, QuestDefinition>();
        private readonly Dictionary<string, QuestInstance> instances = new Dictionary<string, QuestInstance>();

        /// <summary>复用容器，避免每次上报都分配 List。仅供内部单帧使用。</summary>
        private readonly List<QuestObjectiveProgress> changedObjectiveBuffer = new List<QuestObjectiveProgress>();
        private readonly List<QuestInstance> instanceBuffer = new List<QuestInstance>();
        private readonly List<QuestInstance> trackedBuffer = new List<QuestInstance>();

        private int acceptCounter;
        private bool initialized;
        private bool isSubscribed;

        /// <summary>奖励发放目标；为空时自动使用 PlayerRewardService。</summary>
        public IRewardReceiver RewardReceiver { get; set; }

        // -------------------------------- 对外事件 --------------------------------

        /// <summary>任务状态变化（接受 / 完成 / 提交 / 解锁）。</summary>
        public event Action<QuestInstance> OnQuestStatusChanged;

        /// <summary>某个目标的进度变化。UI 只需刷新对应条目。</summary>
        public event Action<QuestInstance, QuestObjectiveProgress> OnObjectiveProgressChanged;

        /// <summary>任务目标全部达成（等待提交）。</summary>
        public event Action<QuestInstance> OnQuestCompleted;

        /// <summary>任务已提交、奖励已发放。</summary>
        public event Action<QuestInstance> OnQuestTurnedIn;

        /// <summary>任务列表结构变化（新增解锁 / 接受 / 提交 / 追踪状态变化）——面板需要整表刷新。</summary>
        public event Action OnQuestLogChanged;

        // ================================ 生命周期 ================================

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("[QuestManager] 场景中存在多个 QuestManager，销毁重复的：" + name);
                Destroy(gameObject);
                return;     // 注意：这里必须 return，否则重复实例也会执行初始化
            }

            instance = this;
            if (Application.isPlaying && transform.parent == null) DontDestroyOnLoad(gameObject);

            if (initializeOnAwake) EnsureInitialized();
        }

        private void OnEnable()
        {
            if (instance != this) return;      // 被销毁的重复实例不订阅，避免进度重复累加
            SubscribeEventBus();
        }

        private void OnDisable()
        {
            UnsubscribeEventBus();
        }

        private void OnDestroy()
        {
            UnsubscribeEventBus();
            if (instance == this) instance = null;
        }

        private void OnApplicationQuit()
        {
            isQuitting = true;
        }

        // ============================ 事件总线订阅（入站） ============================

        private void SubscribeEventBus()
        {
            if (isSubscribed) return;
            isSubscribed = true;
            GameplayEventBus.EnemyKilled += ReportKill;
            GameplayEventBus.ItemCollected += ReportCollect;
            GameplayEventBus.LocationReached += ReportReachLocation;
            GameplayEventBus.NpcTalked += ReportTalkToNPC;
        }

        private void UnsubscribeEventBus()
        {
            if (!isSubscribed) return;
            isSubscribed = false;
            GameplayEventBus.EnemyKilled -= ReportKill;
            GameplayEventBus.ItemCollected -= ReportCollect;
            GameplayEventBus.LocationReached -= ReportReachLocation;
            GameplayEventBus.NpcTalked -= ReportTalkToNPC;
        }

        // ================================ 初始化 ================================

        /// <summary>确保模板与实例已就绪（所有对外 API 都会先调用它，可安全重复调用）。</summary>
        public void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;     // 先置位，避免 LoadDefinitions/EvaluateAvailability 内部重入
            LoadDefinitions();
            RebuildInstances();
        }

        /// <summary>重新加载模板并重建全部实例（会丢失当前进度，慎用）。</summary>
        public void Initialize(bool forceReload = false)
        {
            if (forceReload) initialized = false;
            EnsureInitialized();
        }

        private void LoadDefinitions()
        {
            definitions.Clear();

            QuestDefinition[] loaded = Resources.LoadAll<QuestDefinition>(QuestDefinition.ResourceFolder);
            for (int i = 0; i < loaded.Length; i++)
            {
                QuestDefinition definition = loaded[i];
                if (definition == null) continue;

                string questId = definition.GetQuestId();
                if (string.IsNullOrEmpty(questId))
                {
                    Debug.LogWarning("[QuestManager] 跳过没有 ID 的任务资产：" + definition.name);
                    continue;
                }
                if (definitions.ContainsKey(questId))
                {
                    Debug.LogWarning("[QuestManager] questId 重复，跳过：" + questId + "（" + definition.name + "）");
                    continue;
                }

                List<string> errors = new List<string>();
                if (!definition.ValidateQuest(errors))
                {
                    Debug.LogWarning("[QuestManager] 任务 " + questId + " 配置有问题：" + string.Join("；", errors));
                }

                definitions.Add(questId, definition);
            }

            Debug.Log("[QuestManager] 已加载任务模板 " + definitions.Count + " 个（Resources/" + QuestDefinition.ResourceFolder + "）");
        }

        /// <summary>按当前模板重建全部实例（清空进度）。</summary>
        private void RebuildInstances()
        {
            instances.Clear();
            acceptCounter = 0;

            foreach (KeyValuePair<string, QuestDefinition> pair in definitions)
            {
                instances.Add(pair.Key, new QuestInstance(pair.Value));
            }

            EvaluateAvailability();
        }

        /// <summary>运行期动态注册任务模板（测试 / 程序化生成任务时用）。</summary>
        public void AddOrReplaceDefinition(QuestDefinition definition)
        {
            if (definition == null) return;
            string questId = definition.GetQuestId();
            if (string.IsNullOrEmpty(questId))
            {
                Debug.LogWarning("[QuestManager] 注册失败：任务没有 ID。");
                return;
            }

            EnsureInitialized();
            definitions[questId] = definition;
            instances[questId] = new QuestInstance(definition);
            EvaluateAvailability();
            RaiseQuestLogChanged();
        }

        // ================================ 查询接口 ================================

        public int DefinitionCount { get { return definitions.Count; } }
        public int InstanceCount { get { return instances.Count; } }

        public QuestInstance GetQuest(string questId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(questId)) return null;
            QuestInstance found;
            return instances.TryGetValue(questId, out found) ? found : null;
        }

        public QuestDefinition GetDefinition(string questId)
        {
            EnsureInitialized();
            QuestDefinition found;
            if (!string.IsNullOrEmpty(questId) && definitions.TryGetValue(questId, out found)) return found;
            return null;
        }

        /// <summary>全部任务（按类型、再按接受顺序排序）。返回的是新列表，可安全遍历。</summary>
        public List<QuestInstance> GetAllQuests()
        {
            EnsureInitialized();
            List<QuestInstance> result = new List<QuestInstance>(instances.Values);
            result.Sort(CompareQuest);
            return result;
        }

        public List<QuestInstance> GetQuestsByStatus(QuestStatus status)
        {
            EnsureInitialized();
            List<QuestInstance> result = new List<QuestInstance>();
            foreach (QuestInstance quest in instances.Values)
            {
                if (quest.status == status) result.Add(quest);
            }
            result.Sort(CompareQuest);
            return result;
        }

        /// <summary>所有进行中的任务（按接受时间排序）。</summary>
        public List<QuestInstance> GetActiveQuests()
        {
            return GetQuestsByStatus(QuestStatus.Active);
        }

        /// <summary>被追踪的活跃任务（HUD 用，最多 maxTrackedQuests 个）。</summary>
        public List<QuestInstance> GetTrackedQuests()
        {
            EnsureInitialized();
            trackedBuffer.Clear();
            foreach (QuestInstance quest in instances.Values)
            {
                if (quest.isTracked && quest.status == QuestStatus.Active) trackedBuffer.Add(quest);
            }
            trackedBuffer.Sort(CompareQuest);
            if (trackedBuffer.Count > maxTrackedQuests)
            {
                trackedBuffer.RemoveRange(maxTrackedQuests, trackedBuffer.Count - maxTrackedQuests);
            }
            return new List<QuestInstance>(trackedBuffer);
        }

        /// <summary>排序：进行中 > 可提交 > 可接受 > 其它；同组按接受顺序。</summary>
        private static int CompareQuest(QuestInstance a, QuestInstance b)
        {
            int weightA = GetStatusWeight(a.status);
            int weightB = GetStatusWeight(b.status);
            if (weightA != weightB) return weightA.CompareTo(weightB);
            return a.acceptedOrder.CompareTo(b.acceptedOrder);
        }

        private static int GetStatusWeight(QuestStatus status)
        {
            switch (status)
            {
                case QuestStatus.Active: return 0;
                case QuestStatus.Completed: return 1;
                case QuestStatus.Available: return 2;
                case QuestStatus.TurnedIn: return 3;
                default: return 4;   // Locked
            }
        }

        // ================================ 状态推进 ================================

        /// <summary>
        /// 前置任务是否都已满足。
        /// 判定标准：前置任务处于「已完成（目标达成）」或「已提交」状态。
        /// </summary>
        public bool ArePrerequisitesMet(QuestDefinition definition)
        {
            if (definition == null || definition.prerequisites == null) return true;

            for (int i = 0; i < definition.prerequisites.Count; i++)
            {
                string prerequisiteId = definition.prerequisites[i];
                if (string.IsNullOrEmpty(prerequisiteId)) continue;

                QuestInstance prerequisite = GetQuest(prerequisiteId);
                if (prerequisite == null)
                {
                    Debug.LogWarning("[QuestManager] 任务 " + definition.GetQuestId() + " 的前置 " + prerequisiteId + " 不存在。");
                    return false;
                }
                if (prerequisite.status != QuestStatus.Completed && prerequisite.status != QuestStatus.TurnedIn) return false;
            }
            return true;
        }

        /// <summary>
        /// 重新评估所有锁定任务：前置满足 → 变为可接受；autoStart 的任务直接接受。
        /// 每次任务提交后都会调用，用于解锁任务链的下一环。
        /// </summary>
        public void EvaluateAvailability()
        {
            bool changed = false;

            foreach (QuestInstance quest in instances.Values)
            {
                if (quest.status != QuestStatus.Locked) continue;
                if (!ArePrerequisitesMet(quest.definition)) continue;

                SetStatus(quest, QuestStatus.Available);
                changed = true;

                if (quest.definition.autoStart) AcceptQuest(quest.QuestId);
            }

            // 有解锁就通知一次，UI 无需自己关心何时重新评估
            if (changed) RaiseQuestLogChanged();
        }

        /// <summary>接受任务。只有「可接受」（或可重复任务已提交/已完成）时才成功。</summary>
        public bool AcceptQuest(string questId)
        {
            EnsureInitialized();

            QuestInstance quest = GetQuest(questId);
            if (quest == null)
            {
                Debug.LogWarning("[QuestManager] 接受失败：找不到任务 " + questId);
                return false;
            }
            if (quest.status == QuestStatus.Active || quest.status == QuestStatus.Completed)
            {
                return false;
            }
            if (quest.status == QuestStatus.TurnedIn && !quest.IsRepeatable) return false;
            if (quest.status == QuestStatus.Locked && !ArePrerequisitesMet(quest.definition))
            {
                Debug.LogWarning("[QuestManager] 接受失败：前置任务未完成 " + questId);
                return false;
            }

            quest.ResetProgress();                 // 每次接受都从零开始（可重复任务靠这句重置）
            if (quest.status != QuestStatus.Active) quest.acceptedOrder = ++acceptCounter;
            SetStatus(quest, QuestStatus.Active);
            AutoTrack(quest);
            RaiseQuestLogChanged();
            return true;
        }

        /// <summary>提交任务：发奖励 → 置为已提交；可重复任务会重新变为可接受。</summary>
        public bool TurnInQuest(string questId)
        {
            EnsureInitialized();

            QuestInstance quest = GetQuest(questId);
            if (quest == null)
            {
                Debug.LogWarning("[QuestManager] 提交失败：找不到任务 " + questId);
                return false;
            }
            if (quest.status != QuestStatus.Completed)
            {
                Debug.LogWarning("[QuestManager] 提交失败：任务尚有目标未完成 " + questId);
                return false;
            }

            GrantRewards(quest);
            quest.completionCount++;

            SetStatus(quest, QuestStatus.TurnedIn);
            if (OnQuestTurnedIn != null) OnQuestTurnedIn(quest);

            if (quest.IsRepeatable)
            {
                // 可重复（日常）任务：立刻回到可接受状态并清零进度。
                // 想做"每天刷新"的话，在这里判断日期再接 AcceptQuest 即可。
                quest.ResetProgress();
                SetStatus(quest, QuestStatus.Available);
            }

            EvaluateAvailability();     // 解锁任务链的下一环
            RaiseQuestLogChanged();
            return true;
        }

        /// <summary>设置 / 取消 HUD 追踪。</summary>
        public bool TrackQuest(string questId, bool tracked)
        {
            EnsureInitialized();
            QuestInstance quest = GetQuest(questId);
            if (quest == null) return false;
            if (quest.isTracked == tracked) return true;

            quest.isTracked = tracked;
            RaiseQuestLogChanged();
            return true;
        }

        private void AutoTrack(QuestInstance quest)
        {
            if (quest.isTracked) return;
            if (GetTrackedQuests().Count >= Mathf.Max(1, maxTrackedQuests)) return;
            quest.isTracked = true;
        }

        private void SetStatus(QuestInstance quest, QuestStatus status)
        {
            if (quest == null || quest.status == status) return;
            quest.status = status;
            if (OnQuestStatusChanged != null) OnQuestStatusChanged(quest);
        }

        private void RaiseQuestLogChanged()
        {
            if (OnQuestLogChanged != null) OnQuestLogChanged();
        }

        // ============================ 进度上报（入站核心） ============================

        /// <summary>
        /// 统一进度入口：把所有进行中任务的匹配目标推进 amount。
        /// 事件总线上的四个方法最终都会走到这里。
        /// </summary>
        public void Report(ObjectiveType type, string targetId, int amount = 1)
        {
            EnsureInitialized();
            if (amount <= 0) return;

            // 拷贝一份实例列表再遍历：任务在遍历中可能因完成而改变状态，直接遍历字典值集不安全
            instanceBuffer.Clear();
            instanceBuffer.AddRange(instances.Values);

            bool anyChanged = false;
            for (int i = 0; i < instanceBuffer.Count; i++)
            {
                QuestInstance quest = instanceBuffer[i];
                if (quest.status != QuestStatus.Active) continue;

                changedObjectiveBuffer.Clear();
                quest.ApplyProgress(type, targetId, amount, changedObjectiveBuffer);
                if (changedObjectiveBuffer.Count == 0) continue;

                anyChanged = true;

                // 逐个目标派发进度事件（UI 只刷新变化的那一行）
                for (int j = 0; j < changedObjectiveBuffer.Count; j++)
                {
                    if (OnObjectiveProgressChanged != null) OnObjectiveProgressChanged(quest, changedObjectiveBuffer[j]);
                }

                // 所有目标达成 → 置为「可提交」
                if (quest.AreAllObjectivesCompleted)
                {
                    SetStatus(quest, QuestStatus.Completed);
                    Debug.Log("[QuestManager] 任务完成，等待提交：" + quest.Title);
                    if (OnQuestCompleted != null) OnQuestCompleted(quest);
                }
            }

            if (anyChanged) RaiseQuestLogChanged();
        }

        /// <summary>击杀上报。敌人死亡时调用一行即可：GameplayEventBus.RaiseEnemyKilled("wolf")。</summary>
        public void ReportKill(string enemyId, int amount = 1) { Report(ObjectiveType.Kill, enemyId, amount); }

        /// <summary>拾取上报。</summary>
        public void ReportCollect(string itemId, int amount = 1) { Report(ObjectiveType.Collect, itemId, amount); }

        /// <summary>到达地点上报。</summary>
        public void ReportReachLocation(string locationId) { Report(ObjectiveType.ReachLocation, locationId, 1); }

        /// <summary>与 NPC 对话上报。</summary>
        public void ReportTalkToNPC(string npcId) { Report(ObjectiveType.TalkToNPC, npcId, 1); }

        // ================================ 奖励发放 ================================

        /// <summary>把任务奖励交给 IRewardReceiver（默认 PlayerRewardService）。</summary>
        public void GrantRewards(QuestInstance quest)
        {
            if (quest == null || quest.Reward == null) return;

            IRewardReceiver receiver = RewardReceiver;
            if (receiver == null) receiver = PlayerRewardService.Instance;   // 默认发给演示钱包
            if (receiver == null)
            {
                Debug.LogWarning("[QuestManager] 没有奖励接收端，奖励未发放：" + quest.Title);
                return;
            }

            QuestReward reward = quest.Reward;
            if (reward.experience > 0) receiver.GrantExperience(reward.experience);
            if (reward.gold > 0) receiver.GrantGold(reward.gold);

            if (reward.items != null)
            {
                for (int i = 0; i < reward.items.Count; i++)
                {
                    ItemStack item = reward.items[i];
                    if (item.IsValid) receiver.GrantItem(item.itemId, item.amount);
                }
            }
        }

        // ================================ 存档 / 读档 ================================

        /// <summary>序列化当前进度为 JSON 字符串。</summary>
        public string SaveToJson(bool pretty = true)
        {
            EnsureInitialized();

            QuestSaveData data = new QuestSaveData();
            data.version = QuestSaveData.CurrentVersion;
            data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (QuestInstance quest in instances.Values)
            {
                data.quests.Add(quest.ToSaveRecord());
            }

            return pretty ? JsonUtility.ToJson(data, true) : JsonUtility.ToJson(data, false);
        }

        /// <summary>
        /// 从 JSON 字符串恢复进度。
        /// 会先把所有任务归零再套用存档，保证"读档后进度 == 存档内容"（不会残留旧进度）。
        /// </summary>
        public bool LoadFromJson(string json)
        {
            EnsureInitialized();

            if (string.IsNullOrEmpty(json))
            {
                Debug.LogWarning("[QuestManager] 读档失败：内容为空。");
                return false;
            }

            QuestSaveData data;
            try
            {
                data = JsonUtility.FromJson<QuestSaveData>(json);
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestManager] 读档失败：JSON 解析异常 " + exception.Message);
                return false;
            }

            if (data == null || data.quests == null)
            {
                Debug.LogWarning("[QuestManager] 读档失败：数据结构不匹配。");
                return false;
            }

            // 1) 全部归零（状态 / 进度 / 追踪 / 计数）
            acceptCounter = 0;
            foreach (QuestInstance quest in instances.Values)
            {
                quest.ResetProgress();
                quest.isTracked = false;
                quest.acceptedOrder = 0;
                quest.completionCount = 0;
                SetStatus(quest, QuestStatus.Locked);
            }

            // 2) 套用存档
            for (int i = 0; i < data.quests.Count; i++)
            {
                QuestSaveRecord record = data.quests[i];
                if (record == null || string.IsNullOrEmpty(record.questId)) continue;

                QuestInstance quest = GetQuest(record.questId);
                if (quest == null)
                {
                    Debug.LogWarning("[QuestManager] 存档里有未知任务，已忽略：" + record.questId);
                    continue;
                }

                quest.ApplySaveRecord(record);
                if (quest.acceptedOrder > acceptCounter) acceptCounter = quest.acceptedOrder;
            }

            // 3) 重新评估解锁（存档里是 Locked 但现在前置已满足的，会重新变为可接受）
            EvaluateAvailability();
            RaiseQuestLogChanged();
            Debug.Log("[QuestManager] 读档完成，共 " + data.quests.Count + " 条任务记录。");
            return true;
        }

        /// <summary>存档文件完整路径。</summary>
        public string GetSaveFilePath(string fileName = null)
        {
            if (string.IsNullOrEmpty(fileName)) fileName = DefaultSaveFileName;
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        public bool HasSaveFile(string fileName = null)
        {
            return File.Exists(GetSaveFilePath(fileName));
        }

        /// <summary>写档到 persistentDataPath（WebGL 不支持本地文件，请改用 SaveToJson + 平台存储）。</summary>
        public bool SaveToFile(string fileName = null)
        {
            string path = GetSaveFilePath(fileName);
            try
            {
                File.WriteAllText(path, SaveToJson(), new UTF8Encoding(false));
                Debug.Log("[QuestManager] 已存档：" + path);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestManager] 存档失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>从 persistentDataPath 读档；文件不存在时返回 false。</summary>
        public bool LoadFromFile(string fileName = null)
        {
            string path = GetSaveFilePath(fileName);
            try
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning("[QuestManager] 读档失败：找不到文件 " + path);
                    return false;
                }
                return LoadFromJson(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestManager] 读档失败：" + exception.Message);
                return false;
            }
        }

        public bool DeleteSaveFile(string fileName = null)
        {
            string path = GetSaveFilePath(fileName);
            try
            {
                if (!File.Exists(path)) return false;
                File.Delete(path);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[QuestManager] 删除存档失败：" + exception.Message);
                return false;
            }
        }

        // ================================ 重置 / 调试 ================================

        /// <summary>把所有任务恢复到初始状态（全部重新评估解锁）。</summary>
        public void ResetAllProgress()
        {
            EnsureInitialized();
            acceptCounter = 0;

            foreach (QuestInstance quest in instances.Values)
            {
                quest.ResetProgress();
                quest.isTracked = false;
                quest.acceptedOrder = 0;
                quest.completionCount = 0;
                SetStatus(quest, QuestStatus.Locked);
            }

            EvaluateAvailability();
            RaiseQuestLogChanged();
            Debug.Log("[QuestManager] 已重置所有任务进度。");
        }

        /// <summary>一行文本快照，方便调试面板 / 日志输出。</summary>
        public string GetDebugSummary()
        {
            EnsureInitialized();
            List<QuestInstance> all = GetAllQuests();
            StringBuilder builder = new StringBuilder();
            builder.Append("任务总数 ").Append(all.Count).Append("：");
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0) builder.Append(" | ");
                builder.Append(all[i].Title)
                       .Append("[").Append(QuestText.GetStatusName(all[i].status)).Append(" ")
                       .Append(all[i].GetProgressSummary()).Append("]");
            }
            return builder.ToString();
        }
    }
}
