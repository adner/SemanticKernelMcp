using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SemanticKernelMcpLib;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Options;
using LlmBenchmark.Models;
using System.ComponentModel;
using System.Text;

namespace SignalRChat.Hubs
{

    public class LlmChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, Channel<string>> _streams = new ConcurrentDictionary<string, Channel<string>>();
        private readonly LlmSettings _llmSettings;

        private static OrchestratorKernel _orchestratorKernel;
        private static string _orchestratorConnectionId;

        public LlmChatHub(IOptions<LlmSettings> llmSettings)
        {
            _llmSettings = llmSettings.Value;
        }

        public IAsyncEnumerable<string> LlmStream(string model,
        CancellationToken cancellationToken)
        {
            Context.Items["model"] = model;

            Kernel? kernel = null;
            SimplifiedKernel? myKernel = null;
            double costPerInputToken = 0, costPerOutputToken = 0;

            if (model == "orchestrator")
            {
                kernel = create_orchestrator_Kernel();
                costPerInputToken = 0.4 / 1000000;
                costPerOutputToken = 1.6 / 1000000;

                _orchestratorKernel = new OrchestratorKernel(kernel, costPerInputToken, costPerOutputToken, model, LlmChatHub.SendInfoMessageToOrchestratorChat);
            }
            else
            {
                if (model == "gpt-4.1-mini")
                {
                    kernel = create_Gpt41mini_Kernel();
                    costPerInputToken = 0.4 / 1000000;
                    costPerOutputToken = 1.6 / 1000000;
                }
                else if (model == "gpt-4.1")
                {
                    kernel = create_Gpt41_Kernel();
                    costPerInputToken = 2.0 / 1000000;
                    costPerOutputToken = 8.0 / 1000000;
                }
                else if (model == "o4-mini")
                {
                    kernel = create_o4mini_Kernel();
                    costPerInputToken = 1.1 / 1000000;
                    costPerOutputToken = 4.4 / 1000000;
                }
                else if (model == "grok-3-mini")
                {
                    kernel = createAzureAiInference_Grok3_Kernel();
                    costPerInputToken = 0.25 / 1000000;
                    costPerOutputToken = 1.27 / 1000000;
                }
                else if (model == "DeepSeek-R1")
                {
                    kernel = createAzureAiInference_DeepSeekR3_Kernel();
                    costPerInputToken = 1.35 / 1000000;
                    costPerOutputToken = 5.4 / 1000000;
                }
                else if (model == "gpt-4.1-nano")
                {
                    kernel = create_Gpt41nano_Kernel();
                    costPerInputToken = 0.1 / 1000000;
                    costPerOutputToken = 0.4 / 1000000;
                }

                myKernel = new SimplifiedKernel(kernel, costPerInputToken, costPerOutputToken, model);
             
                Context.Items["myKernel"] = myKernel;
            }

            if (kernel == null)
                    throw new InvalidOperationException($"Unsupported model: {model}");

            kernel.FunctionInvocationFilters.Add(new MyFunctionInvocationHandler(Context));

            var channel = Channel.CreateUnbounded<string>();
            _streams[Context.ConnectionId] = channel;

            if (model == "orchestrator")
            {
                _orchestratorConnectionId = Context.ConnectionId;
            }

            cancellationToken.Register(() =>
            {
                _streams.TryRemove(Context.ConnectionId, out _);
                channel.Writer.Complete();
            });

            return channel.Reader.ReadAllAsync(cancellationToken);
        }

