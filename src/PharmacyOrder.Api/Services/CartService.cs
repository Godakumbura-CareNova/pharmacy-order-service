using Microsoft.EntityFrameworkCore;
using PharmacyOrder.Api.Data;
using PharmacyOrder.Api.DTOs.Cart;
using PharmacyOrder.Api.Models;
using PharmacyOrder.Api.Services.Interfaces;

namespace PharmacyOrder.Api.Services;

public class CartService : ICartService
{
    private readonly AppDbContext _db;

    public CartService(AppDbContext db) => _db = db;

    public Task<CartResponseDto> GetCartAsync(Guid patientId, CancellationToken cancellationToken) =>
        GetCartResponseAsync(patientId, cancellationToken);

    public async Task<CartResponseDto> AddNonPrescriptionItemAsync(Guid patientId, AddCartItemRequestDto request, CancellationToken cancellationToken)
    {
        var medicine = await _db.Medicines.SingleAsync(item => item.Id == request.MedicineId, cancellationToken);

        if (medicine.PrescriptionRequired)
            throw new ArgumentException("Prescription medicines must be added through prescription upload.");

        var cart = await _db.Carts
            .Include(existingCart => existingCart.Items)
            .SingleOrDefaultAsync(existingCart => existingCart.CustomerId == patientId, cancellationToken);

        if (cart is null)
        {
            cart = new Cart { CustomerId = patientId };
            _db.Carts.Add(cart);
        }

        var item = cart.Items.SingleOrDefault(existingItem => existingItem.MedicineId == medicine.Id && existingItem.PrescriptionId is null);
        var quantity = (item?.Quantity ?? 0) + request.Quantity;

        if (request.Quantity < 1 || quantity > medicine.StockQuantity)
            throw new ArgumentException($"Quantity must be between 1 and {medicine.StockQuantity}.");

        if (item is null)
        {
            cart.Items.Add(new CartItem { MedicineId = medicine.Id, Quantity = request.Quantity });
        }
        else
        {
            item.Quantity = quantity;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetCartResponseAsync(patientId, cancellationToken);
    }

    public async Task<CartResponseDto> UpdateItemAsync(Guid patientId, int cartItemId, UpdateCartItemRequestDto request, CancellationToken cancellationToken)
    {
        var item = await _db.CartItems
            .Include(existingItem => existingItem.Medicine)
            .SingleOrDefaultAsync(existingItem => existingItem.Id == cartItemId && existingItem.Cart!.CustomerId == patientId,
                cancellationToken) ?? throw new KeyNotFoundException("Cart item was not found.");

        var maximumQuantity = item.Medicine?.StockQuantity ?? 0;
        if (request.Quantity < 1 || request.Quantity > maximumQuantity)
            throw new ArgumentException($"Quantity must be between 1 and {maximumQuantity}.");

        item.Quantity = request.Quantity;
        await _db.SaveChangesAsync(cancellationToken);
        return await GetCartResponseAsync(patientId, cancellationToken);
    }

    public async Task<CartResponseDto> RemoveItemAsync(Guid patientId, int cartItemId, CancellationToken cancellationToken)
    {
        var item = await _db.CartItems.SingleOrDefaultAsync(existingItem =>
            existingItem.Id == cartItemId && existingItem.Cart!.CustomerId == patientId,
            cancellationToken) ?? throw new KeyNotFoundException("Cart item was not found.");

        _db.CartItems.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
        return await GetCartResponseAsync(patientId, cancellationToken);
    }

    public async Task<CartResponseDto> AddPrescriptionItemAsync(Guid patientId, Prescription prescription, CancellationToken cancellationToken)
    {
        var ownedPrescription = await _db.Prescriptions.SingleOrDefaultAsync(item =>
            item.Id == prescription.Id && item.UploadedBy == patientId,
            cancellationToken) ?? throw new KeyNotFoundException("Prescription was not found.");

        var cart = await _db.Carts
            .Include(existingCart => existingCart.Items)
            .SingleOrDefaultAsync(existingCart => existingCart.CustomerId == patientId, cancellationToken);

        if (cart is null)
        {
            cart = new Cart { CustomerId = patientId };
            _db.Carts.Add(cart);
        }

        if (cart.Items.All(item => item.PrescriptionId != ownedPrescription.Id))
        {
            cart.Items.Add(new CartItem
            {
                MedicineId = ownedPrescription.MedicineId,
                PrescriptionId = ownedPrescription.Id,
                Quantity = ownedPrescription.Quantity
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await GetCartResponseAsync(patientId, cancellationToken);
    }

    private async Task<CartResponseDto> GetCartResponseAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var items = await _db.CartItems
            .Where(item => item.Cart!.CustomerId == patientId)
            .Include(item => item.Medicine)
            .Include(item => item.Prescription)
            .OrderBy(item => item.Medicine!.Name)
            .Select(item => new CartItemResponseDto
            {
                Id = item.Id,
                MedicineId = item.MedicineId,
                MedicineName = item.Medicine!.Name,
                MedicineImagePath = item.Medicine.ImagePath,
                UnitPrice = item.Medicine.Price,
                Quantity = item.Quantity,
                LineTotal = item.Medicine.Price * item.Quantity,
                PrescriptionId = item.PrescriptionId,
                PrescriptionStatus = item.Prescription == null ? null : item.Prescription.Status
            })
            .ToListAsync(cancellationToken);

        var readyItems = items.Where(item => item.PrescriptionId is null || item.PrescriptionStatus == "Verified").ToList();
        var pendingItems = items.Where(item => item.PrescriptionStatus == "PendingVerification").ToList();
        var attentionItems = items.Where(item => item.PrescriptionId is not null && item.PrescriptionStatus != "Verified" && item.PrescriptionStatus != "PendingVerification").ToList();

        return new CartResponseDto
        {
            ReadyItems = readyItems,
            PendingItems = pendingItems,
            AttentionItems = attentionItems,
            ReadySubtotal = readyItems.Sum(item => item.LineTotal),
            PendingSubtotal = pendingItems.Sum(item => item.LineTotal),
            AttentionSubtotal = attentionItems.Sum(item => item.LineTotal),
            CartTotal = items.Sum(item => item.LineTotal)
        };
    }
}
