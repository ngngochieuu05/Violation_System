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
                CreatedAtUtc = DateTime.UtcNow
            };

            context.Users.AddRange(adminUser, managerUser);
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
                    ConfThreshold = 0.40m,
                    IouThreshold = 0.00m,
                    IsActive = true,
                    CreatedAtUtc = DateTime.UtcNow
                }
            };
            context.AiModels.AddRange(seedModels);
        }

        // Seed ViolationRecords
        if (!context.ViolationRecords.Any())
        {
            var seedViolations = new List<ViolationRecord>
            {
                new()
                {
                    Id = Guid.NewGuid(),
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

        context.SaveChanges();
    }
}
