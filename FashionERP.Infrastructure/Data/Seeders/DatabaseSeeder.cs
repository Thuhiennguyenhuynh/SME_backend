namespace FashionERP.Infrastructure.Data.Seeders
{
    using System;
    using System.Threading.Tasks;

    /// <summary>
    /// Điểm vào duy nhất để chạy toàn bộ seed data.
    /// Thứ tự seed tuân theo phụ thuộc khóa ngoại (cha trước con).
    /// Mỗi seeder tự kiểm tra AnyAsync() để đảm bảo idempotent -
    /// chạy lại nhiều lần không tạo dữ liệu trùng.
    /// </summary>
    public static class DatabaseSeeder
    {
        public static async Task SeedAllAsync(AppDbContext db)
        {
            Console.WriteLine("========================================");
            Console.WriteLine("[Seeder] Bắt đầu seed dữ liệu mẫu...");
            Console.WriteLine("========================================");

            // 1. HR (không phụ thuộc bảng khác)
            await DepartmentSeeder.SeedAsync(db);
            await EmployeeSeeder.SeedAsync(db);     // phụ thuộc Department
            await UserSeeder.SeedAsync(db);         // phụ thuộc Employee

            // 2. Catalog
            await CategoryBrandSeeder.SeedAsync(db);
            await ProductSeeder.SeedAsync(db);      // phụ thuộc Category, Brand
            await VariantInventorySeeder.SeedAsync(db); // phụ thuộc Product

            // 3. Customers & Promotions (độc lập)
            await CustomerSeeder.SeedAsync(db);
            await PromotionSeeder.SeedAsync(db);
            await SizeChartSeeder.SeedAsync(db);

            Console.WriteLine("========================================");
            Console.WriteLine("[Seeder] ✅ Hoàn tất seed dữ liệu mẫu!");
            Console.WriteLine("========================================");
        }
    }
}
