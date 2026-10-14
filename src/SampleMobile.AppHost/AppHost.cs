var builder = DistributedApplication.CreateBuilder(args);

// Same image as docker-compose.yml. The generated password is kept in this project's user-secrets,
// so the persistent container and its volume survive AppHost restarts.
var postgres = builder.AddPostgres("postgres")
    .WithImageTag("17-alpine")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent)
    .WithPgAdmin(pgAdmin => pgAdmin.WithLifetime(ContainerLifetime.Persistent));

var database = postgres.AddDatabase("sample-mobile", databaseName: "sample_mobile");

// The web app reads ConnectionStrings:SampleMobile; migrations and the demo seed run on startup in Development.
// Its "http" launch profile keeps port 5010, so the Android emulator still reaches http://10.0.2.2:5010.
var web = builder.AddProject<Projects.SampleMobile_Web>("web")
    .WithReference(database, connectionName: "SampleMobile")
    .WaitFor(database)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

// Local development only: listen on every interface so a phone on the same network can use http://<this-pc-lan-ip>:5010.
if (builder.ExecutionContext.IsRunMode) {
    web.WithEndpoint("http", endpoint => endpoint.TargetHost = "0.0.0.0");
}

builder.Build().Run();
