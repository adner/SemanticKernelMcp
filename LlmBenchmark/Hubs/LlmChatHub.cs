using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SemanticKernelMcpLib;
using Microsoft.SemanticKernel;

namespace SignalRChat.Hubs
{

    public class LlmChatHub : Hub
    {
        private static readonly ConcurrentDictionary<string, Channel<string>> _streams = new ConcurrentDictionary<string, Channel<string>>();

        public IAsyncEnumerable<string> LlmStream(string model,
        CancellationToken cancellationToken)
        {
            Context.Items["model"] = model;

            Kernel kernel = null;
            SimplifiedKernel myKernel = null;
            double costPerInputToken = 0, costPerOutputToken = 0;

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

            kernel.FunctionInvocationFilters.Add(new MyFunctionInvocationHandler(Context));

            myKernel = new SimplifiedKernel(kernel, costPerInputToken, costPerOutputToken, model);

            Context.Items["myKernel"] = myKernel;

            var channel = Channel.CreateUnbounded<string>();
            _streams[Context.ConnectionId] = channel;

            cancellationToken.Register(() =>
            {
                _streams.TryRemove(Context.ConnectionId, out _);
                channel.Writer.Complete();
            });

            return channel.Reader.ReadAllAsync(cancellationToken);
        }

        public async Task SendMessage(string message)
        {
            string model = (string)Context.Items["model"];
            SimplifiedKernel myKernel = (SimplifiedKernel)Context.Items["myKernel"];

            if (_streams.TryGetValue(Context.ConnectionId, out var channel))
            {
                await foreach (var chunk in myKernel.GetChatMessageStreamingAsync(message))
                {
                    await channel.Writer.WriteAsync(chunk);
                }

                await channel.Writer.WriteAsync($"Usage:{myKernel.InputTokenCount},{myKernel.OutputTokenCount},{myKernel.Cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
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


        Kernel create_Gpt41mini_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1-mini", apiKey: "sk-proj-PC...A3d4-9HrUM8IA");
            return builder.Build();
        }

        Kernel create_Gpt41nano_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1-nano", apiKey: "sk-proj-PC...A3d4-9HrUM8IA");
            return builder.Build();
        }

        Kernel create_Gpt41_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "gpt-4.1", apiKey: "sk-proj-PC...A3d4-9HrUM8IA");
            return builder.Build();
        }

        Kernel create_o4mini_Kernel()
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOpenAIChatCompletion(modelId: "o4-mini", apiKey: "sk-proj-PC...A3d4-9HrUM8IA");
            return builder.Build();
        }

        Kernel createAzureAiInference_DeepSeekR3_Kernel()
        {
#pragma warning disable SKEXP0070

            var endpoint = new Uri("https://ai-andreasadner0331ai924044154211.services.ai.azure.com/models");
            var apiKey = "2A1wbo...G4yCQ";
            var model = "DeepSeek-R1-0528";

            IKernelBuilder kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddAzureAIInferenceChatCompletion(modelId: model, apiKey: apiKey, endpoint: endpoint);
            Kernel kernel = kernelBuilder.Build();

            return kernel;
        }


        Kernel createAzureAiInference_Grok3_Kernel()
        {
#pragma warning disable SKEXP0070

            var endpoint = new Uri("https://ai-andreasadner0331ai924044154211.services.ai.azure.com/models");
            var apiKey = "2A1wbo...G4yCQ";
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