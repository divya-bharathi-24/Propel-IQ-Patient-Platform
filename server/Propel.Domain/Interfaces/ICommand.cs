using MediatR;

namespace Propel.Domain.Interfaces;

/// <summary>
/// Marker interface for MediatR write operations (commands) with no return value.
/// All command handlers must implement <see cref="IRequestHandler{TRequest}"/> where
/// <c>TRequest : ICommand</c> (TR-019, AD-2).
/// </summary>
public interface ICommand : IRequest { }

/// <summary>
/// Marker interface for MediatR write operations (commands) that return a result.
/// All command handlers must implement <see cref="IRequestHandler{TRequest, TResponse}"/> where
/// <c>TRequest : ICommand{TResponse}</c> (TR-019, AD-2).
/// </summary>
/// <typeparam name="TResponse">The type returned by the command handler.</typeparam>
public interface ICommand<TResponse> : IRequest<TResponse> { }

/// <summary>
/// Marker interface for MediatR read operations (queries).
/// All query handlers must implement <see cref="IRequestHandler{TRequest, TResponse}"/> where
/// <c>TRequest : IQuery{TResponse}</c> (TR-019, AD-2).
/// Controllers must inject <see cref="MediatR.ISender"/> (not <c>IMediator</c>) to prevent
/// direct repository access from controllers.
/// </summary>
/// <typeparam name="TResponse">The type returned by the query handler.</typeparam>
public interface IQuery<TResponse> : IRequest<TResponse> { }
