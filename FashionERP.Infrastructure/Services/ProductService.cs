using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using FashionERP.Application.Common;
using FashionERP.Application.DTOs.Product;
using FashionERP.Application.Interfaces;
using FashionERP.Domain.Entities;
using FashionERP.Domain.Enums;
using FashionERP.Infrastructure.Data;

namespace FashionERP.Infrastructure.Services
{
    public class ProductService : IProductService
    {
        private readonly AppDbContext _db;
        private readonly IMapper _mapper;

        public ProductService(AppDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        private IQueryable<Product> BaseQuery() =>
            _db.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.Variants).ThenInclude(v => v.Inventory);

        // ─── GET ALL (filter + search) ────────────────────────
        public async Task<List<ProductResponseDto>> GetAllAsync(
            string? status, Guid? categoryId, string? keyword)
        {
            var query = BaseQuery();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<ProductStatus>(status, out var st))
                query = query.Where(p => p.Status == st);

            if (categoryId.HasValue)
                query = query.Where(p => p.CategoryId == categoryId.Value);

            if (!string.IsNullOrEmpty(keyword))
            {
                var kw = keyword.Trim().ToLower();
                query = query.Where(p =>
                    p.Name.ToLower().Contains(kw) ||
                    p.ProductCode.ToLower().Contains(kw) ||
                    (p.Tags != null && p.Tags.ToLower().Contains(kw)));
            }

            var list = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
            return _mapper.Map<List<ProductResponseDto>>(list);
        }

        // ─── GET BY ID ────────────────────────────────────────
        public async Task<ProductResponseDto> GetByIdAsync(Guid id)
        {
            var product = await BaseQuery().FirstOrDefaultAsync(p => p.Id == id)
                ?? throw new NotFoundException("Sản phẩm", id);
            return _mapper.Map<ProductResponseDto>(product);
        }