        public async Task SendMessage(string message)
        {
            string? model = Context.Items["model"] as string;
            SimplifiedKernel? myKernel = Context.Items["myKernel"] as SimplifiedKernel;

            if (model == null || myKernel == null)
                return;

            if (_streams.TryGetValue(Context.ConnectionId, out var channel))
            {
                await foreach (var chunk in myKernel.GetChatMessageStreamingAsync(message))
                {
                    await channel.Writer.WriteAsync(chunk);
                }

                await channel.Writer.WriteAsync($"Usage:{myKernel.InputTokenCount},{myKernel.OutputTokenCount},{myKernel.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            await LlmChatHub.sendMessageToOrchestratorKernel("\n\n[" + model + "]:" + myKernel.ChatHistory.Last().Content);
        }

        public async Task SendMessageToOrchestratorKernel(string message)
        {
            await LlmChatHub.sendMessageToOrchestratorKernel(message);   
        }

        public static async Task sendMessageToOrchestratorKernel(string message)
        {
            if (_streams.TryGetValue(_orchestratorConnectionId, out var channel))
            {
                await foreach (var chunk in _orchestratorKernel.GetChatMessageStreamingAsync(message))
                {
                    await channel.Writer.WriteAsync(chunk);
                }

                await channel.Writer.WriteAsync($"Usage:{_orchestratorKernel.InputTokenCount},{_orchestratorKernel.OutputTokenCount},{_orchestratorKernel.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }

        public static async Task SendInfoMessageToOrchestratorChat(string message)
        {
            if (_streams.TryGetValue(_orchestratorConnectionId, out var channel))
            {    
                await channel.Writer.WriteAsync(message);
            }
        }

        public async Task ListTools()
        {
            if (_streams.TryGetValue(Context.ConnectionId, out var channel))
            {
                foreach (var tool in SimplifiedKernel.DisplayTools())
                {
                    await channel.Writer.WriteAsync(tool);
                    await Task.Delay(100);
                }
            }
        }

        Kernel create_orchestrator_Kernel()
        {        
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1", apiKey: _llmSettings.OpenAI.ApiKey);
            return builder.Build();
        }

        Kernel create_Gpt41mini_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1-mini", apiKey: _llmSettings.OpenAI.ApiKey);
            return builder.Build();
        }

        Kernel create_Gpt41nano_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1-nano", apiKey: _llmSettings.OpenAI.ApiKey);
            return builder.Build();
        }

        Kernel create_Gpt41_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1", apiKey: _llmSettings.OpenAI.ApiKey);
            return builder.Build();
        }

        Kernel create_o4mini_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "o4-mini", apiKey: _llmSettings.OpenAI.ApiKey);
            return builder.Build();
        }

        Kernel createAzureAiInference_DeepSeekR3_Kernel()
        {
#pragma warning disable SKEXP0070

            var endpoint = new Uri(_llmSettings.AzureAI.Endpoint);
            var apiKey = _llmSettings.AzureAI.ApiKey;
            var model = "DeepSeek-R1-0528";

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureAIInferenceChatCompletion(modelId: model, apiKey: apiKey, endpoint: endpoint);
            Kernel kernel = kernelBuilder.Build();

            return kernel;
        }


        Kernel createAzureAiInference_Grok3_Kernel()
        {
#pragma warning disable SKEXP0070

            var endpoint = new Uri(_llmSettings.AzureAI.Endpoint);
            var apiKey = _llmSettings.AzureAI.ApiKey;
            var model = "grok-3-mini";

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureAIInferenceChatCompletion(modelId: model, apiKey: apiKey, endpoint: endpoint);
            Kernel kernel = kernelBuilder.Build();

            return kernel;
        }

        class MyFunctionInvocationHandler : IFunctionInvocationFilter
        {
            private readonly HubCallerContext _hubContext;

            public MyFunctionInvocationHandler(HubCallerContext hubContext)
            {
                _hubContext = hubContext;
            }

            public async Task OnFunctionInvocationAsync(FunctionInvocationContext context, Func<FunctionInvocationContext, Task> next)
            {
                if (_streams.TryGetValue(_hubContext.ConnectionId, out var channel))
                {
                    var arguments = context.Arguments;
                    var toolCallMessage = $"<div class='toolCall'>{context.Function.Name}";

                    if (arguments.Any())
                    {
                        var args = string.Join(", ", arguments.Values);
                        toolCallMessage += $" - {args}";
                    }

                    toolCallMessage += "</div>";
                    await channel.Writer.WriteAsync(toolCallMessage);
                }

                await next(context); // Call the actual function
            }
        }
    }
}