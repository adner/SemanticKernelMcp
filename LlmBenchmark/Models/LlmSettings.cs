namespace LlmBenchmark.Models
{
    public class LlmSettings
    {
        public OpenAISettings OpenAI { get; set; } = new();
        public AzureAISettings AzureAI { get; set; } = new();
    }

    public class OpenAISettings
    {
        public string ApiKey { get; set; } = string.Empty;
    }

    public class AzureAISettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
    }
}
