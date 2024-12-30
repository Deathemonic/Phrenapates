namespace Phrenapates.Services.Terminal
{
    public static class TerminalServiceExtensions
    {
        public static void AddTerminalCommandHandler(this IServiceCollection services)
        {
            services.AddHostedService<TerminalCommandHandler>();
        }
    }
} 