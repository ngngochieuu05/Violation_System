using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Employee
{
    public class WorkSession
    {
        public int Id { get; set; }
        public string EmployeeId { get; set; }
        public string EmployeeName { get; set; }
        public DateTime Date { get; set; }
        public TimeSpan CheckInTime { get; set; }
        public TimeSpan? CheckOutTime { get; set; }
        public string Status { get; set; } // Ví dụ: "Đúng giờ", "Vi phạm đi muộn"
        public string Notes { get; set; }
    }
}
