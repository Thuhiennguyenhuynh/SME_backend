using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using FashionERP.Application.Common;
using FashionERP.Application.DTOs.HR;
using FashionERP.Application.Interfaces;
using FashionERP.Domain.Entities;
using FashionERP.Domain.Enums;
using FashionERP.Infrastructure.Data;

namespace FashionERP.Infrastructure.Services
{
    public class AttendanceService : IAttendanceService
    {
        private readonly AppDbContext _db;
        private readonly IMapper _mapper;

        public AttendanceService(AppDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        private IQueryable<Attendance> BaseQuery() =>
            _db.Attendances.Include(a => a.Employee);

        // ─── CHECK IN ─────────────────────────────────────────
        public async Task<AttendanceResponseDto> CheckInAsync(CheckInRequestDto request)
        {
            if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
                throw new NotFoundException("Nhân viên", request.EmployeeId);

            var today = DateTime.UtcNow.Date;
            var existing = await _db.Attendances
                .FirstOrDefaultAsync(a => a.EmployeeId == request.EmployeeId && a.WorkDate == today);

            if (existing != null)
                throw new BusinessException($"Nhân viên đã check-in hôm nay lúc {existing.CheckIn:HH:mm}");

            var checkInTime = DateTime.UtcNow;
            // Quy định giờ làm: 8:00 sáng, trễ sau 8:15
            var startOfDay = today.AddHours(8);
            var lateThreshold = today.AddHours(8).AddMinutes(15);
            var type = checkInTime > lateThreshold ? AttendanceType.Late : AttendanceType.Normal;

            var attendance = new Attendance
            {
                EmployeeId = request.EmployeeId,
                WorkDate = today,
                CheckIn = checkInTime,
                Type = type,
                OvertimeHours = 0,
                Note = request.Note?.Trim()
            };

            _db.Attendances.Add(attendance);
            await _db.SaveChangesAsync();

            await _db.Entry(attendance).Reference(a => a.Employee).LoadAsync();
            return _mapper.Map<AttendanceResponseDto>(attendance);
        }

        // ─── CHECK OUT ────────────────────────────────────────
        public async Task<AttendanceResponseDto> CheckOutAsync(CheckOutRequestDto request)
        {
            var attendance = await BaseQuery()
                .FirstOrDefaultAsync(a =>
                    a.EmployeeId == request.EmployeeId &&
                    a.WorkDate == request.WorkDate.Date)
                ?? throw new BusinessException("Không tìm thấy bản ghi check-in cho ngày này");

            if (attendance.CheckOut.HasValue)
                throw new BusinessException($"Nhân viên đã check-out lúc {attendance.CheckOut:HH:mm}");

            var checkOutTime = DateTime.UtcNow;

            if (attendance.CheckIn.HasValue && checkOutTime <= attendance.CheckIn.Value)
                throw new BusinessException("Giờ check-out phải sau giờ check-in");

            attendance.CheckOut = checkOutTime;

            if (attendance.CheckIn.HasValue)
            {
                var totalHours = (decimal)(checkOutTime - attendance.CheckIn.Value).TotalHours;
                attendance.TotalHours = Math.Round(totalHours, 2);

                // Giờ làm thêm: vượt quá 8 tiếng/ngày
                attendance.OvertimeHours = totalHours > 8 ? Math.Round(totalHours - 8, 2) : 0;

                // Kiểm tra về sớm: ra trước 17:30
                var endOfDay = request.WorkDate.Date.AddHours(17).AddMinutes(30);
                if (checkOutTime < endOfDay && attendance.Type == AttendanceType.Normal)
                    attendance.Type = AttendanceType.EarlyLeave;
            }

            attendance.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return _mapper.Map<AttendanceResponseDto>(attendance);
        }

        // ─── CREATE MANUAL (Admin tạo thủ công) ──────────────
        public async Task<AttendanceResponseDto> CreateManualAsync(CreateAttendanceManualDto request)
        {
            if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
                throw new NotFoundException("Nhân viên", request.EmployeeId);

            var workDate = request.WorkDate.Date;
            if (await _db.Attendances.AnyAsync(a =>
                a.EmployeeId == request.EmployeeId && a.WorkDate == workDate))
                throw new DuplicateException($"Đã có bản ghi chấm công ngày {workDate:dd/MM/yyyy} cho nhân viên này");

            if (!Enum.TryParse<AttendanceType>(request.Type, out var type))
                throw new AppException("Loại chấm công không hợp lệ");

            decimal? totalHours = null;
            decimal overtimeHours = 0;
            if (request.CheckIn.HasValue && request.CheckOut.HasValue)
            {
                if (request.CheckOut <= request.CheckIn)
                    throw new BusinessException("Giờ ra phải sau giờ vào");
                totalHours = Math.Round((decimal)(request.CheckOut.Value - request.CheckIn.Value).TotalHours, 2);
                overtimeHours = totalHours.Value > 8 ? Math.Round(totalHours.Value - 8, 2) : 0;
            }

            var attendance = new Attendance
            {
                EmployeeId = request.EmployeeId,
                WorkDate = workDate,
                CheckIn = request.CheckIn,
                CheckOut = request.CheckOut,
                TotalHours = totalHours,
                OvertimeHours = overtimeHours,
                Type = type,
                Note = request.Note?.Trim()
            };

            _db.Attendances.Add(attendance);
            await _db.SaveChangesAsync();
            await _db.Entry(attendance).Reference(a => a.Employee).LoadAsync();
            return _mapper.Map<AttendanceResponseDto>(attendance);
        }

        // ─── GET BY EMPLOYEE / MONTH / YEAR ──────────────────
        public async Task<List<AttendanceResponseDto>> GetByEmployeeAsync(
            Guid employeeId, int month, int year)
        {
            if (!await _db.Employees.AnyAsync(e => e.Id == employeeId))
                throw new NotFoundException("Nhân viên", employeeId);

            var list = await BaseQuery()
                .Where(a => a.EmployeeId == employeeId &&
                            a.WorkDate.Month == month &&
                            a.WorkDate.Year == year)
                .OrderBy(a => a.WorkDate)
                .ToListAsync();
            return _mapper.Map<List<AttendanceResponseDto>>(list);
        }
    }

