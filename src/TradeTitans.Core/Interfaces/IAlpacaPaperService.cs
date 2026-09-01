using TradeTitans.Core.DTOs.Alpaca;

namespace TradeTitans.Core.Interfaces;

public interface IAlpacaPaperService
{
    Task<AlpacaAccountDto?> GetAccountAsync(CancellationToken cancellationToken = default);
    Task<List<AlpacaPositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default);
    Task<AlpacaOrderResponseDto?> SubmitOrderAsync(AlpacaOrderRequestDto orderRequest, CancellationToken cancellationToken = default);
}