        // ─── CREATE ───────────────────────────────────────────
        public async Task<ProductResponseDto> CreateAsync(CreateProductRequestDto request)
        {
            if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId))
                throw new NotFoundException("Danh mục", request.CategoryId);

            if (request.BrandId.HasValue && !await _db.Brands.AnyAsync(b => b.Id == request.BrandId.Value))
                throw new NotFoundException("Thương hiệu", request.BrandId.Value);

            if (!Enum.TryParse<ProductGender>(request.Gender, out var gender))
                throw new AppException("Đối tượng sử dụng không hợp lệ");

            if (!Enum.TryParse<ProductStatus>(request.Status, out var status))
                status = ProductStatus.Draft;

            // Sinh ProductCode: PROD-{YYYY}-{số thứ tự 4 chữ số}
            var year = DateTime.UtcNow.Year;
            var countThisYear = await _db.Products
                .CountAsync(p => p.ProductCode.StartsWith($"PROD-{year}-"));
            var productCode = $"PROD-{year}-{(countThisYear + 1):D4}";

            var product = new Product
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                CategoryId = request.CategoryId,
                BrandId = request.BrandId,
                Gender = gender,
                BasePrice = request.BasePrice,
                Tags = request.Tags?.Trim(),
                Status = status,
                ProductCode = productCode
            };

            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            return await GetByIdAsync(product.Id);
        }

        // ─── UPDATE ───────────────────────────────────────────
        public async Task<ProductResponseDto> UpdateAsync(Guid id, UpdateProductRequestDto request)
        {
            var product = await _db.Products.FindAsync(id)
                ?? throw new NotFoundException("Sản phẩm", id);

            if (!await _db.Categories.AnyAsync(c => c.Id == request.CategoryId))
                throw new NotFoundException("Danh mục", request.CategoryId);

            if (request.BrandId.HasValue && !await _db.Brands.AnyAsync(b => b.Id == request.BrandId.Value))
                throw new NotFoundException("Thương hiệu", request.BrandId.Value);

            if (!Enum.TryParse<ProductGender>(request.Gender, out var gender))
                throw new AppException("Đối tượng sử dụng không hợp lệ");

            if (!Enum.TryParse<ProductStatus>(request.Status, out var status))
                throw new AppException("Trạng thái sản phẩm không hợp lệ");

            product.Name = request.Name.Trim();
            product.Description = request.Description?.Trim();
            product.CategoryId = request.CategoryId;
            product.BrandId = request.BrandId;
            product.Gender = gender;
            product.BasePrice = request.BasePrice;
            product.Tags = request.Tags?.Trim();
            product.Status = status;
            product.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return await GetByIdAsync(id);
        }

        // ─── DELETE ───────────────────────────────────────────
        public async Task DeleteAsync(Guid id)
        {
            var product = await _db.Products.FindAsync(id)
                ?? throw new NotFoundException("Sản phẩm", id);

            // Không cho xóa nếu đã có trong đơn hàng
            var hasOrders = await _db.OrderItems
                .AnyAsync(oi => oi.Variant.ProductId == id);
            if (hasOrders)
                throw new BusinessException("Không thể xóa sản phẩm đã có trong đơn hàng. Hãy chuyển trạng thái sang Archived");

            _db.Products.Remove(product);
            await _db.SaveChangesAsync();
        }

        // ─── UPDATE MAIN IMAGE ────────────────────────────────
        public async Task UpdateMainImageAsync(Guid id, string imageUrl, string publicId)
        {
            var product = await _db.Products.FindAsync(id)
                ?? throw new NotFoundException("Sản phẩm", id);

            product.MainImageUrl = imageUrl;
            product.MainImagePublicId = publicId;
            product.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // ─── ADD VARIANT ──────────────────────────────────────
        public async Task<ProductVariantResponseDto> AddVariantAsync(CreateVariantRequestDto request)
        {
            if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId))
                throw new NotFoundException("Sản phẩm", request.ProductId);

            if (!Enum.TryParse<ProductSize>(request.Size, out var size))
                throw new AppException("Kích cỡ không hợp lệ");

            // Kiểm tra trùng (productId, size, color)
            var dup = await _db.ProductVariants.AnyAsync(v =>
                v.ProductId == request.ProductId &&
                v.Size == size &&
                v.Color.ToLower() == request.Color.Trim().ToLower());
            if (dup)
                throw new DuplicateException($"Biến thể {request.Size} - {request.Color} đã tồn tại cho sản phẩm này");

            // Sinh SKU: {ProductCode}-{SIZE}-{COLOR_UPPER_NO_SPACE}
            var product = await _db.Products.FindAsync(request.ProductId)!;
            var colorKey = request.Color.Trim().ToUpper().Replace(" ", "");
            var sku = $"{product!.ProductCode}-{request.Size}-{colorKey}";

            // Nếu SKU trùng thì thêm hậu tố số
            var skuBase = sku;
            var suffix = 1;
            while (await _db.ProductVariants.AnyAsync(v => v.Sku == sku))
                sku = $"{skuBase}-{suffix++}";

            var variant = new ProductVariant
            {
                ProductId = request.ProductId,
                Size = size,
                Color = request.Color.Trim(),
                ColorHex = request.ColorHex?.Trim(),
                Sku = sku,
                Barcode = request.Barcode?.Trim(),
                Price = request.Price,
                IsActive = true
            };

            _db.ProductVariants.Add(variant);

            // Tạo bản ghi Inventory cho variant mới (quantity = 0)
            var inventory = new Inventory
            {
                VariantId = variant.Id,
                Quantity = 0,
                MinStock = 5
            };
            _db.Inventories.Add(inventory);

            await _db.SaveChangesAsync();

            await _db.Entry(variant).Reference(v => v.Inventory).LoadAsync();
            return _mapper.Map<ProductVariantResponseDto>(variant);
        }

        // ─── UPDATE VARIANT ───────────────────────────────────
        public async Task<ProductVariantResponseDto> UpdateVariantAsync(
            Guid variantId, CreateVariantRequestDto request)
        {
            var variant = await _db.ProductVariants
                .Include(v => v.Inventory)
                .FirstOrDefaultAsync(v => v.Id == variantId)
                ?? throw new NotFoundException("Biến thể sản phẩm", variantId);

            if (!Enum.TryParse<ProductSize>(request.Size, out var size))
                throw new AppException("Kích cỡ không hợp lệ");

            // Kiểm tra trùng với biến thể KHÁC cùng product
            var dup = await _db.ProductVariants.AnyAsync(v =>
                v.ProductId == variant.ProductId &&
                v.Size == size &&
                v.Color.ToLower() == request.Color.Trim().ToLower() &&
                v.Id != variantId);
            if (dup)
                throw new DuplicateException($"Biến thể {request.Size} - {request.Color} đã tồn tại cho sản phẩm này");

            // Kiểm tra barcode trùng (nếu có)
            if (!string.IsNullOrEmpty(request.Barcode) &&
                await _db.ProductVariants.AnyAsync(v =>
                    v.Barcode == request.Barcode && v.Id != variantId))
                throw new DuplicateException($"Barcode '{request.Barcode}' đã được sử dụng bởi biến thể khác");

            variant.Size = size;
            variant.Color = request.Color.Trim();
            variant.ColorHex = request.ColorHex?.Trim();
            variant.Price = request.Price;
            variant.Barcode = request.Barcode?.Trim();
            variant.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return _mapper.Map<ProductVariantResponseDto>(variant);
        }

        // ─── DELETE VARIANT ───────────────────────────────────
        public async Task DeleteVariantAsync(Guid variantId)
        {
            var variant = await _db.ProductVariants.FindAsync(variantId)
                ?? throw new NotFoundException("Biến thể sản phẩm", variantId);

            if (await _db.OrderItems.AnyAsync(oi => oi.VariantId == variantId))
                throw new BusinessException("Không thể xóa biến thể đã có trong đơn hàng");

            _db.ProductVariants.Remove(variant);
            await _db.SaveChangesAsync();
        }
    }
}


