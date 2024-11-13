using System.Text;
using System.Text.Json;
using System.Net.Http;
using System.Net.Http.Json;

class Program
{
    private class OllamaRequest
    {
        public string model { get; set; } = "";
        public string prompt { get; set; } = "";
        public bool stream { get; set; } = true;
        public Dictionary<string, object>? options { get; set; }
    }

    private class OllamaResponse
    {
        public string response { get; set; } = "";
        public bool done { get; set; }
    }

    private static string CleanPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim().Trim('\'', '"').Trim();
    }

    static async Task Main(string[] args)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromMinutes(5)
        };

        var conversationHistory = new StringBuilder();

        while (true)
        {
            Console.WriteLine("\nOptions:");
            Console.WriteLine("1. Send text message");
            Console.WriteLine("2. Send image");
            Console.WriteLine("3. Exit");
            Console.Write("Choose option (1-3): ");

            var option = Console.ReadLine();

            switch (option)
            {
                case "1":
                    Console.Write("You: ");
                    var textMessage = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(textMessage))
                        continue;

                    var textRequest = new OllamaRequest
                    {
                        model = "llama3.2-vision",
                        prompt = textMessage,
                        options = new Dictionary<string, object>
                        {
                            ["temperature"] = 0.7
                        }
                    };

                    await SendRequest(httpClient, textRequest, conversationHistory);
                    break;

                case "2":
                    Console.Write("Enter image path: ");
                    var rawImagePath = Console.ReadLine();
                    var imagePath = CleanPath(rawImagePath);

                    if (string.IsNullOrWhiteSpace(imagePath))
                    {
                        Console.WriteLine("No path provided!");
                        continue;
                    }

                    if (!File.Exists(imagePath))
                    {
                        Console.WriteLine($"Image file not found at path: {imagePath}");
                        continue;
                    }

                    try
                    {
                        var imageBytes = await File.ReadAllBytesAsync(imagePath);
                        var base64Image = Convert.ToBase64String(imageBytes);

                        Console.WriteLine($"Successfully loaded image, size: {imageBytes.Length:N0} bytes");
                        Console.WriteLine("Enter your question about the image (e.g., 'What is in this image?', 'Describe the scene', etc.): ");
                        var imagePrompt = Console.ReadLine();

                        if (string.IsNullOrWhiteSpace(imagePrompt))
                        {
                            imagePrompt = "What is in this image? Please describe it in detail.";
                        }

                        var imageRequest = new Dictionary<string, object>
                        {
                            ["model"] = "llama3.2-vision",
                            ["prompt"] = imagePrompt,
                            ["images"] = new[] { base64Image },
                            ["stream"] = true,
                            ["options"] = new Dictionary<string, object>
                            {
                                ["temperature"] = 0.7
                            }
                        };

                        var jsonContent = new StringContent(
                            JsonSerializer.Serialize(imageRequest),
                            Encoding.UTF8,
                            "application/json"
                        );

                        Console.WriteLine("Sending request to model...");
                        var response = await httpClient.PostAsync("/api/generate", jsonContent);

                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            Console.WriteLine($"Error: {response.StatusCode}");
                            Console.WriteLine($"Response content: {errorContent}");
                            continue;
                        }

                        using var stream = await response.Content.ReadAsStreamAsync();
                        using var reader = new StreamReader(stream);

                        var fullResponse = new StringBuilder();
                        Console.Write("Bot: ");

                        while (!reader.EndOfStream)
                        {
                            var line = await reader.ReadLineAsync();
                            if (string.IsNullOrEmpty(line)) continue;

                            try
                            {
                                var streamResponse = JsonSerializer.Deserialize<OllamaResponse>(line);
                                if (streamResponse != null)
                                {
                                    Console.Write(streamResponse.response);
                                    fullResponse.Append(streamResponse.response);

                                    if (streamResponse.done)
                                    {
                                        break;
                                    }
                                }
                            }
                            catch (JsonException)
                            {
                                continue;
                            }
                        }

                        Console.WriteLine();
                        conversationHistory.AppendLine($"User: [Image shared] {imagePrompt}");
                        conversationHistory.AppendLine($"Assistant: {fullResponse}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing request: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                        }
                        Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                    }
                    break;

                case "3":
                    return;

                default:
                    Console.WriteLine("Invalid option!");
                    continue;
            }
        }
    }

    private static async Task SendRequest(HttpClient client, OllamaRequest request, StringBuilder history)
    {
        try
        {
            Console.WriteLine("Sending request to model...");
            var response = await client.PostAsJsonAsync("/api/generate", request);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var fullResponse = new StringBuilder();
            Console.Write("Bot: ");

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) continue;

                try
                {
                    var streamResponse = JsonSerializer.Deserialize<OllamaResponse>(line);
                    if (streamResponse != null)
                    {
                        Console.Write(streamResponse.response);
                        fullResponse.Append(streamResponse.response);

                        if (streamResponse.done)
                        {
                            break;
                        }
                    }
                }
                catch (JsonException)
                {
                    continue;
                }
            }

            Console.WriteLine();
            history.AppendLine($"User: {request.prompt}");
            history.AppendLine($"Assistant: {fullResponse}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in request: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
            }
        }
    }
}