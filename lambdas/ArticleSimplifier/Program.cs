using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

var host = new Microsoft.Extensions.Hosting.HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
