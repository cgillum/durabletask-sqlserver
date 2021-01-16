# Durable Task SQL Provider

The [Durable Task Framework](https://github.com/Azure/durabletask) (DTFx) is a lightweight and portable framework that allows developers to build reliable workflows (orchestrations) using .NET tasks and standard C# async/await syntax. Task orchestrations and their activities are written using standard, imperative code. No DSLs or DAGs.

The Microsoft SQL provider for the DTFx persists all task hub state in a Microsoft SQL database, which can be hosted in the cloud or in your own infrastructure. This provider includes support for all DTFx features, including orchestrations, activities, and entities, and has full support for [Azure Durable Functions](https://docs.microsoft.com/azure/azure-functions/durable/durable-functions-overview).

## Motivation

There are several reasons you might consider using

### Portability

Microsoft SQL Server is available as a managed service or a standalone installation across multiple clouds ([Azure](https://azure.microsoft.com/services/azure-sql/), [AWS](https://aws.amazon.com/sql/), [GCP](https://cloud.google.com/sql/), etc.) and multiple platforms ([Windows Server](https://www.microsoft.com/sql-server/), [Linux containers](https://hub.docker.com/_/microsoft-mssql-server), [IoT/Edge](https://azure.microsoft.com/services/sql-edge/), etc.). All your orchestration data is contained in a single database that can easily be exported from one host to another.

### Control

The DTFx schemas can be provisioned into your own database, allowing you to secure it any way you want, incorporate it into existing business continuity processes, easily integrate it with other line-of-business applications, and scale it up or down to meet your price-performance needs.

### Simplicity

This provider was designed from the ground-up with simplicity in mind. The data is transactionally consistent and it's very easy to simply query the tables using familiar tools like the cross-platform [mssql-cli](https://docs.microsoft.com/sql/tools/mssql-cli) or [SQL Server Management Studio](https://docs.microsoft.com/sql/ssms) to understand what's going on.

### Multitenancy

One of the goals for this provider is to create a foundation for safe multi-tenant deployments. This is especially valuable when your organization has many small apps but prefers to manage only a single backend database. Different groups to connect to this database using credentials that isolate access to data and can then run their DTFx workloads on their own compute infrastructure.
