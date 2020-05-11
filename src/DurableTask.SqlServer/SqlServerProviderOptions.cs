﻿namespace DurableTask.SqlServer
{
    using System;
    using Microsoft.Data.SqlClient;
    using Microsoft.Extensions.Logging;
    using Newtonsoft.Json;

    public class SqlServerProviderOptions
    {
        [JsonProperty("maxActivityConcurrency")]
        public int MaxActivityConcurrency { get; set; } = Environment.ProcessorCount;

        [JsonProperty("maxOrchestrationConcurrency")]
        public int MaxOrchestrationConcurrency { get; set; } = Environment.ProcessorCount;

        [JsonProperty("taskEventLockTimeout")]
        public TimeSpan TaskEventLockTimeout { get; set; } = TimeSpan.FromMinutes(2);

        [JsonProperty("appName")]
        public string AppName { get; set; } = Environment.MachineName;

        // Not serializeable (security sensitive) - must be initializd in code
        public string ConnectionString { get; set; } = GetDefaultConnectionString();

        // Not serializeable (complex object) - must be initialized in code
        public ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();

        internal SqlConnection CreateConnection() => new SqlConnection(this.ConnectionString);

        static string GetDefaultConnectionString()
        {
            // The default for local development on a Windows OS
            string defaultConnectionString = "Server=localhost;Database=TaskHub;Trusted_Connection=True;";

            string saPassword = Environment.GetEnvironmentVariable("SA_PASSWORD");
            if (string.IsNullOrEmpty(saPassword))
            {
                return defaultConnectionString;
            }

            var builder = new SqlConnectionStringBuilder(defaultConnectionString)
            {
                IntegratedSecurity = false,
                UserID = "sa",
                Password = saPassword,
            };

            return builder.ToString();
        }
    }
}
