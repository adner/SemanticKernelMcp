using System.Collections;
using System.ComponentModel;
using System.Data;
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
    public ChatHistory ChatHistory;

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
        this.ChatHistory = new ChatHistory();
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
        ChatHistory.AddUserMessage(userMessage);

        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

        // Enable automatic function calling
        PromptExecutionSettings executionSettings = new()
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(options: new() { RetainArgumentTypes = true })
        };

        var response = await chatCompletionService.GetChatMessageContentAsync(
            ChatHistory,
            kernel: kernel,
            executionSettings: executionSettings
        );

        ChatHistory.Add(response);

        return response.Content;
    }

    public async IAsyncEnumerable<string> GetChatMessageStreamingAsync(string userMessage)
    {
        ChatHistory.AddUserMessage(userMessage);

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
       chatHistory: ChatHistory,
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

        this.ChatHistory.AddAssistantMessage(builder.ToString());
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

public class OrchestratorKernel
{
    private ChatHistory chatHistory;

    private Kernel kernel;

    public int InputTokenCount = 0;
    public int OutputTokenCount = 0;

    private double costPerInputToken;
    private double costPerOutputToken;

    private string model;

    private string currentEvaluatedModelName;

    private Func<string, Task> sendInfoMessageToOrchestrator;

    public double Cost
    {
        get
        {
            return InputTokenCount * costPerInputToken + OutputTokenCount * costPerOutputToken;
        }
    }

    public OrchestratorKernel(Kernel kernel, string model, Func<string, Task> sendInfoMessageToOrchestrator)
    {
        OrchestratorKernelPlugin plugin = new OrchestratorKernelPlugin(this);

        this.chatHistory = new ChatHistory();
        this.kernel = kernel;
        this.model = model;
        this.sendInfoMessageToOrchestrator = sendInfoMessageToOrchestrator;
        this.currentEvaluatedModelName = string.Empty;

        this.kernel.Plugins.AddFromObject(plugin);
    }

    public OrchestratorKernel(Kernel kernel, double costPerInputToken, double costPerOutputToken, string model, Func<string, Task> sendMessageToOrchestrator) : this(kernel, model, sendMessageToOrchestrator)
    {
        this.costPerInputToken = costPerInputToken;
        this.costPerOutputToken = costPerOutputToken;
    }

    public async void SetCurrentModel(string modelName)
    {
        this.currentEvaluatedModelName = modelName;

        await this.sendInfoMessageToOrchestrator("[SwitchModel]:" + modelName);
    }

    public async Task SendInfoMessageToOrchestratorAsync(string message)
    {
        await this.sendInfoMessageToOrchestrator(message);
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

                    this.InputTokenCount += (int)usage.Details.InputTokenCount;
                    this.OutputTokenCount += (int)usage.Details.OutputTokenCount;
                }
            }

            yield return chunk.ToString();
            builder.Append(chunk.ToString());
        }

        this.chatHistory.AddAssistantMessage(builder.ToString());
    }
}


public class OrchestratorKernelPlugin
{

    private OrchestratorKernel _orchestratorKernel;
    public OrchestratorKernelPlugin(OrchestratorKernel orchestratorKernel)
    {
        this._orchestratorKernel = orchestratorKernel;
    }

    [KernelFunction("SendMessageToModel")]
    [Description("Sends a message to the model that you are currently evaluating.")]
    public async Task<string> SendMessageToModel(
        [Description("The message to send to the model.")]
        string message
    )
    {
        await this._orchestratorKernel.SendInfoMessageToOrchestratorAsync("[SendToModel]:" + message);

        return "The message has been sent to the model. Please wait until you get a response starting with [modelName].";
    }

    [KernelFunction("SetCurrentModel")]
    [Description("Sets the current large language model that is being evaluated by the orchestrator.")]
    public async Task SetCurrentModel(
        [Description("The name of the model that is to be evaluated.")]
        string modelName
    )
    {
        this._orchestratorKernel.SetCurrentModel(modelName);
        await Task.CompletedTask;
    }
}
