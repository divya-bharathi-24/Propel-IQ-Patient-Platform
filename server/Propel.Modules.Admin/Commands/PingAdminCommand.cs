using MediatR;

namespace Propel.Modules.Admin.Commands;

public sealed record PingAdminCommand : IRequest<string>;
