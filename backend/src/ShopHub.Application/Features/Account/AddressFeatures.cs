using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.Identity;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.Iam;

namespace ShopHub.Application.Features.Account;

public record AddressDto(
    Guid Id,
    string ReceiverName,
    string Phone,
    string ProvinceCode,
    string ProvinceName,
    // Only on addresses saved before the two-level reform (2025-07-01)
    string? DistrictCode,
    string? DistrictName,
    string WardCode,
    string WardName,
    string Street,
    double? Lat,
    double? Lng,
    AddressType Type,
    bool IsDefault,
    // Saved with units that no longer exist (old district / merged ward or province): the buyer should re-pick them
    bool NeedsUpdate);

public record AddressInput(
    string ReceiverName,
    string Phone,
    string ProvinceCode,
    string WardCode,
    string Street,
    double? Lat,
    double? Lng,
    AddressType Type,
    bool IsDefault);

public sealed class AddressInputValidator : AbstractValidator<AddressInput>
{
    public AddressInputValidator()
    {
        RuleFor(x => x.ReceiverName).NotEmpty().WithMessage("Vui lòng nhập tên người nhận.")
            .MaximumLength(100).WithMessage("Tên người nhận tối đa 100 ký tự.");
        RuleFor(x => x.Phone).Must(p => Identifiers.NormalisePhone(p) is not null).WithMessage("Số điện thoại không hợp lệ.");
        RuleFor(x => x.ProvinceCode).NotEmpty().WithMessage("Vui lòng chọn Tỉnh/Thành phố.");
        RuleFor(x => x.WardCode).NotEmpty().WithMessage("Vui lòng chọn Phường/Xã.");
        RuleFor(x => x.Street).NotEmpty().WithMessage("Vui lòng nhập địa chỉ cụ thể.")
            .MaximumLength(255).WithMessage("Địa chỉ cụ thể tối đa 255 ký tự.");
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("Vĩ độ không hợp lệ.");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("Kinh độ không hợp lệ.");
    }
}

internal static class AddressRules
{
    /// <summary>Two levels (since 2025-07-01): an active ward of the chosen active province (checked against admin_divisions).</summary>
    public static Task EnsureHierarchyAsync(IApplicationDbContext db, AddressInput input, CancellationToken ct) =>
        EnsureHierarchyAsync(db, input.ProvinceCode, input.WardCode, ct);

    public static async Task EnsureHierarchyAsync(IApplicationDbContext db, string provinceCode, string wardCode, CancellationToken ct)
    {
        var codes = new[] { provinceCode, wardCode };
        var units = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code) && d.IsActive).ToListAsync(ct);
        var province = units.FirstOrDefault(u => u.Code == provinceCode && u.Level == AdminDivisionLevel.Province);
        var ward = units.FirstOrDefault(u => u.Code == wardCode && u.Level == AdminDivisionLevel.Ward);

        var errors = new List<ValidationFailure>();
        if (province is null) errors.Add(new("provinceCode", "Tỉnh/Thành phố không hợp lệ."));
        else if (ward is null || ward.ParentCode != provinceCode) errors.Add(new("wardCode", "Phường/Xã không thuộc Tỉnh/Thành phố đã chọn."));
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    public static IQueryable<AddressDto> Project(IApplicationDbContext db, IQueryable<Address> addresses) =>
        from a in addresses
        join p in db.AdminDivisions on a.ProvinceCode equals p.Code
        join w in db.AdminDivisions on a.WardCode equals w.Code
        from d in db.AdminDivisions.Where(d => d.Code == a.DistrictCode).DefaultIfEmpty()
        select new AddressDto(a.Id, a.ReceiverName, a.Phone, a.ProvinceCode, p.Name, a.DistrictCode, d == null ? null : d.Name,
            a.WardCode, w.Name, a.Street, a.Lat, a.Lng, a.Type, a.IsDefault,
            a.DistrictCode != null || !p.IsActive || !w.IsActive || w.ParentCode != a.ProvinceCode);

    /// <summary>Clear the current default first so the partial unique index (one default per user) never trips.</summary>
    public static Task ClearDefaultAsync(IApplicationDbContext db, Guid userId, CancellationToken ct) =>
        db.Addresses.Where(a => a.UserId == userId && a.IsDefault)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false), ct);
}

public record ListAddressesQuery : IRequest<IReadOnlyList<AddressDto>>;

public sealed class ListAddressesHandler(IApplicationDbContext db, ICurrentUser currentUser)
    : IRequestHandler<ListAddressesQuery, IReadOnlyList<AddressDto>>
{
    public async Task<IReadOnlyList<AddressDto>> Handle(ListAddressesQuery request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var rows = await AddressRules.Project(db, db.Addresses.AsNoTracking().Where(a => a.UserId == userId)).ToListAsync(ct);
        return rows.OrderByDescending(a => a.IsDefault).ThenBy(a => a.ReceiverName).ThenBy(a => a.Id).ToList();
    }
}

public record CreateAddressCommand(AddressInput Input) : IRequest<AddressDto>;

public sealed class CreateAddressValidator : AbstractValidator<CreateAddressCommand>
{
    public CreateAddressValidator() => RuleFor(x => x.Input).SetValidator(new AddressInputValidator());
}

