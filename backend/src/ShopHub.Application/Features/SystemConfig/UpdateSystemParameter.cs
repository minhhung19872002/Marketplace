using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ShopHub.Application.Abstractions;
using ShopHub.Application.Common;
using ShopHub.Application.SystemConfig;
using ShopHub.Domain.SystemConfig;

namespace ShopHub.Application.Features.SystemConfig;

/// <param name="Version">xmin seen by the editor; a stale value yields 409 instead of overwriting someone else's change.</param>
public record UpdateSystemParameterCommand(string Key, string Value, uint? Version) : IRequest<SystemParameterDto>;

public sealed class UpdateSystemParameterValidator : AbstractValidator<UpdateSystemParameterCommand>
{
    public UpdateSystemParameterValidator()
    {
        RuleFor(x => x.Key).NotEmpty().WithMessage("Thiếu mã tham số.");
        RuleFor(x => x.Value)
            .NotNull().WithMessage("Giá trị không được để trống.")
            .MaximumLength(4000).WithMessage("Giá trị tối đa 4.000 ký tự.");
    }
}

public sealed class UpdateSystemParameterHandler(
    IApplicationDbContext db,
    ISystemParameters parameters,
    IOutbox outbox,
    IJobScheduler jobScheduler) : IRequestHandler<UpdateSystemParameterCommand, SystemParameterDto>
{
    public async Task<SystemParameterDto> Handle(UpdateSystemParameterCommand request, CancellationToken ct)
    {
        var key = request.Key.Trim().ToUpperInvariant();
        var parameter = await db.SystemParameters.FirstOrDefaultAsync(p => p.Key == key, ct)
            ?? throw new NotFoundException($"Không tìm thấy tham số {key}.");

        if (request.Version is { } version && version != parameter.Version)
            throw new ConflictException("Tham số vừa được người khác sửa. Vui lòng tải lại rồi thử lần nữa.", "STALE_VERSION");

        // Type check needs the stored data type, so it lives here rather than in the validator
        var error = SystemParameter.Validate(parameter.DataType, request.Value);
        if (error is not null) throw new ValidationException([new ValidationFailure("value", error)]);

        parameter.SetValue(request.Value);
        outbox.Enqueue(OutboxTypes.SystemParameterChanged, new SystemParameterChangedPayload(key));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Tham số vừa được người khác sửa. Vui lòng tải lại rồi thử lần nữa.", "STALE_VERSION");
        }

        parameters.Invalidate(key);
        // Schedule changes take effect immediately, without waiting for a restart
        if (parameter.Group == ParameterGroups.Job) await jobScheduler.RegisterRecurringJobsAsync(ct);

        return new SystemParameterDto(parameter.Key, parameter.Value, parameter.DataType.ToString(), parameter.Group,
            parameter.Name, parameter.Description, parameter.Version, parameter.UpdatedAt, parameter.Id);
    }
}
