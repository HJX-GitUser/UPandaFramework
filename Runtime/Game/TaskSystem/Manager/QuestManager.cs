using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UPandaGF.Gameplay.Tasks
{
    /// <summary>
    /// 任务系统核心。
    ///
    /// 职责：
    ///   1. 加载 QuestDefinition 模板（默认 Resources.LoadAll，可通过 DefinitionLoader 替换）；
    ///   2. 为每个模板建立 QuestInstance（运行态，进度与状态都在这里）；
    ///   3. 订阅 GameplayEventBus，把"击杀/拾取/到达/对话"事件推进到匹配的目标上；
    ///   4. 维护状态机（Locked → Available → Active → Completed → TurnedIn）与前置任务解锁；
    ///   5. 提交任务时通过 IRewardReceiver 发奖励（接收端必须显式赋值）；
    ///   6. 通过注入的 ISaveService 存档 / 读档（槽位制，不再自己写 json 文件）。
    ///
    /// 本类没有任何 Update 轮询：所有进度变化都由事件驱动，状态变化再通过 C# 事件通知 UI。
    /// <para>接入方式：把组件放进场景（QuestDemoInstaller 会自动放），根节点在场景加载后注入服务；
    /// 不再提供静态 Instance，也不会自动创建。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class QuestManager : MonoBehaviour, IGameServicesConsumer
    {
        /// <summary>存档服务中的默认槽位名（不再使用 quests.json 文件）。</summary>
        public const string DefaultSaveSlot = "quests";

        [Header("启动行为")]
        [Tooltip("进入游戏时自动加载任务模板。")]
        public bool initializeOnAwake = true;
        [Tooltip("注入服务后自动读取默认槽位的存档。")]
        public bool autoLoadSaveOnAwake = false;

        [Header("HUD")]
        [Tooltip("HUD 最多同时追踪多少个活跃任务。")]
        public int maxTrackedQuests = 3;

        // ------------------------------ 运行时数据 ------------------------------

        private IGameServices services;
        private ILogService log = LogService.Default;
        private bool autoLoadDone;

        /// <summary>任务模板加载器；为空时使用 Resources.LoadAll。测试或自定义资源系统可替换。</summary>
        public Func<QuestDefinition[]> DefinitionLoader { get; set; }

        /// <summary>注入日志与存档服务。根节点在场景加载后调用；也可由创建者显式调用。</summary>
        public void Configure(IGameServices value)
        {
            services = value;
            if (value != null && value.Log != null) log = value.Log;
            if (autoLoadSaveOnAwake && !autoLoadDone)
            {
                autoLoadDone = true;
                LoadFromSlot();
            }
        }

        private readonly Dictionary<string, QuestDefinition> definitions = new Dictionary<string, QuestDefinition>();
        private readonly Dictionary<string, QuestInstance> instances = new Dictionary<string, QuestInstance>();

        /// <summary>复用容器，避免每次上报都分配 List。仅供内部单帧使用。</summary>
        private readonly List<QuestObjectiveProgress> changedObjectiveBuffer = new List<QuestObjectiveProgress>();
        private readonly List<QuestInstance> instanceBuffer = new List<QuestInstance>();
        private readonly List<QuestInstance> trackedBuffer = new List<QuestInstance>();

        private int acceptCounter;
        private bool initialized;
        private bool isSubscribed;

        /// <summary>奖励发放目标；必须由创建者显式赋值（不再回退到 PlayerRewardService 单例）。</summary>
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
            if (initializeOnAwake) EnsureInitialized();
        }

        private void OnEnable()
        {
            SubscribeEventBus();
        }

        private void OnDisable()
        {
            UnsubscribeEventBus();
        }

        private void OnDestroy()
        {
            UnsubscribeEventBus();
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

            QuestDefinition[] loaded = DefinitionLoader != null
                ? DefinitionLoader()
                : Resources.LoadAll<QuestDefinition>(QuestDefinition.ResourceFolder);
            if (loaded == null) loaded = Array.Empty<QuestDefinition>();
            for (int i = 0; i < loaded.Length; i++)
            {
                QuestDefinition definition = loaded[i];
                if (definition == null) continue;

                string questId = definition.GetQuestId();
                if (string.IsNullOrEmpty(questId))
                {
                    log.Warning("[QuestManager] 跳过没有 ID 的任务资产：" + definition.name);
                    continue;
                }
                if (definitions.ContainsKey(questId))
                {
                    log.Warning("[QuestManager] questId 重复，跳过：" + questId + "（" + definition.name + "）");
                    continue;
                }

                List<string> errors = new List<string>();
                if (!definition.ValidateQuest(errors))
                {
                    log.Warning("[QuestManager] 任务 " + questId + " 配置有问题：" + string.Join("；", errors));
                }

                definitions.Add(questId, definition);
            }

            log.Info("[QuestManager] 已加载任务模板 " + definitions.Count + " 个（" + (DefinitionLoader != null ? "自定义加载器" : "Resources/" + QuestDefinition.ResourceFolder) + "）");
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
                log.Warning("[QuestManager] 注册失败：任务没有 ID。");
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
                    log.Warning("[QuestManager] 任务 " + definition.GetQuestId() + " 的前置 " + prerequisiteId + " 不存在。");
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
                log.Warning("[QuestManager] 接受失败：找不到任务 " + questId);
                return false;
            }
            if (quest.status == QuestStatus.Active || quest.status == QuestStatus.Completed)
            {
                return false;
            }
            if (quest.status == QuestStatus.TurnedIn && !quest.IsRepeatable) return false;
            if (quest.status == QuestStatus.Locked && !ArePrerequisitesMet(quest.definition))
            {
                log.Warning("[QuestManager] 接受失败：前置任务未完成 " + questId);
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
                log.Warning("[QuestManager] 提交失败：找不到任务 " + questId);
                return false;
            }
            if (quest.status != QuestStatus.Completed)
            {
                log.Warning("[QuestManager] 提交失败：任务尚有目标未完成 " + questId);
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
                    log.Info("[QuestManager] 任务完成，等待提交：" + quest.Title);
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

        /// <summary>把任务奖励交给显式注入的 IRewardReceiver；没有接收端时只记录、不发放。</summary>
        public void GrantRewards(QuestInstance quest)
        {
            if (quest == null || quest.Reward == null) return;

            IRewardReceiver receiver = RewardReceiver;
            if (receiver == null)
            {
                log.Warning("[QuestManager] 没有奖励接收端，奖励未发放：" + quest.Title);
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

        /// <summary>收集当前进度为可序列化快照（槽位存档与 JSON 调试共用）。</summary>
        private QuestSaveData BuildSaveData()
        {
            EnsureInitialized();
            QuestSaveData data = new QuestSaveData();
            data.version = QuestSaveData.CurrentVersion;
            data.savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            foreach (QuestInstance quest in instances.Values)
            {
                data.quests.Add(quest.ToSaveRecord());
            }
            return data;
        }

        /// <summary>序列化当前进度为 JSON 字符串（调试 / 备份用；正式存档请用 SaveToSlot）。</summary>
        public string SaveToJson(bool pretty = true)
        {
            QuestSaveData data = BuildSaveData();
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
                log.Warning("[QuestManager] 读档失败：内容为空。");
                return false;
            }

            QuestSaveData data;
            try
            {
                data = JsonUtility.FromJson<QuestSaveData>(json);
            }
            catch (Exception exception)
            {
                log.Error("[QuestManager] 读档失败：JSON 解析异常 " + exception.Message);
                return false;
            }

            if (data == null || data.quests == null)
            {
                log.Warning("[QuestManager] 读档失败：数据结构不匹配。");
                return false;
            }

            return ApplySaveData(data);
        }

        /// <summary>把存档内容套用到当前实例（先归零，再按记录恢复，最后重新评估解锁）。</summary>
        private bool ApplySaveData(QuestSaveData data)
        {
            if (data == null || data.quests == null)
            {
                log.Warning("[QuestManager] 读档失败：数据结构不匹配。");
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
                    log.Warning("[QuestManager] 存档里有未知任务，已忽略：" + record.questId);
                    continue;
                }

                quest.ApplySaveRecord(record);
                if (quest.acceptedOrder > acceptCounter) acceptCounter = quest.acceptedOrder;
            }

            // 3) 重新评估解锁（存档里是 Locked 但现在前置已满足的，会重新变为可接受）
            EvaluateAvailability();
            RaiseQuestLogChanged();
            log.Info("[QuestManager] 读档完成，共 " + data.quests.Count + " 条任务记录。");
            return true;
        }

        // ---- 槽位存档（ISaveService）：不再自己拼 persistentDataPath 文件路径 ----

        private static string NormalizeSlot(string slot) { return string.IsNullOrWhiteSpace(slot) ? DefaultSaveSlot : slot; }

        /// <summary>写入注入的存档服务（默认槽位 quests）；未注入服务返回 false。</summary>
        public bool SaveToSlot(string slot = null)
        {
            EnsureInitialized();
            string key = NormalizeSlot(slot);
            ISaveService saves = services != null ? services.Saves : null;
            if (saves == null)
            {
                log.Error("[QuestManager] 未注入 ISaveService，无法存档。");
                return false;
            }
            try
            {
                saves.Save(key, BuildSaveData());
                log.Info("[QuestManager] 已存档到槽位：" + key);
                return true;
            }
            catch (Exception exception)
            {
                log.Error("[QuestManager] 存档失败：" + exception.Message);
                return false;
            }
        }

        /// <summary>从注入的存档服务读取；槽位不存在返回 false。</summary>
        public bool LoadFromSlot(string slot = null)
        {
            EnsureInitialized();
            string key = NormalizeSlot(slot);
            ISaveService saves = services != null ? services.Saves : null;
            if (saves == null)
            {
                log.Error("[QuestManager] 未注入 ISaveService，无法读档。");
                return false;
            }
            try
            {
                QuestSaveData data = saves.Load<QuestSaveData>(key);
                if (data == null)
                {
                    log.Warning("[QuestManager] 读档失败：槽位不存在 " + key);
                    return false;
                }
                return ApplySaveData(data);
            }
            catch (Exception exception)
            {
                log.Error("[QuestManager] 读档失败：" + exception.Message);
                return false;
            }
        }

        public bool HasSave(string slot = null)
        {
            ISaveService saves = services != null ? services.Saves : null;
            return saves != null && saves.Exists(NormalizeSlot(slot));
        }

        public bool DeleteSave(string slot = null)
        {
            ISaveService saves = services != null ? services.Saves : null;
            if (saves == null) return false;
            try { return saves.Delete(NormalizeSlot(slot)); }
            catch (Exception exception)
            {
                log.Error("[QuestManager] 删除存档失败：" + exception.Message);
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
            log.Info("[QuestManager] 已重置所有任务进度。");
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
