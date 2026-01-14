using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using System.Diagnostics;

namespace OddsTracker.Core.Services
{
    public sealed class RedisDockerHostedService : IHostedService, IDisposable
    {
        private const string ContainerName = "local-redis";
        private bool _disposed;

        public RedisDockerHostedService()
        {
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += OnCancelKeyPress;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            if (IsRedisRunning())
                return;

            if (ContainerExists())
            {
                Run("docker", $"start {ContainerName}");
            }
            else
            {
                Run("docker", $"run -d --name {ContainerName} -p 6379:6379 redis:7");
            }

            await WaitForRedis(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopContainer();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Console.CancelKeyPress -= OnCancelKeyPress;
            StopContainer();
        }

        private void OnProcessExit(object? sender, EventArgs e) => StopContainer();

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e) => StopContainer();

        private static void StopContainer()
        {
            if (IsRedisRunning())
            {
                Run("docker", $"stop {ContainerName}");
            }
        }

        private static bool IsRedisRunning()
            => Run("docker", $"ps --filter name={ContainerName} --format \"{{{{.Names}}}}\"")
                .Contains(ContainerName);

        private static bool ContainerExists()
            => Run("docker", $"ps -a --filter name={ContainerName} --format \"{{{{.Names}}}}\"")
                .Contains(ContainerName);

        private static async Task WaitForRedis(CancellationToken token)
        {
            for (var i = 0; i < 10; i++)
            {
                try
                {
                    using var mux = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
                    if (mux.IsConnected) return;
                }
                catch { }

                await Task.Delay(500, token);
            }

            throw new Exception("Redis failed to start");
        }

        private static string Run(string file, string args)
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };

            p.Start();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }
}