public sealed class CreateAddressHandler(IApplicationDbContext db, ICurrentUser currentUser, ISystemParameters parameters)
    : IRequestHandler<CreateAddressCommand, AddressDto>
{
    public async Task<AddressDto> Handle(CreateAddressCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var input = request.Input;
        await AddressRules.EnsureHierarchyAsync(db, input, ct);

        var max = await parameters.GetIntAsync(ParameterKeys.AccountMaxAddresses, ct);
        // One add at a time per user: the limit and "the first one is the default" are counted, not constrained (L134)
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.LockAsync($"addresses:{userId}", ct);
        var count = await db.Addresses.CountAsync(a => a.UserId == userId, ct);
        if (count >= max) throw new ConflictException($"Bạn chỉ có thể lưu tối đa {max} địa chỉ.", "ADDRESS_LIMIT");

        // The first address is always the default
        var makeDefault = input.IsDefault || count == 0;
        if (makeDefault) await AddressRules.ClearDefaultAsync(db, userId, ct);

        var address = new Address(userId);
        address.Update(input.ReceiverName, Identifiers.NormalisePhone(input.Phone)!, input.ProvinceCode,
            input.WardCode, input.Street, input.Lat, input.Lng, input.Type);
        address.SetDefault(makeDefault);
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await AddressRules.Project(db, db.Addresses.AsNoTracking().Where(a => a.Id == address.Id)).SingleAsync(ct);
    }
}

public record UpdateAddressCommand(Guid Id, AddressInput Input) : IRequest<AddressDto>;

public sealed class UpdateAddressValidator : AbstractValidator<UpdateAddressCommand>
{
    public UpdateAddressValidator() => RuleFor(x => x.Input).SetValidator(new AddressInputValidator());
}

public sealed class UpdateAddressHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<UpdateAddressCommand, AddressDto>
{
    public async Task<AddressDto> Handle(UpdateAddressCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == userId, ct)
            ?? throw new NotFoundException("Không tìm thấy địa chỉ.");
        var input = request.Input;
        await AddressRules.EnsureHierarchyAsync(db, input, ct);

        if (input.IsDefault && !address.IsDefault)
        {
            await AddressRules.ClearDefaultAsync(db, userId, ct);
            address.SetDefault(true);
        }
        address.Update(input.ReceiverName, Identifiers.NormalisePhone(input.Phone)!, input.ProvinceCode,
            input.WardCode, input.Street, input.Lat, input.Lng, input.Type);
        await db.SaveChangesAsync(ct);

        return await AddressRules.Project(db, db.Addresses.AsNoTracking().Where(a => a.Id == address.Id)).SingleAsync(ct);
    }
}

public record SetDefaultAddressCommand(Guid Id) : IRequest<Unit>;

public sealed class SetDefaultAddressHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<SetDefaultAddressCommand, Unit>
{
    public async Task<Unit> Handle(SetDefaultAddressCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == userId, ct)
            ?? throw new NotFoundException("Không tìm thấy địa chỉ.");
        if (address.IsDefault) return Unit.Value;

        await AddressRules.ClearDefaultAsync(db, userId, ct);
        address.SetDefault(true);
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

public record DeleteAddressCommand(Guid Id) : IRequest<Unit>;

public sealed class DeleteAddressHandler(IApplicationDbContext db, ICurrentUser currentUser) : IRequestHandler<DeleteAddressCommand, Unit>
{
    public async Task<Unit> Handle(DeleteAddressCommand request, CancellationToken ct)
    {
        var userId = currentUser.RequireUserId();
        var address = await db.Addresses.FirstOrDefaultAsync(a => a.Id == request.Id && a.UserId == userId, ct)
            ?? throw new NotFoundException("Không tìm thấy địa chỉ.");
        var wasDefault = address.IsDefault;
        address.SetDefault(false);
        db.Addresses.Remove(address);
        await db.SaveChangesAsync(ct);

        // Deleting the default promotes the most recently created remaining address
        if (wasDefault)
        {
            var next = await db.Addresses.Where(a => a.UserId == userId)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
            if (next is not null)
            {
                next.SetDefault(true);
                await db.SaveChangesAsync(ct);
            }
        }
        return Unit.Value;
    }
}

// ---------- Administrative divisions (public lookup) ----------

public record AdminDivisionDto(string Code, string Name, AdminDivisionLevel Level, string? ParentCode);

public record ListAdminDivisionsQuery(string? ParentCode) : IRequest<IReadOnlyList<AdminDivisionDto>>;

public sealed class ListAdminDivisionsHandler(IApplicationDbContext db)
    : IRequestHandler<ListAdminDivisionsQuery, IReadOnlyList<AdminDivisionDto>>
{
    public async Task<IReadOnlyList<AdminDivisionDto>> Handle(ListAdminDivisionsQuery request, CancellationToken ct)
    {
        // Only units that exist today: 34 provinces, then the wards of one province (sorted by name for the picker)
        var query = db.AdminDivisions.AsNoTracking().Where(d => d.IsActive);
        query = string.IsNullOrWhiteSpace(request.ParentCode)
            ? query.Where(d => d.Level == AdminDivisionLevel.Province)
            : query.Where(d => d.ParentCode == request.ParentCode && d.Level == AdminDivisionLevel.Ward);
        return await query.OrderBy(d => d.Level == AdminDivisionLevel.Province ? d.Code : d.Name).ThenBy(d => d.Code)
            .Select(d => new AdminDivisionDto(d.Code, d.Name, d.Level, d.ParentCode))
            .ToListAsync(ct);
    }
}
