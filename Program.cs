using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAI;
using System;
using System.ClientModel;
using System.IO;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MCPClientExample
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // 1️⃣ 启动本地 MCP Server 客户端
            var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "LocalMCP",
                /* 生产模式 */
                Command = @"F:\ebCode\MCPServer\bin\Debug\net8.0\MCPServer.exe"
                /* 开发模式 */
                //Command = "dotnet", // 启动 dotnet 程序
                //Arguments = new[] { "run", "--project", @"F:\ebCode\MCPServer\MCPServer.csproj" } // 你的 Server 项目路径
            });

            var client = await McpClientFactory.CreateAsync(clientTransport);

            // Print the list of tools available from the server.
            var tools = await client.ListToolsAsync();
            foreach (var tool in tools)
            {
                Console.WriteLine($"{tool.Name} ({tool.Description})");
            }

            //ModelContextProtocol.Protocol.TextContentBlock

            // Execute a tool (this would normally be driven by LLM tool invocations).
            var result = await client.CallToolAsync(
                "echo",
                new Dictionary<string, object?>() { ["message"] = "Hello MCP!" },
                cancellationToken: CancellationToken.None);

            // echo always returns one and only one text content object
            var textBlock = result.Content.First(c => c.Type == "text") as TextContentBlock; // 转型失败返回 null
            Console.WriteLine(textBlock?.Text);

            // LLM
            //var llm = new LLM();
            //var response = await llm.GetResponse(user_message:"hello", content_info: "nothing", token: CancellationToken.None);
            //Console.WriteLine(response);

            // Options 里指定 BaseUri
            var options = new OpenAIClientOptions
            {
                Endpoint = new Uri("https://api.deepseek.com") // DeepSeek API 地址
            };

            // 从环境变量读取 API Key（或者直接写字符串，但不推荐）
            //var credential = new ApiKeyCredential("");
            var credential = new ApiKeyCredential("sk-ba474a00a0dd42d5b9ff80dc97d04abc");

            // 创建客户端
            var deepSeekClient = new OpenAIClient(credential, options)
                .GetChatClient("deepseek-chat"); // 模型名称按 DeepSeek 文档写

            // 转成 IChatClient
            IChatClient aliClient = deepSeekClient.AsIChatClient();
                
            using IChatClient chatClient = aliClient
                .AsBuilder()
                .UseFunctionInvocation()
                .Build();

            // Have a conversation, making all tools available to the LLM.
            List<ChatMessage> messages = [];
            while (true)
            {
                Console.Write("Q: ");
                var userInput = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(userInput))
                    break;

                messages.Add(new(ChatRole.User, userInput));

                try
                {
                    List<ChatResponse> updates = [];
                    ChatResponse res = await chatClient.GetResponseAsync(messages, new() { Tools = [.. tools] });

                    Console.WriteLine($"A: {res}");
                    messages.AddMessages(res);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
    }

    public class Monkey
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
        public string? Details { get; set; }
        public string? Image { get; set; }
        public int Population { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public class LLM()
    {
        // deepseek - api
        private string api_key_myown_deepseek = "sk-ba474a00a0dd42d5b9ff80dc97d04abc"; //我自己申请的10元账号api
        private string api_url_myown_deepseek = "https://api.deepseek.com/chat/completions";
        private string model_name = "deepseek-chat";

        public async Task<string> GetResponse(string user_message, string content_info , CancellationToken token)
        {
            // 构建传入参数
            string request_content = JsonConvert.SerializeObject(new
            {
                question = user_message,
                content = content_info
            });
            string requestBody = JsonConvert.SerializeObject(new
            {
                model = $"{model_name}",
                messages = new[]
                {
                    new { role = "system", content = "你是专业的软件客服" },
                    new { role = "user", content = request_content }
                },
                stream = false
            });
            // 发送信息
            var client = new HttpClient();
            string response_massege = "";

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {api_key_myown_deepseek}");
            HttpContent httpContent = new StringContent(requestBody, Encoding.UTF8, "application/json");
            try
            {
                HttpResponseMessage response = await client.PostAsync(api_url_myown_deepseek, httpContent, token);
                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    var responseObject = JsonConvert.DeserializeObject<dynamic>(jsonResponse);
                    if (responseObject != null)
                        response_massege = responseObject?.choices[0]?.message?.content ?? "无返回内容";
                }
                else
                {
                    response_massege = $"请求失败: {response.StatusCode}";
                }
            }
            catch (OperationCanceledException)
            {
                response_massege = "上个任务在未完成时取消，请敬待现任务完成...";
                token.ThrowIfCancellationRequested();
            }
            catch (Exception e)
            {
                response_massege = $"请求失败: {e.Message}";
            }
            return response_massege;
        }
    }
}
