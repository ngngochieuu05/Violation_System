using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Employee
{
    public class EmployeeMessage
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; }
        public string Title { get; set; }
        public string Content { get; set; }
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; } // Đánh dấu tin nhắn đã đọc hay chưa
    }
}
