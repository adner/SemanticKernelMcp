# SemanticKernelMcp

This is a demo of using the [Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/) to utilize the Dataverse MCP Server which is posted to [LinkedIn](https://www.linkedin.com/posts/andreas-adner-70b1153_benchmark-of-llms-using-dataverse-mcp-server-activity-7348442438119665665-lLhZ?utm_source=share&utm_medium=member_desktop&rcm=ACoAAACM8rsBEgQIrYgb4NZAbnxwfDRk_Tu5e3w).

Since the [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk) didn't work with the [Dataverse MCP Server](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-mcp) (see [this issue](https://github.com/modelcontextprotocol/csharp-sdk/issues/594)), I had to fork this repo and create a fix which can be found [here](https://github.com/adner/csharp-sdk) and that is also a submodule of this repo.

## Build and run the code
Clone the repo and initialize the submodule:

```git submodule update --init --recursive```

Make modifications in the following files:

- API Keys for OpenAI needs to be updated in `LlmChatHub.cs`.
- API Keys and deployment endpoints for models in Azure AI Foundry needs to be updated in `LlmChatHub.cs`.
The Dataverse MCP Server parameters in the method `getDataverseMcpClient` in `SemanticKernelMcpLib.cs` needs to be updated to reflect your connection.

Build everything: `dotnet build`

Run the web application: `dotnet run`

The demo can now be accessed on [https://localhost:7163/frame](https://localhost:7163/frame) . 

Individual models can be accessed on for example [https://localhost:7163/?model=o4-mini](https://localhost:7163/?model=o4-mini) .

