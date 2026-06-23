using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
// Thêm using thư mục Data và Models của bạn ở đây

namespace SME_backend.Controllers
{
    [Route("api/variants")]
    [ApiController]
    public class VariantsController : ControllerBase
    {
        private readonly ApplicationDbContext _context; // Đổi tên DbContext nếu cần

        public VariantsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: api/variants?barcode=...
        [HttpGet]
        public async Task<IActionResult> GetVariantByBarcode([FromQuery] string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return BadRequest(new { message = "Barcode không được để trống" });

            // Giả sử bảng DB tên là ProductVariants
            var variant = await _context.ProductVariants
                .FirstOrDefaultAsync(v => v.Barcode == barcode);

            if (variant == null)
                return NotFound(new { message = "Không tìm thấy biến thể với mã vạch này" });

            return Ok(variant);
        }
    }
}