using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Thành công", "Cảnh báo", "Lỗi"
    }
}
