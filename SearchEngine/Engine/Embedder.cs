using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Pgvector;

public class Embedder(IEmbeddingGenerator<string, Embedding<float>> generator, HttpClient http)
{
    // query prefix configuration for retrieval/search
    private const string QueryPrefix = "task: search result | query: ";

    public async Task<Vector> Text(string text, bool isQuery = false)
    {
        string input = isQuery ? QueryPrefix + text : text;
        var embedding = await generator.GenerateVectorAsync(input);

        var floats = embedding.ToArray();
        Normalize(floats);

        return new Vector(floats);
    }

    public async Task<Vector> Image(byte[] image, string modelName = "embeddinggemma2", string mediaType = "image/png")
    {
        var dataUri = $"data:{mediaType};base64,{Convert.ToBase64String(image)}";

        var requestBody = new
        {
            model = modelName,
            input = new object[]
            {
                new
                {
                    content = new object[]
                    {
                        new
                        {
                            type = "image_url",
                            image_url = new { url = dataUri }
                        }
                    }
                }
            }
        };

        var response = await http.PostAsJsonAsync("/v1/embeddings", requestBody);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var embeddingArray = json.GetProperty("data")[0].GetProperty("embedding");

        var floats = embeddingArray.EnumerateArray()
            .Select(x => x.GetSingle())
            .ToArray();

        Normalize(floats);
        return new Vector(floats);
    }

    private static void Normalize(float[] vector)
    {
        float sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        float norm = (float)Math.Sqrt(sumSquares);
        if (norm > 0)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }
    }
}