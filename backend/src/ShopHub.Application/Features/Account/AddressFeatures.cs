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
    string DistrictCode,
    string DistrictName,
    string WardCode,
    string WardName,
    string Street,
    double? Lat,
    double? Lng,
    AddressType Type,
    bool IsDefault);

public record AddressInput(
    string ReceiverName,
    string Phone,
    string ProvinceCode,
    string DistrictCode,
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
        RuleFor(x => x.DistrictCode).NotEmpty().WithMessage("Vui lòng chọn Quận/Huyện.");
        RuleFor(x => x.WardCode).NotEmpty().WithMessage("Vui lòng chọn Phường/Xã.");
        RuleFor(x => x.Street).NotEmpty().WithMessage("Vui lòng nhập địa chỉ cụ thể.")
            .MaximumLength(255).WithMessage("Địa chỉ cụ thể tối đa 255 ký tự.");
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat is not null).WithMessage("Vĩ độ không hợp lệ.");
        RuleFor(x => x.Lng).InclusiveBetween(-180, 180).When(x => x.Lng is not null).WithMessage("Kinh độ không hợp lệ.");
    }
}

internal static class AddressRules
{
    /// <summary>Ward must belong to the district, district to the province (checked against admin_divisions).</summary>
    public static async Task EnsureHierarchyAsync(IApplicationDbContext db, AddressInput input, CancellationToken ct)
    {
        var codes = new[] { input.ProvinceCode, input.DistrictCode, input.WardCode };
        var units = await db.AdminDivisions.AsNoTracking().Where(d => codes.Contains(d.Code)).ToListAsync(ct);
        var province = units.FirstOrDefault(u => u.Code == input.ProvinceCode && u.Level == AdminDivisionLevel.Province);
        var district = units.FirstOrDefault(u => u.Code == input.DistrictCode && u.Level == AdminDivisionLevel.District);
        var ward = units.FirstOrDefault(u => u.Code == input.WardCode && u.Level == AdminDivisionLevel.Ward);

        var errors = new List<ValidationFailure>();
        if (province is null) errors.Add(new("provinceCode", "Tỉnh/Thành phố không hợp lệ."));
        if (district is null || district.ParentCode != input.ProvinceCode) errors.Add(new("districtCode", "Quận/Huyện không thuộc Tỉnh/Thành phố đã chọn."));
        if (ward is null || ward.ParentCode != input.DistrictCode) errors.Add(new("wardCode", "Phường/Xã không thuộc Quận/Huyện đã chọn."));
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    public static IQueryable<AddressDto> Project(IApplicationDbContext db, IQueryable<Address> addresses) =>
        from a in addresses
        join p in db.AdminDivisions on a.ProvinceCode equals p.Code
        join d in db.AdminDivisions on a.DistrictCode equals d.Code
        join w in db.AdminDivisions on a.WardCode equals w.Code
        select new AddressDto(a.Id, a.ReceiverName, a.Phone, a.ProvinceCode, p.Name, a.DistrictCode, d.Name,
            a.WardCode, w.Name, a.Street, a.Lat, a.Lng, a.Type, a.IsDefault);

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
        var count = await db.Addresses.CountAsync(a => a.UserId == userId, ct);
        if (count >= max) throw new ConflictException($"Bạn chỉ có thể lưu tối đa {max} địa chỉ.", "ADDRESS_LIMIT");

        // The first address is always the default
        var makeDefault = input.IsDefault || count == 0;
        if (makeDefault) await AddressRules.ClearDefaultAsync(db, userId, ct);

        var address = new Address(userId);
        address.Update(input.ReceiverName, Identifiers.NormalisePhone(input.Phone)!, input.ProvinceCode, input.DistrictCode,
            input.WardCode, input.Street, input.Lat, input.Lng, input.Type);
        address.SetDefault(makeDefault);
        db.Addresses.Add(address);
        await db.SaveChangesAsync(ct);

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
        address.Update(input.ReceiverName, Identifiers.NormalisePhone(input.Phone)!, input.ProvinceCode, input.DistrictCode,
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
        var query = db.AdminDivisions.AsNoTracking();
        query = string.IsNullOrWhiteSpace(request.ParentCode)
            ? query.Where(d => d.Level == AdminDivisionLevel.Province)
            : query.Where(d => d.ParentCode == request.ParentCode);
        return await query.OrderBy(d => d.Code)
            .Select(d => new AdminDivisionDto(d.Code, d.Name, d.Level, d.ParentCode))
            .ToListAsync(ct);
    }
}
