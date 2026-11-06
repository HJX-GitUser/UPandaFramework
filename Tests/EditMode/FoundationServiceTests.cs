using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>新增基础服务的纯逻辑单测：不依赖场景、不依赖 PublicMono 帧循环。</summary>
    public class FoundationServiceTests
    {
        private static async Task AssertThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            try { await action(); }
            catch (TException) { return; }
            catch (Exception e)
            {
                Assert.Fail($"期望异常 {typeof(TException).Name}，实际为 {e.GetType().Name}。");
                return;
            }
            Assert.Fail($"期望异常 {typeof(TException).Name}，但没有抛出异常。");
        }

        [Test]
        public void Timer_Delay_FiresOnlyAfterElapsed()
        {
            var timer = new TimerService();
            bool fired = false;
            timer.Delay(1f, () => fired = true);
            timer.Tick(0.5f, 0.5f);
            Assert.IsFalse(fired);
            timer.Tick(0.6f, 0.6f);
            Assert.IsTrue(fired);
            timer.Dispose();
        }

        [Test]
        public void Timer_UnscaledRunsWhenScaledDeltaIsZero()
        {
            var timer = new TimerService();
            bool scaled = false, unscaled = false;
            timer.Delay(0.5f, () => scaled = true, scaled: true);
            timer.Delay(0.5f, () => unscaled = true, scaled: false);
            timer.Tick(0f, 1f); // 模拟暂停：scaledDelta 为 0
            Assert.IsFalse(scaled);
            Assert.IsTrue(unscaled);
            timer.Dispose();
        }

        [Test]
        public void Timer_EveryRepeatsUntilCanceled()
        {
            var timer = new TimerService();
            int count = 0;
            var handle = timer.Every(0.5f, () => count++);
            timer.Tick(0.5f, 0.5f);
            timer.Tick(0.5f, 0.5f);
            Assert.AreEqual(2, count);
            handle.Cancel();
            timer.Tick(0.5f, 0.5f);
            Assert.AreEqual(2, count);
            timer.Dispose();
        }

        [Test]
        public async Task Timer_DelayAsync_Completes()
        {
            var timer = new TimerService();
            var task = timer.DelayAsync(1f);
            timer.Tick(0.5f, 0.5f);
            Assert.IsFalse(task.IsCompleted);
            timer.Tick(0.6f, 0.6f);
            await task;
            Assert.IsTrue(task.IsCompletedSuccessfully);
            timer.Dispose();
        }

        [Test]
        public async Task Timer_DelayAsync_HonorsCancellation()
        {
            var timer = new TimerService();
            using (var cts = new CancellationTokenSource())
            {
                var task = timer.DelayAsync(1f, cts.Token);
                cts.Cancel();
                await AssertThrowsAsync<TaskCanceledException>(() => task);
            }
            timer.Dispose();
        }

        [Test]
        public void Timer_CallbackExceptionDoesNotBreakOthers()
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                var timer = new TimerService();
                bool reached = false;
                timer.Delay(0.5f, () => throw new InvalidOperationException("boom"));
                timer.Delay(0.5f, () => reached = true);
                timer.Tick(1f, 1f);
                Assert.IsTrue(reached);
                timer.Dispose();
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        [Serializable]
        public class DemoConfig
        {
            public int hp = 10;
            public string env = "dev";
        }

        [Test]
        public void Config_RegisterGetAndDuplicateGuard()
        {
            var config = new ConfigService();
            config.Register("battle", new DemoConfig(), version: 2);
            Assert.AreEqual(10, config.Get<DemoConfig>("battle").hp);
            Assert.AreEqual(2, config.GetVersion("battle"));
            Assert.Throws<InvalidOperationException>(() => config.Register("battle", new DemoConfig()));
            Assert.IsFalse(config.TryGet<DemoConfig>("missing", out _));
        }

        [Test]
        public void Config_OverridesAndValidation()
        {
            var config = new ConfigService();
            config.Register("battle", new DemoConfig(), validator: (cfg, issues) =>
            {
                if (cfg.hp <= 0) issues.Add(new ConfigIssue("battle", "hp 必须为正数", ConfigIssueLevel.Error));
            });
            config.ApplyOverrides(new Dictionary<string, string> { { "battle", "{\"hp\":0,\"env\":\"prod\"}" } });
            var value = config.Get<DemoConfig>("battle");
            Assert.AreEqual(0, value.hp);
            Assert.AreEqual("prod", value.env);
            var issues = config.Validate();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ConfigIssueLevel.Error, issues[0].Level);
        }

        private sealed class StubState : IGameState
        {
            public string Id { get; }
            public int EnterCount, ExitCount;
            public bool Allow = true;
            public bool ThrowOnEnter;
            public StubState(string id) { Id = id; }
            public bool CanEnter(GameFlowContext context, out string reason)
            { reason = Allow ? null : "blocked"; return Allow; }
            public Task EnterAsync(GameFlowContext context, CancellationToken token)
            { EnterCount++; if (ThrowOnEnter) throw new InvalidOperationException("enter failed"); return Task.CompletedTask; }
            public Task ExitAsync(CancellationToken token) { ExitCount++; return Task.CompletedTask; }
        }

        [Test]
        public async Task GameFlow_StartSwitchAndNotify()
        {
            var flow = new GameFlowController();
            var boot = new StubState("Boot");
            var lobby = new StubState("Lobby");
            flow.Register(boot); flow.Register(lobby);
            int notifications = 0;
            flow.Changed += ctx => { notifications++; Assert.AreEqual("Lobby", ctx.To); };
            await flow.StartAsync("Boot");
            Assert.AreEqual(GameFlowStatus.Ready, flow.Status);
            await flow.GoToAsync("Lobby");
            Assert.AreEqual("Lobby", flow.CurrentStateId);
            Assert.AreEqual(1, boot.ExitCount);
            Assert.AreEqual(1, lobby.EnterCount);
            Assert.AreEqual(1, notifications);
            await flow.ShutdownAsync();
            flow.Dispose();
        }

        [Test]
        public async Task GameFlow_GuardRejectsWithoutLeavingCurrentState()
        {
            var flow = new GameFlowController();
            var boot = new StubState("Boot");
            var lobby = new StubState("Lobby") { Allow = false };
            flow.Register(boot); flow.Register(lobby);
            await flow.StartAsync("Boot");
            await AssertThrowsAsync<InvalidOperationException>(() => flow.GoToAsync("Lobby"));
            Assert.AreEqual("Boot", flow.CurrentStateId);
            Assert.AreEqual(GameFlowStatus.Ready, flow.Status);
            flow.Dispose();
        }

        [Test]
        public async Task GameFlow_EnterFailureMarksFailed()
        {
            var flow = new GameFlowController();
            var boot = new StubState("Boot");
            var battle = new StubState("Battle") { ThrowOnEnter = true };
            flow.Register(boot); flow.Register(battle);
            await flow.StartAsync("Boot");
            await AssertThrowsAsync<InvalidOperationException>(() => flow.GoToAsync("Battle"));
            Assert.AreEqual(GameFlowStatus.Failed, flow.Status);
            Assert.AreEqual("Boot", flow.CurrentStateId);
            flow.Dispose();
        }

        [Test]
        public async Task StartupLifecycle_RecordsMetricsIncludingFailure()
        {
            var lifecycle = new StartupLifecycle();
            bool stopped = false;
            lifecycle.Add("Ok", ct => Task.CompletedTask, () => { stopped = true; return Task.CompletedTask; });
            lifecycle.Add("Bad", ct => throw new InvalidOperationException("init failed"));
            var pending = lifecycle.GetModuleDiagnostics();
            Assert.AreEqual(2, pending.Count);
            Assert.AreEqual(FrameworkModuleState.Pending, pending[0].State);
            await AssertThrowsAsync<InvalidOperationException>(() => lifecycle.InitializeAsync());
            Assert.AreEqual(StartupState.Failed, lifecycle.State);
            Assert.AreEqual(2, lifecycle.Metrics.Count);
            Assert.IsTrue(lifecycle.Metrics[0].Completed);
            Assert.IsFalse(lifecycle.Metrics[1].Completed);
            Assert.IsTrue(stopped); // 失败步骤参与逆序回滚
            var modules = lifecycle.GetModuleDiagnostics();
            Assert.AreEqual(FrameworkModuleState.RolledBack, modules[0].State);
            Assert.AreEqual(FrameworkModuleState.Failed, modules[1].State);
            await lifecycle.ShutdownAsync();
        }
    }
}
