-- Apply migration: 20260321144000_AddAzureAlertContextColumns
-- This migration was missing its Designer.cs so dotnet ef couldn't discover it.
-- Run this script against sqldb-opscopilot-platform-dev to fix the schema drift.

BEGIN TRANSACTION;

-- Add the 8 missing columns (idempotent via column existence check)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AlertProvider')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AlertProvider] nvarchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AlertSourceType')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AlertSourceType] nvarchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AzureApplication')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AzureApplication] nvarchar(128) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AzureResourceGroup')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AzureResourceGroup] nvarchar(128) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AzureResourceId')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AzureResourceId] nvarchar(512) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AzureSubscriptionId')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AzureSubscriptionId] nvarchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'AzureWorkspaceId')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [AzureWorkspaceId] nvarchar(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'IsExceptionSignal')
    ALTER TABLE [agentRuns].[AgentRuns] ADD [IsExceptionSignal] bit NOT NULL DEFAULT 0;

-- Create the 2 new indexes (idempotent)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'IX_AgentRuns_TenantId_AzureResourceGroup_CreatedAtUtc')
    CREATE INDEX [IX_AgentRuns_TenantId_AzureResourceGroup_CreatedAtUtc]
        ON [agentRuns].[AgentRuns] ([TenantId], [AzureResourceGroup], [CreatedAtUtc]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]') AND name = N'IX_AgentRuns_TenantId_IsExceptionSignal_CreatedAtUtc')
    CREATE INDEX [IX_AgentRuns_TenantId_IsExceptionSignal_CreatedAtUtc]
        ON [agentRuns].[AgentRuns] ([TenantId], [IsExceptionSignal], [CreatedAtUtc]);

-- Insert into EF migrations history (idempotent)
IF NOT EXISTS (SELECT 1 FROM [agentRuns].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260321144000_AddAzureAlertContextColumns')
    INSERT INTO [agentRuns].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260321144000_AddAzureAlertContextColumns', N'9.0.2');

COMMIT;

-- Verify
SELECT [MigrationId], [ProductVersion] FROM [agentRuns].[__EFMigrationsHistory] ORDER BY [MigrationId];

SELECT c.name AS ColumnName, t.name AS DataType, c.max_length, c.is_nullable
FROM sys.columns c
JOIN sys.types t ON c.user_type_id = t.user_type_id
WHERE c.object_id = OBJECT_ID(N'[agentRuns].[AgentRuns]')
ORDER BY c.column_id;
