using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager
{
    public class ApprovalRequest
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; }
        public string RequestType { get; set; } // "Đơn xin nghỉ", "Tin nhắn"
        public string Content { get; set; }
        public DateTime SubmittedAt { get; set; }
        public string Status { get; set; } // "Chờ duyệt", "Đã duyệt", "Từ chối"
    }
}