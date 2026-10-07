using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Admin;

// ---------- Danh mục hành chính (VI.8) ----------

public record AdminDivisionRowDto(string Code, string Name, AdminDivisionLevel Level, string? ParentCode, int ChildCount, int AddressCount);

public record AdminDivisionsQuery(string? ParentCode) : IRequest<IReadOnlyList<AdminDivisionRowDto>>;

/// <summary>One level of the tree (provinces, or the children of a unit) with how many units and saved addresses hang on each.</summary>
public sealed class AdminDivisionsHandler(IApplicationDbContext db) : IRequestHandler<AdminDivisionsQuery, IReadOnlyList<AdminDivisionRowDto>>
{
    public async Task<IReadOnlyList<AdminDivisionRowDto>> Handle(AdminDivisionsQuery request, CancellationToken ct) =>
        await db.AdminDivisions.AsNoTracking().Where(d => d.ParentCode == request.ParentCode)
            .OrderBy(d => d.Code)
            .Select(d => new AdminDivisionRowDto(d.Code, d.Name, d.Level, d.ParentCode,
                db.AdminDivisions.Count(c => c.ParentCode == d.Code),
                db.Addresses.Count(a => a.ProvinceCode == d.Code || a.DistrictCode == d.Code || a.WardCode == d.Code)))
            .ToListAsync(ct);
}

public record SaveAdminDivisionCommand(string Code, string Name, string? ParentCode, bool IsNew) : IRequest<Unit>;

public sealed class SaveAdminDivisionValidator : AbstractValidator<SaveAdminDivisionCommand>
{
    public SaveAdminDivisionValidator()
    {
        RuleFor(x => x.Code).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập mã đơn vị.")
            .Matches("^[0-9]{2,10}$").WithMessage("Mã đơn vị gồm 2–10 chữ số (theo mã Tổng cục Thống kê).");
        RuleFor(x => x.Name).Cascade(CascadeMode.Stop).NotEmpty().WithMessage("Vui lòng nhập tên đơn vị.")
            .MaximumLength(100).WithMessage("Tên tối đa 100 ký tự.");
    }
}

/// <summary>
/// Add a unit (a new ward after a merger…) under a parent, or rename one. Codes never change and units are never
/// deleted: saved addresses, warehouses and order snapshots point at them. Every change goes to the audit log.
/// </summary>
public sealed class SaveAdminDivisionHandler(IApplicationDbContext db, ICurrentUser currentUser, IClock clock) : IRequestHandler<SaveAdminDivisionCommand, Unit>
{
    public async Task<Unit> Handle(SaveAdminDivisionCommand request, CancellationToken ct)
    {
        var code = request.Code.Trim();
        if (!request.IsNew)
        {
            var unit = await db.AdminDivisions.FirstOrDefaultAsync(d => d.Code == code, ct) ?? throw new NotFoundException("Không tìm thấy đơn vị hành chính.");
            var old = unit.Name;
            unit.Rename(request.Name);
            Audit(AuditActions.Update, code, Json(new { Name = old }), Json(new { unit.Name }));
        }
        else
        {
            if (await db.AdminDivisions.AnyAsync(d => d.Code == code, ct)) throw new ConflictException($"Mã {code} đã tồn tại.", "DIVISION_CODE_TAKEN");
            AdminDivisionLevel level = AdminDivisionLevel.Province;
            if (request.ParentCode is { Length: > 0 } parentCode)
            {
                var parent = await db.AdminDivisions.AsNoTracking().FirstOrDefaultAsync(d => d.Code == parentCode, ct)
                             ?? throw new NotFoundException("Không tìm thấy đơn vị cấp trên.");
                level = parent.Level switch
                {
                    AdminDivisionLevel.Province => AdminDivisionLevel.District,
                    AdminDivisionLevel.District => AdminDivisionLevel.Ward,
                    _ => throw new ConflictException("Phường / xã không có cấp dưới.", "DIVISION_NO_CHILD"),
                };
            }
            var created = new AdminDivision(code, request.Name.Trim(), level, request.ParentCode is { Length: > 0 } ? request.ParentCode : null);
            db.AdminDivisions.Add(created);
            Audit(AuditActions.Create, code, null, Json(new { created.Name, Level = created.Level.ToString(), created.ParentCode }));
        }
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    // The table is left out of the automatic audit (10.000+ units are seeded); admin changes are logged here
    private void Audit(string action, string code, string? oldValue, string? newValue) =>
        db.AuditLogs.Add(new AuditLog(currentUser.UserId, currentUser.IpAddress, currentUser.UserAgent, action, nameof(AdminDivision), code, oldValue, newValue,
            clock.UtcNow));

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}
