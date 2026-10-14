using SampleMobile.Application.Abstractions.Repositories;
using SampleMobile.Domain.Entities;

namespace SampleMobile.Infrastructure.Persistence.Repositories;

internal sealed class DemoAlertRepository(AppDbContext db) : IDemoAlertRepository {
    public void Add(DemoAlert alert) => db.DemoAlerts.Add(alert);
}
