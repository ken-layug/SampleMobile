using SampleMobile.Domain.Entities;
using SampleMobile.Domain.Primitives;

namespace SampleMobile.Domain.Events;

/// <summary>A demo alert was raised for a user. Carries ids and values, never entity references.</summary>
public sealed record DemoAlertRaised(
    Guid AlertId,
    Guid RecipientUserId,
    DemoAlertSeverity Severity,
    string Message) : DomainEvent;
