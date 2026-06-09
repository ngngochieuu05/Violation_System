using System;
using System.Threading;
using System.Threading.Tasks;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.Interfaces;

public interface IUserService
{
    Task<User?> AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> RegisterAsync(User user, string plainPassword, string faceImageBase64, CancellationToken cancellationToken = default);
    Task<bool> ActivateManagerKeyAsync(string username, string key, CancellationToken cancellationToken = default);
    Task<bool> UpdateBiometricImageAsync(Guid userId, string faceImageBase64, CancellationToken cancellationToken = default);
    Task<bool> VerifyBiometricsAsync(string username, string faceImageBase64, CancellationToken cancellationToken = default);
}
