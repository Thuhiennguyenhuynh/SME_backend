using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FashionERP.Application.Common;
using FashionERP.Application.Interfaces;
using FashionERP.Domain.Entities;
using FashionERP.Infrastructure.Data;

namespace FashionERP.API.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UsersController : BaseController
    {
        private readonly AppDbContext _db;
        private readonly IPasswordHasher _hasher;

        public UsersController(AppDbContext db, IPasswordHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public record CreateUserDto(
            string Email,
            string Password,
            string Role,
            Guid? EmployeeId);

        /// <summary>Lấy danh sách tài khoản</summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var users = await _db.Users
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.Role,
                    u.EmployeeId,
                    u.IsActive,
                    u.LastLoginAt
                })
                .ToListAsync();
            return Ok(users);
        }

        /// <summary>Admin tạo tài khoản mới</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateUserDto request)
        {
            var validRoles = new[] { "Admin", "Manager", "Sales", "Warehouse", "Accountant" };
            if (!Array.Exists(validRoles, r => r == request.Role))
                return BadRequest("Role không hợp lệ");

            if (await _db.Users.AnyAsync(u => u.Email == request.Email))
                throw new BusinessException("Email đã tồn tại trong hệ thống");

            var user = new User
            {
                Email = request.Email.Trim().ToLower(),
                PasswordHash = _hasher.Hash(request.Password),
                Role = request.Role,
                EmployeeId = request.EmployeeId,
                IsActive = true
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            return Created(new { user.Id, user.Email, user.Role }, "Tạo tài khoản thành công");
        }

        /// <summary>Admin bật/tắt tài khoản</summary>
        [HttpPatch("{id:guid}/toggle-active")]
        public async Task<IActionResult> ToggleActive(Guid id)
        {
            var user = await _db.Users.FindAsync(id)
                ?? throw new NotFoundException("Tài khoản", id);
            user.IsActive = !user.IsActive;
            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok<object>(null!, user.IsActive ? "Kích hoạt tài khoản thành công" : "Vô hiệu hoá tài khoản thành công");
        }
    }
}