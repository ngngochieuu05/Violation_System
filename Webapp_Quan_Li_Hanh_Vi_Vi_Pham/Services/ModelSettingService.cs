using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services;

public class ModelSettingService : IModelSettingService
{
    private readonly ViolationDbContext _context;

    public ModelSettingService(ViolationDbContext context)
    {
        _context = context;
    }

    public async Task<ModelSetting> GetActiveSettingAsync(CancellationToken cancellationToken = default)
    {
        var setting = await _context.ModelSettings.FirstOrDefaultAsync(s => s.IsActive, cancellationToken);
        if (setting == null)
        {
            setting = new ModelSetting
            {
                YoloModelPath = "ML/weights/best.pt",
                YoloConfThreshold = 0.25m,
                YoloIouThreshold = 0.45m,
                DeepfaceConfThreshold = 0.40m,
                IsActive = true
            };
            _context.ModelSettings.Add(setting);
            await _context.SaveChangesAsync(cancellationToken);
        }
        return setting;
    }

    public async Task UpdateSettingAsync(ModelSetting setting, CancellationToken cancellationToken = default)
    {
        var active = await GetActiveSettingAsync(cancellationToken);
        active.YoloModelPath = setting.YoloModelPath;
        active.YoloConfThreshold = setting.YoloConfThreshold;
        active.YoloIouThreshold = setting.YoloIouThreshold;
        active.DeepfaceConfThreshold = setting.DeepfaceConfThreshold;
        
        _context.ModelSettings.Update(active);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
