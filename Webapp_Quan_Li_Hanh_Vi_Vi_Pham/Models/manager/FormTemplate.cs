using System;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Manager
{
    public class FormTemplate
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public DateTime LastUpdated { get; set; }
        public string FileUrl { get; set; }
    }
}