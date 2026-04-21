using MediatR;

namespace Propel.Modules.Clinical.Commands;

public sealed record PingClinicalCommand : IRequest<string>;
