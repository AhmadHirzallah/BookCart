using BookCart.Domain.Common.Abstractions;
using BookCart.Domain.Entities.Categories.ValueObjects;

namespace BookCart.Domain.Entities.Categories.Events;

public sealed record CategoryCreatedDomainEvent(CategoryId CategoryId) : INotifyDomainEvent;
