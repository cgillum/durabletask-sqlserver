﻿namespace DurableTask.SqlServer.Tests.Integration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using DurableTask.Core;
    using DurableTask.SqlServer.Tests.Logging;
    using DurableTask.SqlServer.Tests.Utils;
    using Newtonsoft.Json.Linq;
    using Xunit;
    using Xunit.Abstractions;

    public class Orchestrations : IAsyncLifetime
    {
        readonly TestService testService;

        public Orchestrations(ITestOutputHelper output)
        {
            this.testService = new TestService(output);
        }

        Task IAsyncLifetime.InitializeAsync() => this.testService.InitializeAsync();

        Task IAsyncLifetime.DisposeAsync() => this.testService.DisposeAsync();

        [Fact]
        public async Task EmptyOrchestration_Completes()
        {
            string input = $"Hello {DateTime.UtcNow:o}";
            string orchestrationName = "EmptyOrchestration";

            // Does nothing except return the original input
            TestInstance<string> instance = await this.testService.RunOrchestration(
                input,
                orchestrationName,
                implementation: (ctx, input) => Task.FromResult(input));

            await instance.WaitForCompletion(
                expectedOutput: input);

            // Validate logs
            LogAssert.NoWarningsOrErrors(this.testService.LogProvider);
            LogAssert.Sequence(
                this.testService.LogProvider,
                LogAssert.AcquiredAppLock(),
                LogAssert.CheckpointingOrchestration(orchestrationName));
        }

        [Fact]
        public async Task OrchestrationWithTimer_Completes()
        {
            string input = $"Hello {DateTime.UtcNow:o}";
            string orchestrationName = "OrchestrationWithTimer";
            TimeSpan delay = TimeSpan.FromSeconds(3);

            // Performs a delay and then returns the input
            TestInstance<string> instance = await this.testService.RunOrchestration(
                input,
                orchestrationName,
                implementation: async (ctx, input) =>
                {
                    var result = await ctx.CreateTimer(ctx.CurrentUtcDateTime.Add(delay), input);
                    return result;
                });

            TimeSpan timeout = TimeSpan.FromSeconds(10);
            OrchestrationState state = await instance.WaitForCompletion(
                timeout,
                expectedOutput: input);

            // Verify that the delay actually happened
            Assert.True(state.CreatedTime.Add(delay) <= state.CompletedTime);

            // Validate logs
            LogAssert.NoWarningsOrErrors(this.testService.LogProvider);
            LogAssert.Sequence(
                this.testService.LogProvider,
                LogAssert.AcquiredAppLock(),
                LogAssert.CheckpointingOrchestration(orchestrationName),
                LogAssert.CheckpointingOrchestration(orchestrationName));
        }

        [Fact]
        public async Task Orchestration_IsReplaying_Works()
        {
            TestInstance<string> instance = await this.testService.RunOrchestration<List<bool>, string>(
                null,
                orchestrationName: "TwoTimerReplayTester",
                implementation: async (ctx, _) =>
                {
                    var list = new List<bool>();
                    list.Add(ctx.IsReplaying);
                    await ctx.CreateTimer(ctx.CurrentUtcDateTime, 0);
                    list.Add(ctx.IsReplaying);
                    await ctx.CreateTimer(ctx.CurrentUtcDateTime, 0);
                    list.Add(ctx.IsReplaying);
                    return list;
                });

            OrchestrationState state = await instance.WaitForCompletion();
            JArray results = JArray.Parse(state.Output);
            Assert.Equal(3, results.Count);
            Assert.True((bool)results[0]);
            Assert.True((bool)results[1]);
            Assert.False((bool)results[2]);
        }

        [Fact]
        public async Task OrchestrationWithActivity_Completes()
        {
            string input = $"[{DateTime.UtcNow:o}]";

            TestInstance<string> instance = await this.testService.RunOrchestration(
                input,
                orchestrationName: "OrchestrationWithActivity",
                implementation: (ctx, input) => ctx.ScheduleTask<string>("SayHello", "", input),
                activities: new[] {
                    ("SayHello", TestService.MakeActivity((TaskContext ctx, string input) => $"Hello, {input}!")),
                });

            OrchestrationState state = await instance.WaitForCompletion(
                expectedOutput: $"Hello, {input}!");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        public async Task OrchestrationsWithActivityChain_Completes(int parallelCount)
        {
            List<TestInstance<string>> instances = await this.testService.RunOrchestrations<int, string>(
                parallelCount,
                _ => null,
                orchestrationName: "OrchestrationsWithActivityChain",
                implementation: async (ctx, _) =>
                {
                    int value = 0;
                    for (int i = 0; i < 10; i++)
                    {
                        value = await ctx.ScheduleTask<int>("PlusOne", "", value);
                    }

                    return value;
                },
                activities: new[] {
                    ("PlusOne", TestService.MakeActivity((TaskContext ctx, int input) => input + 1)),
                });

            IEnumerable<Task> tasks = instances.Select(
                instance => instance.WaitForCompletion(
                    timeout: TimeSpan.FromSeconds(30),
                    expectedOutput: 10));
            await Task.WhenAll(tasks);
        }

        [Fact]
        public async Task OrchestrationWithException_Fails()
        {
            string errorMessage = "Kah-BOOOOOM!!!";

            // The exception is expected to fail the orchestration execution
            TestInstance<string> instance = await this.testService.RunOrchestration<string, string>(
                null,
                orchestrationName: "OrchestrationWithException",
                implementation: (ctx, input) => throw new Exception(errorMessage));

            await instance.WaitForCompletion(
                timeout: TimeSpan.FromSeconds(10),
                expectedOutput: errorMessage,
                expectedStatus: OrchestrationStatus.Failed);
        }

        [Fact]
        public async Task OrchestrationWithActivityFailure_Fails()
        {
            // Performs a delay and then returns the input
            TestInstance<string> instance = await this.testService.RunOrchestration(
                null as string,
                orchestrationName: "OrchestrationWithActivityFailure",
                implementation: (ctx, input) => ctx.ScheduleTask<string>("Throw", ""),
                activities: new[] {
                    ("Throw", TestService.MakeActivity<string, string>((ctx, input) => throw new Exception("Kah-BOOOOOM!!!"))),
                });

            OrchestrationState state = await instance.WaitForCompletion(
                expectedStatus: OrchestrationStatus.Failed,
                expectedOutputRegex: ".*(Kah-BOOOOOM!!!).*"); // TODO: Test for error message in output
        }

        [Fact]
        public async Task OrchestrationWithActivityFanOut()
        {
            TestInstance<string> instance = await this.testService.RunOrchestration<string[], string>(
                null,
                orchestrationName: "OrchestrationWithActivityFanOut",
                implementation: async (ctx, _) =>
                {
                    var tasks = new List<Task<string>>();
                    for (int i = 0; i < 10; i++)
                    {
                        tasks.Add(ctx.ScheduleTask<string>("ToString", "", i));
                    }

                    string[] results = await Task.WhenAll(tasks);
                    Array.Sort(results);
                    Array.Reverse(results);
                    return results;
                },
                activities: new[] {
                    ("ToString", TestService.MakeActivity((TaskContext ctx, int input) => input.ToString())),
                });

            OrchestrationState state = await instance.WaitForCompletion(
                expectedOutput: new[] { "9", "8", "7", "6", "5", "4", "3", "2", "1", "0" });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(100)]
        public async Task OrchestrationWithExternalEvents(int eventCount)
        {
            TaskCompletionSource<int> tcs = null;

            TestInstance<string> instance = await this.testService.RunOrchestration<int, string>(
                null,
                orchestrationName: "OrchestrationWithExternalEvents",
                implementation: async (ctx, _) =>
                {
                    tcs = new TaskCompletionSource<int>();

                    int i;
                    for (i = 0; i < eventCount; i++)
                    {
                        await tcs.Task;
                        tcs = new TaskCompletionSource<int>();
                    }

                    return i;
                },
                onEvent: (ctx, name, value) =>
                {
                    Assert.Equal("Event" + value, name);
                    tcs.SetResult(int.Parse(value));
                });

            for (int i = 0; i < eventCount; i++)
            {
                await instance.RaiseEventAsync($"Event{i}", i);
            }

            OrchestrationState state = await instance.WaitForCompletion(
                timeout: TimeSpan.FromSeconds(15),
                expectedOutput: eventCount);
        }

        [Fact]
        public async Task TerminateOrchestration()
        {
            string input = $"Hello {DateTime.UtcNow:o}";
            string orchestrationName = "OrchestrationWithTimer";
            TimeSpan delay = TimeSpan.FromSeconds(30);

            // Performs a delay and then returns the input
            TestInstance<string> instance = await this.testService.RunOrchestration(
                input,
                orchestrationName,
                implementation: (ctx, input) => ctx.CreateTimer(ctx.CurrentUtcDateTime.Add(delay), input));

            // Give the orchestration one second to start and then terminate it.
            // We wait to ensure that the log output we expect is deterministic.
            await Task.Delay(TimeSpan.FromSeconds(1));

            await instance.TerminateAsync("Bye!");

            TimeSpan timeout = TimeSpan.FromSeconds(5);
            OrchestrationState state = await instance.WaitForCompletion(
                timeout,
                expectedStatus: OrchestrationStatus.Terminated,
                expectedOutput: "Bye!");

            // Validate logs
            LogAssert.NoWarningsOrErrors(this.testService.LogProvider);
            LogAssert.Sequence(
                this.testService.LogProvider,
                LogAssert.AcquiredAppLock(),
                LogAssert.CheckpointingOrchestration(orchestrationName),
                LogAssert.CheckpointingOrchestration(orchestrationName));
        }


        [Fact]
        public async Task ContinueAsNew()
        {
            TestInstance<int> instance = await this.testService.RunOrchestration(
                input: 0,
                orchestrationName: "ContinueAsNewTest",
                implementation: async (ctx, input) =>
                {
                    if (input < 10)
                    {
                        await ctx.CreateTimer<object>(ctx.CurrentUtcDateTime, null);
                        ctx.ContinueAsNew(input + 1);
                    }

                    return input;
                });

            TimeSpan timeout = TimeSpan.FromSeconds(5);
            await instance.WaitForCompletion(timeout, expectedOutput: 10, continuedAsNew: true);
        }
    }
}
