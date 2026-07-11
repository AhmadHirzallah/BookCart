using MediatR;

namespace BookCart.Domain.Common.Abstractions;

//! We could have one or more [[subscribers]] to a [[domain event]], and we want to be able to publish the event without caring about who is handling it. This is the essence of the [[Mediator pattern]], and [[MediatR]] is a popular library that implements this pattern in [[.NET]].
//* - By having our [[domain events]] implement [[INotification]], we can use [[MediatR]] to publish these [[events]] and have any number of handlers respond to them without coupling our [[domain]] logic to the handlers.
//* - [[MediatR]] Notifications Are used to implemented the ([[ Publish and Subscribe Pattern ]]); And we will be publishing our [[domain events]] And we could have one or more [[subscribers]] To this [[event]] that want to handle it.
public interface INotifyDomainEvent : INotification;
