using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HarborOps.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ChangeDocumentIdToGuid : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // SQL Server cannot ALTER COLUMN between int and uniqueidentifier (type clash),
        // nor can it ALTER an IDENTITY column. All four columns use drop/add/rename.

        // ── Documents.Id (temporal, IDENTITY) int → uniqueidentifier ─────────────

        // Drop FK and index on Notifications before touching Documents
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] DROP CONSTRAINT [FK_Notifications_Documents_DocumentId];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Notifications_DocumentId] ON [documents].[Notifications];");

        // Turn off system versioning so we can modify Documents + its history table
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = OFF);");

        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_DocumentTypeId] ON [documents].[Documents];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_ExpiryDate] ON [documents].[Documents];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_VesselId] ON [documents].[Documents];");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] DROP CONSTRAINT [PK_Documents];");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] ADD [NewId] uniqueidentifier NOT NULL DEFAULT NEWID();");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[DocumentsHistory] ADD [NewId] uniqueidentifier NOT NULL DEFAULT NEWID();");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] DROP COLUMN [Id];");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[DocumentsHistory] DROP COLUMN [Id];");

        migrationBuilder.Sql(
            "EXEC sp_rename '[documents].[Documents].[NewId]', 'Id', 'COLUMN';");
        migrationBuilder.Sql(
            "EXEC sp_rename '[documents].[DocumentsHistory].[NewId]', 'Id', 'COLUMN';");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] ADD CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]);");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = ON " +
            "(HISTORY_TABLE = [documents].[DocumentsHistory], DATA_CONSISTENCY_CHECK = OFF));");

        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_DocumentTypeId] ON [documents].[Documents] ([DocumentTypeId]);");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_ExpiryDate] ON [documents].[Documents] ([ExpiryDate]);");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_VesselId] ON [documents].[Documents] ([VesselId]);");

        // ── Notifications.DocumentId int → uniqueidentifier ───────────────────────
        // FK and index already dropped above. Use drop/add/rename (ALTER COLUMN fails).

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] ADD [DocumentId_new] uniqueidentifier NOT NULL " +
            "DEFAULT '00000000-0000-0000-0000-000000000000';");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] DROP COLUMN [DocumentId];");
        migrationBuilder.Sql(
            "EXEC sp_rename '[documents].[Notifications].[DocumentId_new]', 'DocumentId', 'COLUMN';");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] ADD CONSTRAINT [FK_Notifications_Documents_DocumentId] " +
            "FOREIGN KEY ([DocumentId]) REFERENCES [documents].[Documents] ([Id]) ON DELETE CASCADE;");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Notifications_DocumentId] ON [documents].[Notifications] ([DocumentId]);");

        // ── SurveyAudits.DocumentId int? → uniqueidentifier? ─────────────────────
        // Not temporal, no FK at DB level, but ALTER COLUMN still fails (type clash).

        migrationBuilder.Sql(
            "DROP INDEX [IX_SurveyAudits_DocumentId] ON [compliance].[SurveyAudits];");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[SurveyAudits] ADD [DocumentId_new] uniqueidentifier NULL;");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[SurveyAudits] DROP COLUMN [DocumentId];");
        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[SurveyAudits].[DocumentId_new]', 'DocumentId', 'COLUMN';");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_SurveyAudits_DocumentId] ON [compliance].[SurveyAudits] ([DocumentId]);");

        // ── NonConformities.EvidenceDocumentId int → uniqueidentifier (temporal) ──

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] SET (SYSTEM_VERSIONING = OFF);");
        migrationBuilder.Sql(
            "DROP INDEX [IX_NonConformities_EvidenceDocumentId] ON [compliance].[NonConformities];");

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] ADD [EvidenceDocumentId_new] uniqueidentifier NOT NULL " +
            "DEFAULT '00000000-0000-0000-0000-000000000000';");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformitiesHistory] ADD [EvidenceDocumentId_new] uniqueidentifier NOT NULL " +
            "DEFAULT '00000000-0000-0000-0000-000000000000';");

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] DROP COLUMN [EvidenceDocumentId];");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformitiesHistory] DROP COLUMN [EvidenceDocumentId];");

        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[NonConformities].[EvidenceDocumentId_new]', 'EvidenceDocumentId', 'COLUMN';");
        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[NonConformitiesHistory].[EvidenceDocumentId_new]', 'EvidenceDocumentId', 'COLUMN';");

        migrationBuilder.Sql(
            "CREATE INDEX [IX_NonConformities_EvidenceDocumentId] ON [compliance].[NonConformities] ([EvidenceDocumentId]);");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] SET (SYSTEM_VERSIONING = ON " +
            "(HISTORY_TABLE = [compliance].[NonConformitiesHistory], DATA_CONSISTENCY_CHECK = OFF));");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // ── NonConformities.EvidenceDocumentId uniqueidentifier → int (temporal) ──

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] SET (SYSTEM_VERSIONING = OFF);");
        migrationBuilder.Sql(
            "DROP INDEX [IX_NonConformities_EvidenceDocumentId] ON [compliance].[NonConformities];");

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] ADD [EvidenceDocumentId_old] int NOT NULL DEFAULT 0;");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformitiesHistory] ADD [EvidenceDocumentId_old] int NOT NULL DEFAULT 0;");

        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] DROP COLUMN [EvidenceDocumentId];");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformitiesHistory] DROP COLUMN [EvidenceDocumentId];");

        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[NonConformities].[EvidenceDocumentId_old]', 'EvidenceDocumentId', 'COLUMN';");
        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[NonConformitiesHistory].[EvidenceDocumentId_old]', 'EvidenceDocumentId', 'COLUMN';");

        migrationBuilder.Sql(
            "CREATE INDEX [IX_NonConformities_EvidenceDocumentId] ON [compliance].[NonConformities] ([EvidenceDocumentId]);");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[NonConformities] SET (SYSTEM_VERSIONING = ON " +
            "(HISTORY_TABLE = [compliance].[NonConformitiesHistory], DATA_CONSISTENCY_CHECK = OFF));");

        // ── SurveyAudits.DocumentId uniqueidentifier? → int? ─────────────────────

        migrationBuilder.Sql(
            "DROP INDEX [IX_SurveyAudits_DocumentId] ON [compliance].[SurveyAudits];");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[SurveyAudits] ADD [DocumentId_old] int NULL;");
        migrationBuilder.Sql(
            "ALTER TABLE [compliance].[SurveyAudits] DROP COLUMN [DocumentId];");
        migrationBuilder.Sql(
            "EXEC sp_rename '[compliance].[SurveyAudits].[DocumentId_old]', 'DocumentId', 'COLUMN';");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_SurveyAudits_DocumentId] ON [compliance].[SurveyAudits] ([DocumentId]);");

        // ── Notifications.DocumentId uniqueidentifier → int ───────────────────────

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] DROP CONSTRAINT [FK_Notifications_Documents_DocumentId];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Notifications_DocumentId] ON [documents].[Notifications];");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] ADD [DocumentId_old] int NOT NULL DEFAULT 0;");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] DROP COLUMN [DocumentId];");
        migrationBuilder.Sql(
            "EXEC sp_rename '[documents].[Notifications].[DocumentId_old]', 'DocumentId', 'COLUMN';");

        // ── Documents.Id uniqueidentifier → int IDENTITY (temporal) ──────────────

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = OFF);");

        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_DocumentTypeId] ON [documents].[Documents];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_ExpiryDate] ON [documents].[Documents];");
        migrationBuilder.Sql(
            "DROP INDEX [IX_Documents_VesselId] ON [documents].[Documents];");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] DROP CONSTRAINT [PK_Documents];");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] DROP COLUMN [Id];");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[DocumentsHistory] DROP COLUMN [Id];");

        // History table gets plain int (no IDENTITY — history rows are copied, not inserted)
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] ADD [Id] int IDENTITY(1,1) NOT NULL;");
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[DocumentsHistory] ADD [Id] int NOT NULL DEFAULT 0;");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] ADD CONSTRAINT [PK_Documents] PRIMARY KEY ([Id]);");

        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Documents] SET (SYSTEM_VERSIONING = ON " +
            "(HISTORY_TABLE = [documents].[DocumentsHistory], DATA_CONSISTENCY_CHECK = OFF));");

        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_DocumentTypeId] ON [documents].[Documents] ([DocumentTypeId]);");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_ExpiryDate] ON [documents].[Documents] ([ExpiryDate]);");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Documents_VesselId] ON [documents].[Documents] ([VesselId]);");

        // Recreate FK on Notifications
        migrationBuilder.Sql(
            "ALTER TABLE [documents].[Notifications] ADD CONSTRAINT [FK_Notifications_Documents_DocumentId] " +
            "FOREIGN KEY ([DocumentId]) REFERENCES [documents].[Documents] ([Id]) ON DELETE CASCADE;");
        migrationBuilder.Sql(
            "CREATE INDEX [IX_Notifications_DocumentId] ON [documents].[Notifications] ([DocumentId]);");
    }
}
