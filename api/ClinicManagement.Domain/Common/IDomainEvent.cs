using MediatR;

namespace ClinicManagement.Domain.Common;

public interface IDomainEvent : INotification
{
    DateTime OccurredOn { get; }
}



