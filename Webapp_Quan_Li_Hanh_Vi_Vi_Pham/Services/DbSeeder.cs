using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public static class DbSeeder
{
    public static void Initialize(ViolationDbContext context)
    {
        // Auto-create database if not exists
        context.Database.EnsureCreated();
        EnsureUserColumns(context);
        EnsureViolationColumns(context);

        // Seed Users
        if (!context.Users.Any())
        {
            var adminUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "admin",
                PasswordHash = PasswordHasher.HashPassword("admin123"),
                FullName = "System Administrator",
                Role = "Admin",
                FaceImagePath = "",
                ManagerKey = "",
                IsKeyActivated = true,
                MustChangePassword = false,
                RequiresInitialSecuritySetup = false,
                Email = "admin@compliancehub.vn",
                Phone = "0987 654 321",
                Department = "Quản trị hệ thống",
                EmployeeCode = "ADM-001",
                CreatedAtUtc = DateTime.UtcNow
            };

            var managerUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "manager",
                PasswordHash = PasswordHasher.HashPassword("manager123"),
                FullName = "General Manager",
                Role = "Manager",
                FaceImagePath = "",
                ManagerKey = "hieudeptraivcl",
                IsKeyActivated = false,
                MustChangePassword = false,
                RequiresInitialSecuritySetup = false,
                Email = "manager@compliancehub.vn",
                Phone = "0912 345 678",
                Department = "Ban Giám Đốc",
                EmployeeCode = "MGR-001",
                CreatedAtUtc = DateTime.UtcNow
            };

            var employeeUser = new User
            {
                Id = Guid.NewGuid(),
                Username = "employee",
                PasswordHash = PasswordHasher.HashPassword("employee123"),
                FullName = "Nguyen Van Employee",
                Role = "Employee",
                FaceImagePath = "",
                ManagerKey = "",
                IsKeyActivated = true,
                MustChangePassword = false,
                RequiresInitialSecuritySetup = false,
                Email = "employee@compliancehub.vn",
                Phone = "0123 456 789",
                Department = "Khối vận hành",
                EmployeeCode = "NV014",
                CreatedAtUtc = DateTime.UtcNow
            };

            context.Users.AddRange(adminUser, managerUser, employeeUser);
        }

        // Seed ModelSettings
        if (!context.ModelSettings.Any())
        {
            var defaultSetting = new ModelSetting
            {
                YoloModelPath = "ML/weights/best.pt",
                YoloConfThreshold = 0.25m,
                YoloIouThreshold = 0.45m,
                DeepfaceConfThreshold = 0.40m,
                IsActive = true
            };
            context.ModelSettings.Add(defaultSetting);
        }

        // Seed AiModels
        if (!context.AiModels.Any())
        {
            var seedModels = new List<AiModel>
            {
                new()
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
                new()
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
                new()
                {
                    Id = Guid.NewGuid(),
                    Name = "DeepFace Face Recognition",
                    Type = "Deepface",
                    ModelPath = "VGG-Face",
                    ConfThreshold = 0.55m,
                    IouThreshold = 0.00m,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            };
            context.AiModels.AddRange(seedModels);
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

        // Seed ViolationRecords
        if (!context.ViolationRecords.Any())
        {
            var seedViolations = new List<ViolationRecord>
            {
                new()
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
                new()
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
                }
            };
            context.ViolationRecords.AddRange(seedViolations);
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

        // Seed AuditLogs
        if (!context.AuditLogs.Any())
        {
            var seedLogs = new List<AuditLog>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddMinutes(-10),
                    Username = "Nguyễn Dương",
                    Action = "Đăng nhập",
                    Details = "Quản lý đã đăng nhập hệ thống",
                    IpAddress = "192.168.1.50",
                    Status = "Thành công"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddMinutes(-30),
                    Username = "admin",
                    Action = "Cập nhật cấu hình",
                    Details = "Cập nhật Model Chấm công - ngưỡng độ tin cậy 85%",
                    IpAddress = "127.0.0.1",
                    Status = "Thành công"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddHours(-1),
                    Username = "System",
                    Action = "Camera",
                    Details = "Camera Tầng 3 - Lối thoát hiểm đã mất kết nối",
                    IpAddress = "127.0.0.1",
                    Status = "Cảnh báo"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddHours(-2),
                    Username = "Trần Văn Minh",
                    Action = "Đăng nhập",
                    Details = "Mật khẩu không chính xác - Lần 1/3",
                    IpAddress = "192.168.1.182",
                    Status = "Lỗi"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddHours(-3),
                    Username = "admin",
                    Action = "Webhook Zalo",
                    Details = "Gửi thông báo về vi phạm qua Webhook Zalo",
                    IpAddress = "127.0.0.1",
                    Status = "Thành công"
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTime.UtcNow.AddHours(-4),
                    Username = "System",
                    Action = "Dọn dẹp dữ liệu",
                    Details = "Xóa dữ liệu cũ hơn 90 ngày (15 bản ghi)",
                    IpAddress = "127.0.0.1",
                    Status = "Thành công"
                }
            };
            context.AuditLogs.AddRange(seedLogs);
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
}
