using System.Threading;
using System.Threading.Tasks;
using FashionERP.Application.DTOs.AI;

namespace FashionERP.Application.Interfaces
{
    /// <summary>
    /// Client gọi sang Python AI Service (FastAPI) chạy ở ai-service/.
    /// Implementation thật: FashionERP.Infrastructure.AIClient.AIServiceClient
    /// </summary>
    public interface IAIServiceClient
    {
        Task<ChatbotResponseDto> ChatAsync(AIChatbotProxyRequest request, CancellationToken ct = default);

        Task<SizeRecommendResponseDto> RecommendSizeAsync(AISizeRecommendProxyRequest request, CancellationToken ct = default);

        Task<InventoryForecastResponseDto> ForecastAsync(AIForecastProxyRequest request, CancellationToken ct = default);
    }
}