    // ==================================================
    // FILE: backend/FashionERP/FashionERP.Infrastructure/Services/LeaveService.cs
    // ==================================================
    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _db;
        private readonly IMapper _mapper;

        public LeaveService(AppDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        private IQueryable<Leave> BaseQuery() =>
            _db.Leaves
                .Include(l => l.Employee)
                .Include(l => l.Approver);

        // ─── CREATE ───────────────────────────────────────────
        public async Task<LeaveResponseDto> CreateAsync(CreateLeaveRequestDto request)
        {
            if (!await _db.Employees.AnyAsync(e => e.Id == request.EmployeeId))
                throw new NotFoundException("Nhân viên", request.EmployeeId);

            var fromDate = request.FromDate.Date;
            var toDate = request.ToDate.Date;

            if (toDate < fromDate)
                throw new BusinessException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu nghỉ");

            // Kiểm tra đơn trùng (overlap)
            var overlap = await _db.Leaves.AnyAsync(l =>
                l.EmployeeId == request.EmployeeId &&
                l.Status != LeaveStatus.Rejected &&
                l.FromDate <= toDate &&
                l.ToDate >= fromDate);

            if (overlap)
                throw new BusinessException("Nhân viên đã có đơn nghỉ phép trong khoảng thời gian này");

            var days = (int)(toDate - fromDate).TotalDays + 1;

            var leave = new Leave
            {
                EmployeeId = request.EmployeeId,
                FromDate = fromDate,
                ToDate = toDate,
                Days = days,
                Reason = request.Reason.Trim(),
                Status = LeaveStatus.Pending
            };

            _db.Leaves.Add(leave);
            await _db.SaveChangesAsync();
            await _db.Entry(leave).Reference(l => l.Employee).LoadAsync();
            return _mapper.Map<LeaveResponseDto>(leave);
        }

        // ─── APPROVE / REJECT ─────────────────────────────────
        public async Task<LeaveResponseDto> ApproveAsync(
            Guid id, ApproveLeaveRequestDto request, Guid approverId)
        {
            var leave = await BaseQuery().FirstOrDefaultAsync(l => l.Id == id)
                ?? throw new NotFoundException("Đơn nghỉ phép", id);

            if (leave.Status != LeaveStatus.Pending)
                throw new BusinessException("Chỉ có thể duyệt/từ chối đơn đang ở trạng thái Pending");

            if (!Enum.TryParse<LeaveStatus>(request.Status, out var status))
                throw new AppException("Trạng thái không hợp lệ");

            leave.Status = status;
            leave.ApprovedBy = approverId;
            leave.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return _mapper.Map<LeaveResponseDto>(leave);
        }

        // ─── GET BY EMPLOYEE ──────────────────────────────────
        public async Task<List<LeaveResponseDto>> GetByEmployeeAsync(Guid employeeId)
        {
            if (!await _db.Employees.AnyAsync(e => e.Id == employeeId))
                throw new NotFoundException("Nhân viên", employeeId);

            var list = await BaseQuery()
                .Where(l => l.EmployeeId == employeeId)
                .OrderByDescending(l => l.FromDate)
                .ToListAsync();
            return _mapper.Map<List<LeaveResponseDto>>(list);
        }

        // ─── GET PENDING ──────────────────────────────────────
        public async Task<List<LeaveResponseDto>> GetPendingAsync()
        {
            var list = await BaseQuery()
                .Where(l => l.Status == LeaveStatus.Pending)
                .OrderBy(l => l.CreatedAt)
                .ToListAsync();
            return _mapper.Map<List<LeaveResponseDto>>(list);
        }
    }

