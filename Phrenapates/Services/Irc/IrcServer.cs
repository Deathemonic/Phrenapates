using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Phrenapates.Commands;
using Plana.Database;

namespace Phrenapates.Services.Irc
{
    public class IrcServer
    {
        private ConcurrentDictionary<TcpClient, IrcConnection> clients = new ConcurrentDictionary<TcpClient, IrcConnection>(); // most irc commands doesn't even send over the player uid so imma just use TcpClient as key
        private ConcurrentDictionary<string, List<long>> channels = new ConcurrentDictionary<string, List<long>>();

        private readonly TcpListener listener;

        private readonly ILogger<IrcService> logger;
        private readonly SCHALEContext context;
        private readonly ExcelTableService excelTableService;

        public IrcServer(IPAddress host, int port, ILogger<IrcService> _logger, SCHALEContext _context, ExcelTableService _excelTableService)
        {
            logger = _logger;
            context = _context;
            excelTableService = _excelTableService;

            listener = new TcpListener(host, port);
        }

        public async Task StartAsync(CancellationToken stoppingToken)
        {
            listener.Start();
            logger.LogDebug("Irc Server Started");

            while (!stoppingToken.IsCancellationRequested)
            {
                var tcpClient = await listener.AcceptTcpClientAsync();

                _ = HandleMessageAsync(tcpClient);
                logger.LogDebug("TcpClient is trying to connect...");
            }
        }

        public async Task HandleMessageAsync(TcpClient tcpClient)
        {
            using var reader = new StreamReader(tcpClient.GetStream());
            using var writer = new StreamWriter(tcpClient.GetStream()) { AutoFlush = true };

            string? line;

            while ((line = await reader.ReadLineAsync()) is not null)
            {
                var splitLine = line.Split(' ', 2);
                var commandStr = splitLine[0].ToUpper().Trim();
                var parameters = splitLine.Length > 1 ? splitLine[1] : "";

                if (!Enum.TryParse<IrcCommand>(commandStr, out var command))
                {
                    command = IrcCommand.UNKNOWN;
                }

                string result = "";

                switch (command)
                {
                    case IrcCommand.NICK:
                        result = await HandleNick(parameters);
                        break;
                    case IrcCommand.USER:
                        await HandleUser(parameters, tcpClient, writer);
                        break;
                    case IrcCommand.JOIN:
                        await HandleJoin(parameters, tcpClient);
                        break;
                    case IrcCommand.PRIVMSG:
                        await HandlePrivMsg(parameters, tcpClient);
                        break;
                    case IrcCommand.PING:
                        result = await HandlePing(parameters);
                        break;
                }

                if (result != null || result != "")
                    await writer.WriteLineAsync(result);
            }

            tcpClient.Close();
        }

        public void Stop()
        {
            listener.Stop();
        }

        private Task<string> HandleNick(string parameters) // welcomes
        {
            return Task.FromResult(new Reply()
            {
                Prefix = "server",
                ReplyCode = ReplyCode.RPL_WELCOME,
                Trailing = "Welcome, Sensei."
            }.ToString());
        }

        private Task HandleUser(string parameters, TcpClient client, StreamWriter writer)
        {
            string[] args = parameters.Split(' ');
            var user_serverId = long.Parse(args[0].Split("_")[1]);

            clients[client] = new IrcConnection()
            {
                AccountServerId = user_serverId,
                Context = context,
                TcpClient = client,
                StreamWriter = writer,
                ExcelTableService = excelTableService,
                CurrentChannel = string.Empty
            };

            logger.LogDebug($"User {user_serverId} logged in");
            
            return Task.CompletedTask;
        }

        private Task HandleJoin(string parameters, TcpClient client)
        {
            var channel = parameters;

            if (!channels.TryGetValue(channel, out var channelUsers))
            {
                channelUsers = [];
                channels[channel] = channelUsers;
            }

            var connection = clients[client];

            channels[channel].Add(connection.AccountServerId);
            connection.CurrentChannel = channel;

            logger.LogDebug($"User {connection.AccountServerId} joined {channel}");

            // custom welcome
            connection.SendChatMessage("Welcome, Sensei.");
            connection.SendChatMessage("Type /help for more information.");
            connection.SendEmote(2);

            return Task.CompletedTask;
        }

        private Task HandlePrivMsg(string parameters, TcpClient client)
        {
            string[] args = parameters.Split(' ', 2);

            var channel = args[0];
            var payloadStr = args[1].TrimStart(':');

            var payload = JsonSerializer.Deserialize<IrcMessage>(payloadStr);
            
            if (!(payload?.Text?.StartsWith('/') ?? false))
            {
                return Task.CompletedTask;
            }

            var cmdStrings = payload.Text.Split(" ");
            var connection = clients[client];
            var cmdStr = cmdStrings.First().Split('/').Last();

            try
            {
                Command? cmd = CommandFactory.CreateCommand(cmdStr, connection, cmdStrings[1..]);

                if (cmd is null)
                {
                    connection.SendChatMessage($"Invalid command {cmdStr}, try /help");
                    return Task.CompletedTask;
                }

                cmd.Execute();
                connection.SendChatMessage($"Command {cmdStr} executed sucessfully! Please relog for it to take effect.");
            }
            catch (Exception ex)
            {
                var cmdAtr = (CommandHandlerAttribute?)Attribute.GetCustomAttribute(CommandFactory.commands[cmdStr], typeof(CommandHandlerAttribute));

                connection.SendChatMessage($"Command {cmdStr} failed to execute!, " + ex.Message);
                connection.SendChatMessage($"Usage: {cmdAtr?.Usage}");
            }

            return Task.CompletedTask;
        }

        private Task<string> HandlePing(string parameters)
        {
            return Task.FromResult(new Reply().ToString());
        }
    }

    public enum IrcCommand
    {
        PASS,
        NICK,
        USER,
        JOIN,
        PRIVMSG,
        PING,
        PART,
        QUIT,
        UNKNOWN
    }

    public enum IrcMessageType
    {
        None,
        Notice,
        Sticker,
        Chat,
        HistoryCount
    }

    public class IrcMessage : EventArgs
    {
        [JsonPropertyName("MessageType")]
        public IrcMessageType MessageType { get; set; }
        
        [JsonPropertyName("CharacterId")]
        public long CharacterId { get; set; }

        [JsonPropertyName("AccountNickname")]
        public required string AccountNickname { get; set; }

        [JsonPropertyName("StickerId")]
        public long StickerId { get; set; }

        [JsonPropertyName("Text")]
        public required string Text { get; set; }

        [JsonPropertyName("SendTicks")]
        public long SendTicks { get; set; }

        [JsonPropertyName("EmblemId")]
        public int EmblemId { get; set; }
    }
}
