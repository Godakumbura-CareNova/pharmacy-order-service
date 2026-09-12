using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Models;

namespace PharmacyOrder.Api.Services.Interfaces;

public interface ICartService
{
    Task<CartResponseDto> GetCartAsync(Guid patientId, CancellationToken cancellationToken);

    Task<CartResponseDto> AddNonPrescriptionItemAsync(Guid patientId, AddCartItemRequestDto request, CancellationToken cancellationToken);

    Task<CartResponseDto> UpdateItemAsync(Guid patientId, int cartItemId, UpdateCartItemRequestDto request, CancellationToken cancellationToken);

    Task<CartResponseDto> RemoveItemAsync(Guid patientId, int cartItemId, CancellationToken cancellationToken);

    Task<CartResponseDto> AddPrescriptionItemAsync(Guid patientId, Prescription prescription, CancellationToken cancellationToken);
}
