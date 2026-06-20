namespace FashionERP.Application.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using FashionERP.Application.DTOs.Inventory;

    public interface IInventoryService
    {
        Task<List<InventoryResponseDto>> GetAllAsync(bool? lowStockOnly);
        Task<InventoryResponseDto> GetByVariantIdAsync(Guid variantId);
        Task ImportStockAsync(ImportStockRequestDto request, Guid createdBy);
        Task AdjustStockAsync(AdjustStockRequestDto request, Guid createdBy);
    }
}
