using System.Threading;
using System.Threading.Tasks;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

public interface IModelSettingService
{
    Task<ModelSetting> GetActiveSettingAsync(CancellationToken cancellationToken = default);
    Task UpdateSettingAsync(ModelSetting setting, CancellationToken cancellationToken = default);
}
