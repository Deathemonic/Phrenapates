using Phrenapates.Commands;
using Phrenapates.Services.Irc;

using Plana.Database;


namespace Phrenapates.Services.Terminal
{
    public class TerminalCommandHandler : BackgroundService
    {
        private readonly ILogger<TerminalCommandHandler> _logger;
        private readonly SCHALEContext _context;
        private readonly ExcelTableService _excelTableService;
        private IrcConnection _terminalConnection;

        public TerminalCommandHandler(ILogger<TerminalCommandHandler> logger, SCHALEContext context, ExcelTableService excelTableService)
        {
            _logger = logger;
            _context = context;
            _excelTableService = excelTableService;

            _terminalConnection = new IrcConnection
            {
                Context = _context,
                ExcelTableService = _excelTableService,
                StreamWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true },
                CurrentChannel = "terminal",
                TcpClient = null!
            };
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Terminal command handler started. Type /help for available commands.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var input = await Console.In.ReadLineAsync();
                if (string.IsNullOrEmpty(input)) continue;
                if (!input.StartsWith('/')) continue;

                var cmdStrings = input.Split(" ");
                var cmdStr = cmdStrings.First().Split('/').Last();

                Command? cmd = CommandFactory.CreateCommand(cmdStr, _terminalConnection, cmdStrings[1..]);
                if (cmd is null)
                {
                    _terminalConnection.SendChatMessage($"Invalid command {cmdStr}, try /help");
                    continue;
                }

                try
                {
                    cmd.Execute();
                    _terminalConnection.SendChatMessage($"Command {cmdStr} executed successfully!");
                }
                catch (Exception ex)
                {
                    var cmdAtr = (CommandHandlerAttribute?)Attribute.GetCustomAttribute(CommandFactory.commands[cmdStr], typeof(CommandHandlerAttribute));
                    _terminalConnection.SendChatMessage($"Command {cmdStr} failed to execute! {ex.Message}");
                    _terminalConnection.SendChatMessage($"Usage: {cmdAtr?.Usage}");
                }
            }
        }
    }
} 