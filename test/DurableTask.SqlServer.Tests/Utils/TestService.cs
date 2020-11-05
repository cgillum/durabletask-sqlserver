namespace DurableTask.SqlServer.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using DurableTask.Core;
    using DurableTask.SqlServer.Tests.Logging;
    using Microsoft.Extensions.Logging;
    using Xunit;
    using Xunit.Abstractions;

    class TestService
    {
        readonly SqlProviderOptions options;
        readonly ILoggerFactory loggerFactory;

        TaskHubWorker worker;
        TaskHubClient client;

        public TestService(ITestOutputHelper output)
        {
            Type type = output.GetType();
            FieldInfo testMember = type.GetField("test", BindingFlags.Instance | BindingFlags.NonPublic);
            var test = (ITest)testMember.GetValue(output);

            this.LogProvider = new TestLogProvider(output);
            this.loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(this.LogProvider);
            });

            this.options = new SqlProviderOptions
            {
                AppName = test.DisplayName,
                LoggerFactory = this.loggerFactory,
            };
        }

        public TestLogProvider LogProvider { get; }

        public async Task InitializeAsync()
        {
            var provider = new SqlOrchestrationService(this.options);
            await ((IOrchestrationService)provider).CreateIfNotExistsAsync();

            this.worker = await new TaskHubWorker(provider, this.loggerFactory).StartAsync();
            this.client = new TaskHubClient(provider, loggerFactory: this.loggerFactory);
        }

        public Task PurgeAsync(DateTime minimumThreshold, OrchestrationStateTimeRangeFilterType filterType)
        {
            return this.client.PurgeOrchestrationInstanceHistoryAsync(
                minimumThreshold,
                filterType);
        }

        public async Task DisposeAsync()
        {
            await this.worker.StopAsync(isForced: true);
            this.worker.Dispose();
        }

        public async Task<TestInstance<TInput>> RunOrchestration<TOutput, TInput>(
            TInput input,
            string orchestrationName,
            Func<OrchestrationContext, TInput, Task<TOutput>> implementation,
            Action<OrchestrationContext, string, string> onEvent = null,
            params (string name, TaskActivity activity)[] activities)
        {
            var instances = await this.RunOrchestrations(
                count: 1,
                inputGenerator: i => input,
                orchestrationName,
                implementation,
                onEvent,
                activities);

            return instances.First();
        }

        public async Task<List<TestInstance<TInput>>> RunOrchestrations<TOutput, TInput>(
            int count,
            Func<int, TInput> inputGenerator,
            string orchestrationName,
            Func<OrchestrationContext, TInput, Task<TOutput>> implementation,
            Action<OrchestrationContext, string, string> onEvent = null,
            params (string name, TaskActivity activity)[] activities)
        {
            // Register the inline orchestration - note that this will only work once per test
            Type orchestrationType = typeof(OrchestrationShim<TOutput, TInput>);

            this.worker.AddTaskOrchestrations(new TestObjectCreator<TaskOrchestration>(
                orchestrationName,
                MakeOrchestration(implementation, onEvent)));

            foreach ((string name, TaskActivity activity) in activities)
            {
                this.worker.AddTaskActivities(new TestObjectCreator<TaskActivity>(name, activity));
            }

            DateTime utcNow = DateTime.UtcNow;

            var instances = new List<TestInstance<TInput>>(count);
            for (int i = 0; i < count; i++)
            {
                TInput input = inputGenerator(i);
                OrchestrationInstance instance = await this.client.CreateOrchestrationInstanceAsync(
                    orchestrationName,
                    string.Empty /* version */,
                    input);

                // Verify that the CreateOrchestrationInstanceAsync implementation set the InstanceID and ExecutionID fields
                Assert.NotNull(instance.InstanceId);
                Assert.NotNull(instance.ExecutionId);

                instances.Add(new TestInstance<TInput>(this.client, instance, utcNow, input));
            }

            return instances;
        }

        public static TaskOrchestration MakeOrchestration<TOutput, TInput>(
            Func<OrchestrationContext, TInput, Task<TOutput>> implementation,
            Action<OrchestrationContext, string, string> onEvent = null)
        {
            return new OrchestrationShim<TOutput, TInput>(implementation, onEvent);
        }

        // This is just a wrapper around the constructor for convenience. It allows us to write 
        // less code because generic arguments for methods can be implied, unlike constructors.
        public static TaskActivity MakeActivity<TInput, TOutput>(
            Func<TaskContext, TInput, TOutput> implementation)
        {
            return new ActivityShim<TInput, TOutput>(implementation);
        }

        static string GetFriendlyTypeName(Type type)
        {
            string friendlyName = type.Name;
            if (type.IsGenericType)
            {
                int iBacktick = friendlyName.IndexOf('`');
                if (iBacktick > 0)
                {
                    friendlyName = friendlyName.Remove(iBacktick);
                }

                friendlyName += "<";
                Type[] typeParameters = type.GetGenericArguments();
                for (int i = 0; i < typeParameters.Length; ++i)
                {
                    string typeParamName = GetFriendlyTypeName(typeParameters[i]);
                    friendlyName += (i == 0 ? typeParamName : "," + typeParamName);
                }

                friendlyName += ">";
            }

            return friendlyName;
        }

        class ActivityShim<TInput, TOutput> : TaskActivity<TInput, TOutput>
        {
            public ActivityShim(Func<TaskContext, TInput, TOutput> implementation)
            {
                this.Implementation = implementation;
            }

            public Func<TaskContext, TInput, TOutput> Implementation { get; }

            protected override TOutput Execute(TaskContext context, TInput input)
            {
                return this.Implementation(context, input);
            }
        }

        class OrchestrationShim<TOutput, TInput> : TaskOrchestration<TOutput, TInput>
        {
            public OrchestrationShim(
                Func<OrchestrationContext, TInput, Task<TOutput>> implementation,
                Action<OrchestrationContext, string, string> onEvent = null)
            {
                this.Implementation = implementation;
                this.OnEventRaised = onEvent;
            }

            public Func<OrchestrationContext, TInput, Task<TOutput>> Implementation { get; set; }

            public Action<OrchestrationContext, string, string> OnEventRaised { get; set; }

            public override Task<TOutput> RunTask(OrchestrationContext context, TInput input)
                => this.Implementation(context, input);

            public override void RaiseEvent(OrchestrationContext context, string name, string input)
                => this.OnEventRaised(context, name, input);
        }

        class TestObjectCreator<T> : ObjectCreator<T>
        {
            readonly T obj;

            public TestObjectCreator(string name, T obj)
            {
                this.Name = name;
                this.Version = string.Empty;
                this.obj = obj;
            }

            public override T Create() => this.obj;
        }
    }
}
