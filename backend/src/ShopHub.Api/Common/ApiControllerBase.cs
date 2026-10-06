using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ShopHub.Api.Common;

// Thin controllers: bind input, send to MediatR, wrap the result. No try/catch here — errors are handled globally.
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _sender;

    protected ISender Sender => _sender ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    protected OkObjectResult OkData<T>(T data, string message = "") => Ok(ApiResponse.Ok(data, message));
}
