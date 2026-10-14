using SampleMobile.Domain.Entities;

namespace SampleMobile.Application.Abstractions.Repositories;

public interface IDemoAlertRepository {
    void Add(DemoAlert alert);
}
