using System;
using System.Net;
using System.Net.WebSockets;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using Discord.API.Gateway;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace Clide
{
  class Discord
  {
    private string Token { get; set; }
    private ClientWebSocket WebSocketClient { get; set; }
    private WebClient WebClient { get; set; }
    private int HeartbeatInterval { get; set; }
    private int? Sequence { get; set; }
    public delegate void onMessageCallback(Message message);
    public onMessageCallback onMessage;
    public Discord()
    {
      this.WebClient = new WebClient();
    }

    private async Task<Uri> GetWebSocketURL()
    {
      var response = await WebClient.DownloadStringTaskAsync("https://discordapp.com/api/gateway");
      var data = JsonConvert.DeserializeObject<GatewayResponse>(response);

      return new Uri(data.url);
    }

    private async Task HandleHello(WebSocketMessage<Hello> data)
    {
      this.HeartbeatInterval = data.Data.HeartbeatInterval;
      this.Sequence = data.s;

      this.Heartbeat();
      await this.Identify();
    }

    private async Task Identify()
    {
      var parameters = new IdentifyParams();
      parameters.Token = this.Token;
      parameters.Properties = new Dictionary<string, string>(){
        { "$os", "linux" },
        { "$browser", "disco" },
        { "$device", "disco" },
      };

      var payload = new WebSocketMessage<IdentifyParams>();
      payload.op = OPCodes.Identify;
      payload.Data = parameters;

      var data = JsonConvert.SerializeObject(payload);

      await this.SendMessage(data);
    }

    private async Task ReceiveMessage()
    {
      while (this.WebSocketClient.State == WebSocketState.Open)
      {
        var finalBuffer = new List<byte>();
        var buffer = new byte[8];

        var result = await WebSocketClient.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close)
        {
          await WebSocketClient.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
          return;
        }

        do
        {
          for (var i = 0; i < result.Count; i++)
          {
            finalBuffer.Add(buffer[i]);
          }

          result = await WebSocketClient.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        } while (!result.EndOfMessage);

        for (var i = 0; i < result.Count; i++)
        {
          finalBuffer.Add(buffer[i]);
        }

        var data = (new ASCIIEncoding()).GetString(finalBuffer.ToArray());
        await this.HandleMessage(data);
      }
    }

    private async Task HandleDispatch(WebSocketMessage<Message> message)
    {
      switch (message.t)
      {
        case "MESSAGE_CREATE":
          this.onMessage(message.Data);
          break;
        default:
          break;
      }
    }

    private async Task HandleMessage(string data)
    {
      var message = JsonConvert.DeserializeObject<WebSocketMessage>(data);
      this.Sequence = message.s;

      switch (message.op)
      {
        case OPCodes.Hello:
          await this.HandleHello(JsonConvert.DeserializeObject<WebSocketMessage<Hello>>(data));
          break;
        case OPCodes.Dispatch:
          await this.HandleDispatch(JsonConvert.DeserializeObject<WebSocketMessage<Message>>(data));
          break;
        case OPCodes.HeartbeatACK:
          Console.WriteLine("Heartbeat ok!");
          break;
        case OPCodes.InvalidSession:
          throw new Exception("Invalid Discord token");
        default:
          Console.WriteLine(message.op);
          break;
      }
    }

    public async Task SendMessage(string message)
    {
      var bytes = Encoding.UTF8.GetBytes(message);
      var buffer = new ArraySegment<byte>(bytes);

      await this.WebSocketClient.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private void Heartbeat()
    {
      var message = new WebSocketMessage<int?>();
      message.op = OPCodes.Heartbeat;
      message.Data = this.Sequence;

      var payload = JsonConvert.SerializeObject(message);

      var task = Task.Factory.StartNew(async () =>
      {
        while (this.WebSocketClient.State != WebSocketState.Closed)
        {
          await this.SendMessage(payload);
          await Task.Delay(this.HeartbeatInterval);
        }
      });
    }

    public async Task Login(string token)
    {
      this.Token = token;

      var GatewayUri = await this.GetWebSocketURL();
      this.WebSocketClient = new ClientWebSocket();
      await this.WebSocketClient.ConnectAsync(GatewayUri, CancellationToken.None);

      await this.ReceiveMessage();
    }
  }

  class GatewayResponse
  {
    public string url { get; set; }
  }
}