using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

public class UserFaceEmbedding
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string EmbeddingJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    // Navigation property
    public User? User { get; set; }
}
