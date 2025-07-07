using System.Collections;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SemanticKernelMcpLib;

#pragma warning disable SKEXP0001

public class SimplifiedKernel
{
    private ChatHistory chatHistory;

    private Kernel kernel;

    private static IMcpClient? _dataVerseMcpClient = null;

    public int InputTokenCount = 0;
    public int OutputTokenCount = 0;

    private double costPerInputToken;
    private double costPerOutputToken;

    private string model;

    public double Cost
    {
        get
        {
            return InputTokenCount * costPerInputToken + OutputTokenCount * costPerOutputToken;
        }
    }

    public SimplifiedKernel(Kernel kernel, string model)
    {
        this.chatHistory = new ChatHistory();
        this.kernel = kernel;

        if (_dataVerseMcpClient == null) _dataVerseMcpClient = getDataverseMcpClient().Result;

        IList<McpClientTool> tools = _dataVerseMcpClient.ListToolsAsync().Result;
        this.kernel.Plugins.AddFromFunctions("Tools", tools.Select(aiFunction => aiFunction.AsKernelFunction()));
    }

    public SimplifiedKernel(Kernel kernel, double costPerInputToken, double costPerOutputToken, string model) : this(kernel, model)
    {
        this.costPerInputToken = costPerInputToken;
        this.costPerOutputToken = costPerOutputToken;
        this.model = model;
    }

    public async Task<string> GetChatMessageContentAsync(string userMessage)
    {
        chatHistory.AddUserMessage(userMessage);

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        // Enable automatic function calling
        PromptExecutionSettings executionSettings = new()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };

        var response = await chatCompletionService.GetChatMessageContentAsync(
            chatHistory,
            kernel: kernel,
            executionSettings: executionSettings
        );

        chatHistory.Add(response);

        return response.Content;
    }

    public async IAsyncEnumerable<string> GetChatMessageStreamingAsync(string userMessage)
    {
        chatHistory.AddUserMessage(userMessage);

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        // Enable automatic function calling
        PromptExecutionSettings executionSettings = null;

        if (this.model == "o4-mini") //AllowParallelCalls parameter not supported for 04-mini
        {
            executionSettings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
            };
        }
        else
        {
            executionSettings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true, AllowParallelCalls = false })
            };
        }

       var response = chatCompletionService.GetStreamingChatMessageContentsAsync(
       chatHistory: chatHistory,
       kernel: kernel,
       executionSettings: executionSettings
   );

        StringBuilder builder = new StringBuilder();

        await foreach (var chunk in response)
        {
            var metadata = chunk.Metadata;

            if (metadata != null && metadata.ContainsKey("Usage") && metadata["Usage"] != null)
            {
                if (metadata["Usage"] is OpenAI.Chat.ChatTokenUsage)
                {
                    OpenAI.Chat.ChatTokenUsage usage = (OpenAI.Chat.ChatTokenUsage)metadata["Usage"];

                    this.InputTokenCount += usage.InputTokenCount;
                    this.OutputTokenCount += usage.OutputTokenCount;
                }
                else if (metadata["Usage"] is Microsoft.Extensions.AI.UsageContent)
                {
                    Microsoft.Extensions.AI.UsageContent usage = (Microsoft.Extensions.AI.UsageContent)metadata["Usage"];
                    
                    this.InputTokenCount += (int) usage.Details.InputTokenCount;
                    this.OutputTokenCount += (int) usage.Details.OutputTokenCount;
                }
            }

            yield return chunk.ToString();
            builder.Append(chunk.ToString());
        }

        

        this.chatHistory.AddAssistantMessage(builder.ToString());
    }


    private async static Task<IMcpClient> getDataverseMcpClient(
        Kernel? kernel = null,
        Func<Kernel, CreateMessageRequestParams?, IProgress<ProgressNotificationValue>, CancellationToken, Task<CreateMessageResult>>? samplingRequestHandler = null)
    {
        var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "DataverseMcpServer",
            Command = "Microsoft.PowerPlatform.Dataverse.MCP",
            Arguments = [
                "--ConnectionUrl",
                "https://make.powerautomate.com/environments/7c89bd81-ec79-e990-99eb-90d823595740/connections?apiName=shared_commondataserviceforapps\"&\"connectionName=91433eff0e204d9a96771a47117a7d48",
                "--MCPServerName",
                "DataverseMCPServer",
                "--TenantId",
                "ea59b638-3d02-4773-83a8-a7f8606da0b6",
                "--EnableHttpLogging",
                "true",
                "--EnableMsalLogging",
                "false",
                "--Debug",
                "false",
                "--BackendProtocol",
                "HTTP"
                ],
        });

        var client = await McpClientFactory.CreateAsync(clientTransport);

        return client;
    }

    public static  IEnumerable<string> DisplayTools()
    {
        var tools = _dataVerseMcpClient.ListToolsAsync().Result;
        
        yield return "Available MCP tools:\n";
        foreach (var tool in tools)
        {
            yield return $"- Name: {tool.Name}, Description: {tool.Description}\n";
        }
    }
}
