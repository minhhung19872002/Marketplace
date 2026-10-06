using MediatR;
using ShopHub.Application.Abstractions;
using ShopHub.Application.SystemConfig;

namespace ShopHub.Application.Features.SystemConfig;

// Public platform identity shown in header/footer — every value comes from system parameters
public record SiteInfoDto(
    string PlatformName,
    string Hotline,
    string SupportEmail,
    string LegalName,
    string LegalAddress,
    string TaxCode,
    string BusinessLicense);

public record GetSiteInfoQuery : IRequest<SiteInfoDto>;

public sealed class GetSiteInfoHandler(ISystemParameters parameters) : IRequestHandler<GetSiteInfoQuery, SiteInfoDto>
{
    public async Task<SiteInfoDto> Handle(GetSiteInfoQuery request, CancellationToken ct) => new(
        await parameters.GetStringAsync(ParameterKeys.SitePlatformName, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteHotline, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteSupportEmail, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteLegalName, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteLegalAddress, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteTaxCode, ct),
        await parameters.GetStringAsync(ParameterKeys.SiteBusinessLicense, ct));
}
