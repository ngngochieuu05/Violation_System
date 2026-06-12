using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public static class DbSeeder
{
    public static void Initialize(ViolationDbContext context)
    {
        context.Database.EnsureCreated();

        EnsureUserColumns(context);
        EnsureViolationColumns(context);
        EnsureEmployeeWorkspaceTables(context);

        if (!context.Users.Any())
        {
            context.Users.AddRange(
                new User
                {
                    Id = Guid.NewGuid(),
                    Username = "admin",
                    PasswordHash = PasswordHasher.HashPassword("admin123"),
                    FullName = "System Administrator",
                    Role = "Admin",
                    FaceImagePath = string.Empty,
                    AvatarPath = string.Empty,
                    ManagerKey = string.Empty,
                    IsKeyActivated = true,
                    MustChangePassword = false,
                    RequiresInitialSecuritySetup = false,
                    Email = "admin@compliancehub.vn",
                    Phone = "0987 654 321",
                    Department = "Quản trị hệ thống",
                    EmployeeCode = "ADM-001",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    Username = "manager",
                    PasswordHash = PasswordHasher.HashPassword("manager123"),
                    FullName = "General Manager",
                    Role = "Manager",
                    FaceImagePath = string.Empty,
                    AvatarPath = string.Empty,
                    ManagerKey = "hieudeptraivcl",
                    IsKeyActivated = false,
                    MustChangePassword = false,
                    RequiresInitialSecuritySetup = false,
                    Email = "manager@compliancehub.vn",
                    Phone = "0912 345 678",
                    Department = "Ban Giám Đốc",
                    EmployeeCode = "MGR-001",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new User
                {
                    Id = Guid.NewGuid(),
                    Username = "employee",
                    PasswordHash = PasswordHasher.HashPassword("employee123"),
                    FullName = "Nguyen Van Employee",
                    Role = "Employee",
                    FaceImagePath = string.Empty,
                    AvatarPath = string.Empty,
                    ManagerKey = string.Empty,
                    IsKeyActivated = true,
                    MustChangePassword = false,
                    RequiresInitialSecuritySetup = false,
                    Email = "employee@compliancehub.vn",
                    Phone = "0123 456 789",
                    Department = "Khối vận hành",
                    EmployeeCode = "NV014",
                    CreatedAtUtc = DateTime.UtcNow
                });
        }

        if (!context.ModelSettings.Any())
        {
            context.ModelSettings.Add(new ModelSetting
            {
                YoloModelPath = "ML/weights/best.pt",
                YoloConfThreshold = 0.25m,
                YoloIouThreshold = 0.45m,
                DeepfaceConfThreshold = 0.40m,
                IsActive = true
            });
        }

        if (!context.AiModels.Any())
        {
            context.AiModels.AddRange(
                new AiModel
                {
                    Id = Guid.NewGuid(),
                    Name = "YOLO Smoking Detection",
                    Type = "YoloSmoking",
                    ModelPath = "ML/weights/smoking.pt",
                    ConfThreshold = 0.25m,
                    IouThreshold = 0.45m,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new AiModel
                {
                    Id = Guid.NewGuid(),
                    Name = "YOLO Leaving Position Detection",
                    Type = "YoloLeaving",
                    ModelPath = "ML/weights/leaving.pt",
                    ConfThreshold = 0.30m,
                    IouThreshold = 0.45m,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                },
                new AiModel
                {
                    Id = Guid.NewGuid(),
                    Name = "DeepFace Face Recognition",
                    Type = "Deepface",
                    ModelPath = "VGG-Face",
                    ConfThreshold = 0.55m,
                    IouThreshold = 0,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                });
        }
        else
        {
            var activeDeepfaceModel = context.AiModels.FirstOrDefault(m => m.Type == "Deepface" && m.IsActive);
            if (activeDeepfaceModel != null &&
                activeDeepfaceModel.ModelPath == "VGG-Face" &&
                activeDeepfaceModel.ConfThreshold < 0.55m)
            {
                activeDeepfaceModel.ConfThreshold = 0.55m;
            }
        }

        if (!context.ViolationRecords.Any())
        {
            context.ViolationRecords.AddRange(
                new ViolationRecord
                {
                    Id = Guid.NewGuid(),
                    TrackingId = "VR-SEED-0001",
                    EmployeeCode = "NV001",
                    EmployeeName = "Nguyen Van A",
                    ViolationType = "Khong doi mu bao ho",
                    Severity = "High",
                    DetectedAtUtc = DateTime.UtcNow.AddMinutes(-25),
                    CameraLocation = "Xuong A - Cong vao",
                    EvidenceUrl = "/evidence/sample-1.jpg",
                    Status = "Pending"
                },
                new ViolationRecord
                {
                    Id = Guid.NewGuid(),
                    TrackingId = "VR-SEED-0002",
                    EmployeeCode = "NV014",
                    EmployeeName = "Tran Thi B",
                    ViolationType = "Vao khu vuc han che",
                    Severity = "Medium",
                    DetectedAtUtc = DateTime.UtcNow.AddHours(-2),
                    CameraLocation = "Kho B",
                    EvidenceUrl = "/evidence/sample-2.jpg",
                    Status = "Reviewed"
                });
        }
        else
        {
            var existingViolations = context.ViolationRecords
                .Where(v => string.IsNullOrWhiteSpace(v.TrackingId))
                .ToList();

            foreach (var violation in existingViolations)
            {
                violation.TrackingId = $"VR-{violation.Id.ToString("N")[..10].ToUpperInvariant()}";
            }
        }

        if (!context.AuditLogs.Any())
        {
            context.AuditLogs.AddRange(
                new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddMinutes(-10),
                    Username = "Nguyen Duong",
                    Action = "Dang nhap",
                    Details = "Quan ly da dang nhap he thong",
                    IpAddress = "192.168.1.50",
                    Status = "Thanh cong"
                },
                new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddMinutes(-30),
                    Username = "admin",
                    Action = "Cap nhat cau hinh",
                    Details = "Cap nhat model cham cong",
                    IpAddress = "127.0.0.1",
                    Status = "Thanh cong"
                });
        }

        foreach (var employee in context.Users.Where(u => u.Role == "Employee").ToList())
        {
            if (!context.EmployeePreferences.Any(p => p.UserId == employee.Id))
            {
                context.EmployeePreferences.Add(new EmployeePreference
                {
                    Id = Guid.NewGuid(),
                    UserId = employee.Id,
                    NotificationsEnabled = true,
                    CompactMode = false,
                    ReducedMotion = false,
                    Language = "vi-VN",
                    Theme = "light",
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }
        }

        if (!context.FormTemplates.Any())
        {
            context.FormTemplates.AddRange(
                new FormTemplate
                {
                    Title = "Đơn xin nghỉ phép",
                    Description = "Sử dụng khi nhân viên cần đăng ký nghỉ có phép theo ngày.",
                    FileUrl = string.Empty,
                    LastUpdated = DateTime.UtcNow
                },
                new FormTemplate
                {
                    Title = "Đơn xin đi muộn về sớm",
                    Description = "Sử dụng khi nhân viên cần xin phép đến muộn hoặc rời ca sớm.",
                    FileUrl = string.Empty,
                    LastUpdated = DateTime.UtcNow
                },
                new FormTemplate
                {
                    Title = "Đơn xin tăng ca",
                    Description = "Sử dụng khi nhân viên đăng ký làm thêm giờ theo kế hoạch.",
                    FileUrl = string.Empty,
                    LastUpdated = DateTime.UtcNow
                },
                new FormTemplate
                {
                    Title = "Đơn xin điều chỉnh ca làm",
                    Description = "Sử dụng khi nhân viên cần đổi lịch hoặc điều chỉnh ca làm việc.",
                    FileUrl = string.Empty,
                    LastUpdated = DateTime.UtcNow
                });
        }

        context.SaveChanges();
    }

    private static void EnsureUserColumns(ViolationDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('Users', 'MustChangePassword') IS NULL
            BEGIN
                ALTER TABLE [Users] ADD [MustChangePassword] bit NOT NULL CONSTRAINT [DF_Users_MustChangePassword] DEFAULT(0);
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('Users', 'RequiresInitialSecuritySetup') IS NULL
            BEGIN
                ALTER TABLE [Users] ADD [RequiresInitialSecuritySetup] bit NOT NULL CONSTRAINT [DF_Users_RequiresInitialSecuritySetup] DEFAULT(0);
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('Users', 'AvatarPath') IS NULL
            BEGIN
                ALTER TABLE [Users] ADD [AvatarPath] nvarchar(512) NOT NULL CONSTRAINT [DF_Users_AvatarPath] DEFAULT('');
            END
            """);
    }

    private static void EnsureViolationColumns(ViolationDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ViolationRecords', 'TrackingId') IS NULL
            BEGIN
                ALTER TABLE [ViolationRecords] ADD [TrackingId] nvarchar(64) NOT NULL CONSTRAINT [DF_ViolationRecords_TrackingId] DEFAULT('');
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ViolationRecords', 'ReviewedBy') IS NULL
            BEGIN
                ALTER TABLE [ViolationRecords] ADD [ReviewedBy] nvarchar(256) NULL;
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ViolationRecords', 'ReviewedAtUtc') IS NULL
            BEGIN
                ALTER TABLE [ViolationRecords] ADD [ReviewedAtUtc] datetime2 NULL;
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ViolationRecords', 'ReviewChannel') IS NULL
            BEGIN
                ALTER TABLE [ViolationRecords] ADD [ReviewChannel] nvarchar(64) NULL;
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ViolationRecords', 'ReviewNote') IS NULL
            BEGIN
                ALTER TABLE [ViolationRecords] ADD [ReviewNote] nvarchar(512) NULL;
            END
            """);
    }

    private static void EnsureEmployeeWorkspaceTables(ViolationDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.WorkSessions', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[WorkSessions]
                (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [EmployeeUserId] uniqueidentifier NULL,
                    [EmployeeId] nvarchar(128) NOT NULL DEFAULT(''),
                    [EmployeeCode] nvarchar(128) NOT NULL DEFAULT(''),
                    [EmployeeName] nvarchar(256) NOT NULL DEFAULT(''),
                    [Date] datetime2 NOT NULL,
                    [CheckInTime] datetime2 NOT NULL,
                    [CheckOutTime] datetime2 NULL,
                    [Status] nvarchar(64) NOT NULL DEFAULT('Pending'),
                    [Notes] nvarchar(max) NOT NULL DEFAULT(''),
                    [CheckInImagePath] nvarchar(512) NOT NULL DEFAULT(''),
                    [CheckOutImagePath] nvarchar(512) NOT NULL DEFAULT('')
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('WorkSessions', 'EmployeeUserId') IS NULL ALTER TABLE [WorkSessions] ADD [EmployeeUserId] uniqueidentifier NULL;
            IF COL_LENGTH('WorkSessions', 'EmployeeCode') IS NULL ALTER TABLE [WorkSessions] ADD [EmployeeCode] nvarchar(128) NOT NULL CONSTRAINT [DF_WorkSessions_EmployeeCode] DEFAULT('');
            IF COL_LENGTH('WorkSessions', 'CheckInImagePath') IS NULL ALTER TABLE [WorkSessions] ADD [CheckInImagePath] nvarchar(512) NOT NULL CONSTRAINT [DF_WorkSessions_CheckInImagePath] DEFAULT('');
            IF COL_LENGTH('WorkSessions', 'CheckOutImagePath') IS NULL ALTER TABLE [WorkSessions] ADD [CheckOutImagePath] nvarchar(512) NOT NULL CONSTRAINT [DF_WorkSessions_CheckOutImagePath] DEFAULT('');
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.ApprovalRequests', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[ApprovalRequests]
                (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [EmployeeUserId] uniqueidentifier NULL,
                    [EmployeeUsername] nvarchar(128) NOT NULL DEFAULT(''),
                    [EmployeeName] nvarchar(256) NOT NULL DEFAULT(''),
                    [RequestType] nvarchar(128) NOT NULL DEFAULT(''),
                    [Content] nvarchar(max) NOT NULL DEFAULT(''),
                    [SubmittedAt] datetime2 NOT NULL,
                    [Status] nvarchar(64) NOT NULL DEFAULT('Chờ duyệt')
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('ApprovalRequests', 'EmployeeUserId') IS NULL ALTER TABLE [ApprovalRequests] ADD [EmployeeUserId] uniqueidentifier NULL;
            IF COL_LENGTH('ApprovalRequests', 'EmployeeUsername') IS NULL ALTER TABLE [ApprovalRequests] ADD [EmployeeUsername] nvarchar(128) NOT NULL CONSTRAINT [DF_ApprovalRequests_EmployeeUsername] DEFAULT('');
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.FormTemplates', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[FormTemplates]
                (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [Title] nvarchar(256) NOT NULL DEFAULT(''),
                    [Category] nvarchar(128) NOT NULL DEFAULT(''),
                    [Description] nvarchar(max) NOT NULL DEFAULT(''),
                    [FileUrl] nvarchar(512) NOT NULL DEFAULT(''),
                    [LastUpdated] datetime2 NOT NULL DEFAULT(GETUTCDATE())
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.EmployeeMessages', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[EmployeeMessages]
                (
                    [Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [EmployeeUserId] uniqueidentifier NULL,
                    [EmployeeUsername] nvarchar(128) NOT NULL DEFAULT(''),
                    [EmployeeName] nvarchar(256) NOT NULL DEFAULT(''),
                    [Channel] nvarchar(64) NOT NULL DEFAULT('manager'),
                    [SenderRole] nvarchar(64) NOT NULL DEFAULT('Employee'),
                    [SenderName] nvarchar(256) NOT NULL DEFAULT(''),
                    [Title] nvarchar(256) NOT NULL DEFAULT(''),
                    [Content] nvarchar(max) NOT NULL DEFAULT(''),
                    [SentAt] datetime2 NOT NULL,
                    [IsRead] bit NOT NULL DEFAULT(0)
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF COL_LENGTH('EmployeeMessages', 'EmployeeUserId') IS NULL ALTER TABLE [EmployeeMessages] ADD [EmployeeUserId] uniqueidentifier NULL;
            IF COL_LENGTH('EmployeeMessages', 'EmployeeUsername') IS NULL ALTER TABLE [EmployeeMessages] ADD [EmployeeUsername] nvarchar(128) NOT NULL CONSTRAINT [DF_EmployeeMessages_EmployeeUsername] DEFAULT('');
            IF COL_LENGTH('EmployeeMessages', 'Channel') IS NULL ALTER TABLE [EmployeeMessages] ADD [Channel] nvarchar(64) NOT NULL CONSTRAINT [DF_EmployeeMessages_Channel] DEFAULT('manager');
            IF COL_LENGTH('EmployeeMessages', 'SenderRole') IS NULL ALTER TABLE [EmployeeMessages] ADD [SenderRole] nvarchar(64) NOT NULL CONSTRAINT [DF_EmployeeMessages_SenderRole] DEFAULT('Employee');
            IF COL_LENGTH('EmployeeMessages', 'SenderName') IS NULL ALTER TABLE [EmployeeMessages] ADD [SenderName] nvarchar(256) NOT NULL CONSTRAINT [DF_EmployeeMessages_SenderName] DEFAULT('');
            IF COL_LENGTH('EmployeeMessages', 'EditedAtUtc') IS NULL ALTER TABLE [EmployeeMessages] ADD [EditedAtUtc] datetime2 NULL;
            IF COL_LENGTH('EmployeeMessages', 'RevokedAtUtc') IS NULL ALTER TABLE [EmployeeMessages] ADD [RevokedAtUtc] datetime2 NULL;
            IF COL_LENGTH('EmployeeMessages', 'IsRevoked') IS NULL ALTER TABLE [EmployeeMessages] ADD [IsRevoked] bit NOT NULL CONSTRAINT [DF_EmployeeMessages_IsRevoked] DEFAULT(0);
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.EmployeeTasks', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[EmployeeTasks]
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [EmployeeId] uniqueidentifier NOT NULL,
                    [Title] nvarchar(256) NOT NULL DEFAULT(''),
                    [Description] nvarchar(max) NOT NULL DEFAULT(''),
                    [DueDate] datetime2 NOT NULL,
                    [Status] nvarchar(64) NOT NULL DEFAULT('Pending'),
                    [CreatedAt] datetime2 NOT NULL
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.PayrollRecords', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[PayrollRecords]
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [EmployeeId] uniqueidentifier NOT NULL,
                    [Month] int NOT NULL,
                    [Year] int NOT NULL,
                    [BaseSalary] decimal(18,2) NOT NULL,
                    [KpiBonus] decimal(18,2) NOT NULL,
                    [ViolationDeduction] decimal(18,2) NOT NULL,
                    [NetSalary] decimal(18,2) NOT NULL,
                    [Status] nvarchar(64) NOT NULL DEFAULT('Chưa thanh toán'),
                    [CreatedAt] datetime2 NOT NULL,
                    [PaidAt] datetime2 NULL
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.EmployeePreferences', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[EmployeePreferences]
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [UserId] uniqueidentifier NOT NULL,
                    [NotificationsEnabled] bit NOT NULL DEFAULT(1),
                    [CompactMode] bit NOT NULL DEFAULT(0),
                    [ReducedMotion] bit NOT NULL DEFAULT(0),
                    [Language] nvarchar(32) NOT NULL DEFAULT('vi-VN'),
                    [Theme] nvarchar(32) NOT NULL DEFAULT('light'),
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    CONSTRAINT [UX_EmployeePreferences_UserId] UNIQUE ([UserId])
                );
            END
            """);

        context.Database.ExecuteSqlRaw(
            """
            IF OBJECT_ID('dbo.KnowledgeBaseItems', 'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[KnowledgeBaseItems]
                (
                    [Id] uniqueidentifier NOT NULL PRIMARY KEY,
                    [Title] nvarchar(256) NOT NULL DEFAULT(''),
                    [Summary] nvarchar(max) NOT NULL DEFAULT(''),
                    [Category] nvarchar(128) NOT NULL DEFAULT(''),
                    [FileType] nvarchar(32) NOT NULL DEFAULT('LINK'),
                    [Content] nvarchar(max) NOT NULL DEFAULT(''),
                    [ResourceUrl] nvarchar(512) NOT NULL DEFAULT(''),
                    [IsPublished] bit NOT NULL DEFAULT(1),
                    [UpdatedAtUtc] datetime2 NOT NULL
                );
            END
            """);
    }
}