    // ==================================================
    // FILE: backend/FashionERP/FashionERP.Infrastructure/Services/PayrollService.cs
    // ==================================================
    public class PayrollService : IPayrollService
    {
        private readonly AppDbContext _db;
        private readonly IMapper _mapper;

        public PayrollService(AppDbContext db, IMapper mapper)
        {
            _db = db;
            _mapper = mapper;
        }

        private IQueryable<Payroll> BaseQuery() =>
            _db.Payrolls.Include(p => p.Employee);

        // ─── GENERATE PAYROLL ─────────────────────────────────
        public async Task<PayrollResponseDto> GenerateAsync(GeneratePayrollRequestDto request)
        {
            var emp = await _db.Employees.FindAsync(request.EmployeeId)
                ?? throw new NotFoundException("Nhân viên", request.EmployeeId);

            // Chỉ tạo 1 bảng lương/tháng/nhân viên
            if (await _db.Payrolls.AnyAsync(p =>
                p.EmployeeId == request.EmployeeId &&
                p.Month == request.Month &&
                p.Year == request.Year))
                throw new DuplicateException(
                    $"Bảng lương tháng {request.Month}/{request.Year} của nhân viên này đã được tạo");

            // Tính số công thực tế từ bảng chấm công
            var workingDaysActual = await _db.Attendances
                .Where(a => a.EmployeeId == request.EmployeeId &&
                            a.WorkDate.Month == request.Month &&
                            a.WorkDate.Year == request.Year &&
                            a.Type != AttendanceType.Absent)
                .CountAsync();

            // Tính giờ OT
            var totalOvertimeHours = await _db.Attendances
                .Where(a => a.EmployeeId == request.EmployeeId &&
                            a.WorkDate.Month == request.Month &&
                            a.WorkDate.Year == request.Year)
                .SumAsync(a => a.OvertimeHours);

            // Công thức lương OT: 1.5x lương giờ
            var hourlyRate = emp.WorkingDaysPerMonth > 0
                ? emp.BaseSalary / emp.WorkingDaysPerMonth / 8
                : 0;
            var overtimePay = Math.Round(totalOvertimeHours * hourlyRate * 1.5m, 0);

            // Lương theo công
            var dailyRate = emp.WorkingDaysPerMonth > 0
                ? emp.BaseSalary / emp.WorkingDaysPerMonth
                : 0;
            var earnedSalary = Math.Round(dailyRate * workingDaysActual, 0);

            var netSalary = earnedSalary + request.Allowance + overtimePay - request.Deduction;
            if (netSalary < 0) netSalary = 0;

            var payroll = new Payroll
            {
                EmployeeId = request.EmployeeId,
                Month = request.Month,
                Year = request.Year,
                WorkingDaysActual = workingDaysActual,
                BaseSalary = emp.BaseSalary,
                Allowance = request.Allowance,
                OvertimePay = overtimePay,
                Deduction = request.Deduction,
                NetSalary = netSalary,
                Status = PayrollStatus.Draft
            };

            _db.Payrolls.Add(payroll);
            await _db.SaveChangesAsync();
            await _db.Entry(payroll).Reference(p => p.Employee).LoadAsync();
            return _mapper.Map<PayrollResponseDto>(payroll);
        }

        // ─── GET BY MONTH/YEAR ────────────────────────────────
        public async Task<List<PayrollResponseDto>> GetByMonthYearAsync(int month, int year)
        {
            var list = await BaseQuery()
                .Where(p => p.Month == month && p.Year == year)
                .OrderBy(p => p.Employee.FullName)
                .ToListAsync();
            return _mapper.Map<List<PayrollResponseDto>>(list);
        }

        // ─── CONFIRM ──────────────────────────────────────────
        public async Task ConfirmAsync(Guid id)
        {
            var payroll = await _db.Payrolls.FindAsync(id)
                ?? throw new NotFoundException("Bảng lương", id);

            if (payroll.Status != PayrollStatus.Draft)
                throw new BusinessException("Chỉ có thể xác nhận bảng lương đang ở trạng thái Draft");

            payroll.Status = PayrollStatus.Confirmed;
            payroll.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // ─── MARK AS PAID ─────────────────────────────────────
        public async Task MarkAsPaidAsync(Guid id)
        {
            var payroll = await _db.Payrolls.FindAsync(id)
                ?? throw new NotFoundException("Bảng lương", id);

            if (payroll.Status != PayrollStatus.Confirmed)
                throw new BusinessException("Chỉ có thể đánh dấu đã trả lương khi bảng lương đã được xác nhận");

            payroll.Status = PayrollStatus.Paid;
            payroll.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}


