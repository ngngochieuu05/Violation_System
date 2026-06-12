using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Models.Entities;

namespace Webapp_Quan_Li_Hanh_Vi_Vi_Pham.Services.AI;

public class InternalAiChatService : IInternalAiChatService
{
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Employee",
        "Manager"
    };

    private readonly HttpClient _httpClient;
    private readonly ViolationDbContext _context;
    private readonly GeminiChatOptions _options;
    private readonly ILogger<InternalAiChatService> _logger;

    public InternalAiChatService(
        HttpClient httpClient,
        ViolationDbContext context,
        IOptions<GeminiChatOptions> options,
        ILogger<InternalAiChatService> logger)
    {
        _httpClient = httpClient;
        _context = context;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InternalChatResponse> AskAsync(
        ClaimsPrincipal principal,
        InternalChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            return new InternalChatResponse
            {
                Success = false,
                Message = "Chat AI nội bộ hiện chưa được cấu hình."
            };
        }

        var role = principal.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
        if (!AllowedRoles.Contains(role))
        {
            return new InternalChatResponse
            {
                Success = false,
                Message = "Bạn không có quyền sử dụng trợ lý AI nội bộ."
            };
        }

        var normalizedMessage = (request.Message ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedMessage))
        {
            return new InternalChatResponse
            {
                Success = false,
                Message = "Vui lòng nhập nội dung cần hỏi."
            };
        }

        var username = principal.Identity?.Name ?? string.Empty;
        var userIdRaw = principal.FindFirst("UserId")?.Value;
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            return new InternalChatResponse
            {
                Success = false,
                Message = "Không xác định được danh tính người dùng."
            };
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new InternalChatResponse
            {
                Success = false,
                Message = "Không tìm thấy hồ sơ người dùng."
            };
        }

        var contextText = await BuildSystemContextAsync(user, role, username, cancellationToken);
        var history = request.History
            .Where(item => !string.IsNullOrWhiteSpace(item.Content))
            .TakeLast(8)
            .ToList();

        var payload = BuildGeminiPayload(contextText, history, normalizedMessage);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{_options.Model}:generateContent");
        httpRequest.Headers.Add("X-goog-api-key", _options.ApiKey);
        httpRequest.Content = JsonContent.Create(payload);

        try
        {
            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Gemini internal chat failed: {StatusCode} {Body}", response.StatusCode, body);
                return new InternalChatResponse
                {
                    Success = false,
                    Message = "Không thể lấy phản hồi từ trợ lý AI nội bộ."
                };
            }

            using var document = JsonDocument.Parse(body);
            var answer = ExtractText(document.RootElement);
            return new InternalChatResponse
            {
                Success = !string.IsNullOrWhiteSpace(answer),
                Message = string.IsNullOrWhiteSpace(answer)
                    ? "Tôi chỉ có thể trả lời dựa trên dữ liệu vi phạm và thông tin nội bộ hiện có trong hệ thống."
                    : answer.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Gemini internal chat threw.");
            return new InternalChatResponse
            {
                Success = false,
                Message = "Không thể kết nối tới trợ lý AI nội bộ."
            };
        }
    }

    private async Task<string> BuildSystemContextAsync(
        User user,
        string role,
        string username,
        CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("THÔNG TIN TÀI KHOẢN HIỆN TẠI");
        builder.AppendLine($"- Họ tên: {user.FullName}");
        builder.AppendLine($"- Username: {user.Username}");
        builder.AppendLine($"- Vai trò: {user.Role}");
        builder.AppendLine($"- Mã nhân viên: {user.EmployeeCode}");
        builder.AppendLine($"- Email: {user.Email}");
        builder.AppendLine($"- Số điện thoại: {user.Phone}");
        builder.AppendLine($"- Phòng ban: {user.Department}");
        builder.AppendLine($"- Ngày tạo: {user.CreatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine();

        if (string.Equals(role, "Employee", StringComparison.OrdinalIgnoreCase))
        {
            var employeeViolations = await _context.ViolationRecords
                .Where(v => v.EmployeeCode == user.EmployeeCode || v.EmployeeCode == username)
                .OrderByDescending(v => v.DetectedAtUtc)
                .Take(20)
                .ToListAsync(cancellationToken);

            builder.AppendLine("DỮ LIỆU VI PHẠM CỦA CHÍNH NHÂN VIÊN");
            AppendViolations(builder, employeeViolations);
            return builder.ToString();
        }

        var systemViolations = await _context.ViolationRecords
            .OrderByDescending(v => v.DetectedAtUtc)
            .Take(30)
            .ToListAsync(cancellationToken);
        var totalViolationsToday = await _context.ViolationRecords
            .CountAsync(v => v.DetectedAtUtc >= DateTime.UtcNow.Date, cancellationToken);
        var pendingViolations = await _context.ViolationRecords
            .CountAsync(v => v.Status == "Pending", cancellationToken);

        builder.AppendLine("TỔNG QUAN VI PHẠM HỆ THỐNG");
        builder.AppendLine($"- Tổng vi phạm hôm nay: {totalViolationsToday}");
        builder.AppendLine($"- Vi phạm đang chờ xử lý: {pendingViolations}");
        builder.AppendLine();
        builder.AppendLine("DANH SÁCH VI PHẠM GẦN NHẤT");
        AppendViolations(builder, systemViolations);

        return builder.ToString();
    }

    private static void AppendViolations(StringBuilder builder, IReadOnlyCollection<ViolationRecord> violations)
    {
        if (violations.Count == 0)
        {
            builder.AppendLine("- Không có dữ liệu vi phạm phù hợp.");
            return;
        }

        foreach (var violation in violations)
        {
            builder.AppendLine(
                $"- [{violation.DetectedAtUtc:yyyy-MM-dd HH:mm:ss} UTC] {violation.EmployeeName} / {violation.EmployeeCode}: {violation.ViolationType}; Mức độ: {violation.Severity}; Trạng thái: {violation.Status}; Camera: {violation.CameraLocation}");
        }
    }

    private static object BuildGeminiPayload(
        string systemContext,
        IReadOnlyCollection<InternalChatMessage> history,
        string userMessage)
    {
        var contents = new List<object>
        {
            new
            {
                role = "user",
                parts = new[]
                {
                    new
                    {
                        text =
                            """
                            Bạn là trợ lý AI nội bộ của hệ thống quản lý hành vi vi phạm.
                            Chỉ được phép trả lời dựa trên phần CONTEXT được cung cấp trong yêu cầu này.
                            Chỉ trả lời các câu hỏi liên quan tới:
                            1. Thông tin nội bộ của tài khoản hiện tại.
                            2. Dữ liệu vi phạm trong hệ thống thuộc phạm vi người dùng được phép xem.

                            Quy tắc bắt buộc:
                            - Không được trả lời kiến thức ngoài hệ thống.
                            - Không suy đoán nếu CONTEXT không có dữ liệu.
                            - Nếu câu hỏi ngoài phạm vi hoặc CONTEXT không đủ, trả lời ngắn: "Tôi chỉ có thể hỗ trợ thông tin vi phạm và thông tin nội bộ tài khoản trong hệ thống."
                            - Trả lời bằng tiếng Việt, ngắn gọn, rõ ràng.

                            CONTEXT:
                            """ + "\n" + systemContext
                    }
                }
            }
        };

        foreach (var item in history)
        {
            contents.Add(new
            {
                role = string.Equals(item.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[]
                {
                    new
                    {
                        text = item.Content
                    }
                }
            });
        }

        contents.Add(new
        {
            role = "user",
            parts = new[]
            {
                new
                {
                    text = userMessage
                }
            }
        });

        return new
        {
            contents,
            generationConfig = new
            {
                temperature = 0.2
            }
        };
    }

    private static string ExtractText(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out var content) ||
                !content.TryGetProperty("parts", out var parts) ||
                parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }
}
